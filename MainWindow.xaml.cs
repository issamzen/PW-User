using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProfessionalPowerCopyCatalogModern
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private const string ApiBaseUrl = "https://catalog.estichara.ma/mold-api/v1/";

        private INFITF.Application _catia;
        private MECMOD.PartDocument _destination;
        private MECMOD.PartDocument _source;
        private CatalogItem _selectedItem;
        private string _categoryFilter = "All";
        private readonly Dictionary<string, CatalogItem> _localTemplateConfig =
            new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);
        private readonly LicenseApiClient _api;
        private readonly PackageManager _packageManager;
        private readonly string _deviceId;
        private readonly DispatcherTimer _heartbeatTimer;
        private bool _isSignedIn;
        private string _activeLeaseToken;
        private string _organizationName;
        private LifterStudioWindow _lifterStudio;
        private CatalogItem _lifterSessionItem;
        private PwToolbarWindow _pwToolbar;
        private bool _lifterStudioRunning;
        private LicenseWindow _licenseWindow;
        private LibraryWindow _libraryWindow;

        /// <summary>Raised whenever the controller's status line changes, so the
        /// small tool windows can mirror it without owning any logic.</summary>
        public event Action<string> StatusChanged;

        /// <summary>Raised when the authorized catalog was reloaded.</summary>
        public event Action CatalogChanged;

        // Lifter (server-only) packages are wiped from this PC when the template
        // session ends, so the paid PowerCopy never stays on the customer's disk.
        // Set to false to keep the encrypted package cache between sessions.
        // (static readonly, not const, so the check never produces dead code)
        private static readonly bool WipeLifterPackageOnSessionEnd = true;

        public ObservableCollection<CatalogItem> CatalogItems { get; } = new ObservableCollection<CatalogItem>();
        public ObservableCollection<CheckRow> Results { get; } = new ObservableCollection<CheckRow>();

        public CatalogItem SelectedItem
        {
            get => _selectedItem;
            private set
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            Branding.ApplyLogo(BrandLogo, BrandInitials);
            Branding.ApplyLogo(LoginBrandLogo, LoginBrandInitials);

            _api = new LicenseApiClient(ApiBaseUrl);
            _packageManager = new PackageManager();
            _deviceId = DeviceIdentity.GetOrCreate();
            _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _heartbeatTimer.Tick += async (_, __) => await RenewLeaseSilentlyAsync();

            // The product is the toolbar: this window is only the engine
            // (licence, catalog, packages, CATIA). It is never shown.
            ShowInTaskbar = false;
            Loaded += delegate
            {
                Hide();
                ShowPwToolbar();
            };

            // Mirror the status line to the tool windows.
            var statusProperty = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                TextBlock.TextProperty, typeof(TextBlock));
            if (statusProperty != null)
            {
                statusProperty.AddValueChanged(StatusText, delegate
                {
                    Action<string> handler = StatusChanged;
                    if (handler != null) handler(StatusText.Text);
                });
            }

            LoadLocalTemplateConfiguration();
            LoadLifterOverrides();
            ICollectionView view = CollectionViewSource.GetDefaultView(CatalogItems);
            view.Filter = FilterCatalogItem;
            ConnectToCatia();
            Closing += async (_, __) => await ReleaseLeaseSilentlyAsync();

            // Remember me: prefill and auto-sign-in if credentials were stored.
            var remembered = CredentialStore.Load();
            if (remembered != null)
            {
                LoginEmailBox.Text = remembered.email;
                LoginPasswordBox.Password = remembered.password;
                RememberMeCheck.IsChecked = true;
                Loaded += async (_, __) =>
                {
                    if (!string.IsNullOrEmpty(LoginEmailBox.Text) && !string.IsNullOrEmpty(LoginPasswordBox.Password))
                        await DoLoginAsync();
                };
            }
        }

        private void LoadLocalTemplateConfiguration()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.json");
            if (!File.Exists(path)) return;

            var serializer = new JavaScriptSerializer();
            List<CatalogItem> items = serializer.Deserialize<List<CatalogItem>>(File.ReadAllText(path));
            if (items == null) return;

            foreach (CatalogItem item in items)
            {
                item.ThumbnailFullPath = ResolveThumbnail(item.Thumbnail);
                _localTemplateConfig[(item.Id ?? string.Empty).Trim()] = item;
            }
        }

        /// <summary>Matches a server catalog row to its local catalog.json entry:
        /// by template Id (trimmed, case-insensitive), then by PowerCopy name.
        /// Server rows sometimes use a different template Id than the local
        /// development entry; the local entry is also what flags a template as
        /// Workflow = "lifter" until the server catalog or the package manifest
        /// carry the flag themselves.</summary>
        private CatalogItem FindLocalConfig(CatalogItem serverItem)
        {
            if (serverItem == null) return null;

            string id = (serverItem.Id ?? string.Empty).Trim();
            if (id.Length > 0 && _localTemplateConfig.TryGetValue(id, out CatalogItem byId))
                return byId;

            string powerCopy = (serverItem.PowerCopyName ?? string.Empty).Trim();
            if (powerCopy.Length > 0)
            {
                foreach (CatalogItem local in _localTemplateConfig.Values)
                {
                    if (string.Equals(NormalizeKey(local.PowerCopyName), NormalizeKey(powerCopy), StringComparison.Ordinal))
                        return local;
                }
            }

            // Last resort: the display name ("name" is a single-word JSON key, so
            // it always deserializes, unlike snake_case keys such as
            // "powercopy_name"). Catches the case where the server row uses a
            // completely different template Id than the local catalog.json entry.
            string name = (serverItem.Name ?? string.Empty).Trim();
            if (name.Length > 0)
            {
                foreach (CatalogItem local in _localTemplateConfig.Values)
                {
                    if (string.Equals(NormalizeKey(local.Name), NormalizeKey(name), StringComparison.Ordinal))
                        return local;
                }
            }
            return null;
        }

        /// <summary>Comparison key that survives case, spacing and punctuation
        /// differences between the server row and the local catalog.json entry
        /// ("Dog house lifter" == "DOGHOUSE-LIFTER").</summary>
        private static string NormalizeKey(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();
            foreach (char c in new[] { ' ', '-', '_', '.' })
            {
                text = text.Replace(c.ToString(), string.Empty);
            }
            return text;
        }

        private async Task LoadAuthorizedCatalogAsync()
        {
            ServerCatalogResponse response = await _api.GetCatalogAsync();
            string previousSelectedId = SelectedItem?.Id != null ? SelectedItem.Id.Trim().ToUpperInvariant() : null;
            CatalogItems.Clear();

            foreach (CatalogItem serverItem in response.Items ?? new List<CatalogItem>())
            {
                // Cards the user flagged manually with the "STROKE setup"
                // button (persisted locally) always run the lifter workflow.
                if (!string.IsNullOrWhiteSpace(serverItem.Id) &&
                    _lifterOverrides.Contains(serverItem.Id.Trim()))
                {
                    serverItem.Workflow = "lifter";
                }

                CatalogItem local = FindLocalConfig(serverItem);
                if (local != null)
                {
                    serverItem.CatPartPath = local.CatPartPath;
                    serverItem.CheckScriptDirectory = local.CheckScriptDirectory;
                    serverItem.CheckScriptFile = local.CheckScriptFile;
                    serverItem.CheckFunction = local.CheckFunction;
                    serverItem.IsFavorite = local.IsFavorite;
                    if (!string.IsNullOrWhiteSpace(local.Workflow)) serverItem.Workflow = local.Workflow;
                }

                serverItem.ThumbnailFullPath = ResolveThumbnail(serverItem.Thumbnail);
                CatalogItems.Add(serverItem);
            }

            CollectionViewSource.GetDefaultView(CatalogItems)?.Refresh();
            if (CatalogItems.Count > 0)
            {
                if (previousSelectedId != null)
                {
                    CatalogItem prior = CatalogItems.FirstOrDefault(
                        c => string.Equals(c.Id.Trim(), previousSelectedId, StringComparison.OrdinalIgnoreCase));
                    if (prior != null)
                    {
                        CatalogList.SelectedItem = prior;
                        CatalogList.ScrollIntoView(prior);
                    }
                    else
                    {
                        CatalogList.SelectedIndex = 0;
                    }
                }
                else
                {
                    CatalogList.SelectedIndex = 0;
                }
            }
        }

        private async void RefreshCatalogButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!_isSignedIn) return;
            RefreshCatalogButton.IsEnabled = false;
            bool hadSelection = SelectedItem != null;
            try
            {
                StatusText.Text = "Checking the server for new or updated Power Copies…";
                await LoadAuthorizedCatalogAsync();
                StatusText.Text = hadSelection
                    ? "Catalog refreshed from the license server."
                    : "Your account has no authorized templates.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Refresh failed: " + FriendlyApiError(ex.Message);
            }
            finally
            {
                RefreshCatalogButton.IsEnabled = _isSignedIn;
            }
        }

        private static string ResolveThumbnail(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri remote)) return remote.ToString();
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value ?? string.Empty);
        }

        private bool FilterCatalogItem(object value)
        {
            if (!(value is CatalogItem item)) return false;
            string query = SearchBox?.Text?.Trim() ?? string.Empty;

            bool categoryMatches = _categoryFilter == "All" ||
                (_categoryFilter == "Favorites" && item.IsFavorite) ||
                string.Equals(item.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase);

            if (!categoryMatches) return false;
            if (query.Length == 0) return true;

            return Contains(item.Name, query) ||
                   Contains(item.Category, query) ||
                   Contains(item.Description, query) ||
                   Contains(item.Id, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async void LoginButton_OnClick(object sender, RoutedEventArgs e)
            => await DoLoginAsync();

        private async Task DoLoginAsync()
        {
            LoginButton.IsEnabled = false;
            LoginStatusText.Text = "Signing in…";

            try
            {
                LoginResponse response = await _api.LoginAsync(
                    LoginEmailBox.Text.Trim(),
                    LoginPasswordBox.Password,
                    _deviceId,
                    Environment.MachineName);

                if (RememberMeCheck.IsChecked == true)
                    CredentialStore.Save(LoginEmailBox.Text.Trim(), LoginPasswordBox.Password);
                else
                    CredentialStore.Clear();
                LoginPasswordBox.Clear();
                LoginOverlay.Visibility = Visibility.Collapsed;
                AboutUserNameText.Text = response.user.display_name;
                _organizationName = response.user.organization;
                AboutUserPlanText.Text = "Licensed user" +
                                         " • " + (_organizationName ?? "Individual workspace");

                _isSignedIn = true;
                await RefreshLicenseDisplayAsync();
                StatusText.Text = "Loading your authorized catalog…";
                await LoadAuthorizedCatalogAsync();
                StatusText.Text = CatalogItems.Count == 0
                    ? "Your account has no authorized templates."
                    : "Authorized catalog synchronized with the license server.";
                ShowLibraryWorkspace();
                ShowPwToolbar();
            }
            catch (Exception ex)
            {
                LoginStatusText.Text = FriendlyApiError(ex.Message);
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private async Task RefreshLicenseDisplayAsync()
        {
            try
            {
                // Fetch the current license(s) so we can show how much time is left.
                MeResponse me = await _api.GetMeAsync();
                MeLicense license = me?.licenses?.Count > 0
                    ? me.licenses.OrderByDescending(l => l.expires_at).First()
                    : null;

                if (license == null)
                {
                    AccessDot.Fill = BrushFrom("#DC2626");
                    AccessStatusText.Text = "No license";
                    AccessStatusText.Foreground = BrushFrom("#FCA5A5");
                    LicenseTimeText.Text = "No active license";
                    AboutUserPlanText.Text = "Licensed user • no active license";
                    return;
                }

                bool expired = IsPast(license.expires_at);
                if (expired)
                {
                    AccessDot.Fill = BrushFrom("#DC2626");
                    AccessStatusText.Text = "Expired";
                    AccessStatusText.Foreground = BrushFrom("#FCA5A5");
                    LicenseTimeText.Text = "License expired";
                    AboutUserPlanText.Text = "Licensed user • license expired";
                    return;
                }

                string timeLeft = DescribeTimeLeft(license.expires_at);
                AccessDot.Fill = BrushFrom("#22C55E");
                AccessStatusText.Text = "Licensed";
                AccessStatusText.Foreground = BrushFrom("#D1FAE5");
                LicenseTimeText.Text = license.license_name + " • " + timeLeft;
                AboutUserPlanText.Text = "Licensed user • " + (_organizationName ?? "Individual") +
                                         " • " + timeLeft;
            }
            catch
            {
                AccessDot.Fill = BrushFrom("#F59E0B");
                AccessStatusText.Text = "Offline";
                AccessStatusText.Foreground = BrushFrom("#FCD34D");
                LicenseTimeText.Text = "Could not check license";
            }
        }

        private static bool IsPast(string utcDateTime)
        {
            if (DateTimeOffset.TryParse(utcDateTime, out DateTimeOffset d) && d != default)
                return d <= DateTimeOffset.UtcNow;
            return false;
        }

        private static string DescribeTimeLeft(string utcDateTime)
        {
            if (!DateTimeOffset.TryParse(utcDateTime, out DateTimeOffset d) || d == default)
                return "—";
            TimeSpan left = d - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero) return "expired";
            if (left.TotalDays >= 1)
                return string.Format("{0:F0} day(s) left", Math.Ceiling(left.TotalDays));
            if (left.TotalHours >= 1)
                return string.Format("{0:F0} hour(s) left", Math.Ceiling(left.TotalHours));
            return string.Format("{0:F0} min left", Math.Ceiling(left.TotalMinutes));
        }

        private static string FriendlyApiError(string value)
        {
            switch (value)
            {
                case "invalid_credentials": return "Email or password is incorrect.";
                case "server_error": return "The license server reported an internal error.";
                case "invalid_device_id": return "The local device identity is invalid.";
                case "template_not_entitled": return "Your account is not entitled to this template.";
                case "no_seat_available": return "All licensed seats are currently in use.";
                case "package_not_authorized_or_not_available": return "The server package is not published for this template yet.";
                case "package_file_missing": return "The package is configured in MySQL but the file is missing from Hostinger private storage.";
                case "package_hash_missing": return "The package SHA-256 hash is missing from the server response.";
                case "package_integrity_error": return "Package integrity verification failed. Upload the package again and update its SHA-256 value.";
                case "package_decrypt_error": return "The package could not be decrypted (missing or incorrect key). Re-upload the package and re-sync the database.";
                case "invalid_or_expired_lease": return "The license seat expired. Start the template again.";
                default: return value;
            }
        }

        private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(CatalogItems)?.Refresh();
        }

        private void CategoryButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button selectedButton)) return;

            AllCategoryButton.Tag = null;
            DogHousesCategoryButton.Tag = null;
            ClipsCategoryButton.Tag = null;
            RibsCategoryButton.Tag = null;
            BossesCategoryButton.Tag = null;
            LiftersCategoryButton.Tag = null;
            FavoritesCategoryButton.Tag = null;
            selectedButton.Tag = "Selected";

            if (selectedButton == AllCategoryButton) _categoryFilter = "All";
            else if (selectedButton == FavoritesCategoryButton) _categoryFilter = "Favorites";
            else _categoryFilter = selectedButton.Content?.ToString() ?? "All";

            CollectionViewSource.GetDefaultView(CatalogItems)?.Refresh();

            if (CatalogList.Items.Count > 0)
                CatalogList.SelectedIndex = 0;
            else
                SelectedItem = null;
        }

        private void FavoriteButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is CatalogItem item)
            {
                item.IsFavorite = !item.IsFavorite;
                if (_categoryFilter == "Favorites")
                    CollectionViewSource.GetDefaultView(CatalogItems)?.Refresh();
                e.Handled = true;
            }
        }

        private void PowerCopiesNavButton_OnClick(object sender, RoutedEventArgs e)
        {
            ShowLibraryWorkspace();
            // Also brings the floating PW toolbar back when the user hid it.
            if (_isSignedIn) ShowPwToolbar();
        }

        private void AboutNavButton_OnClick(object sender, RoutedEventArgs e)
        {
            PowerCopiesNavButton.Tag = null;
            AboutNavButton.Tag = "Selected";
            CatalogPane.Visibility = Visibility.Collapsed;
            DetailPane.Visibility = Visibility.Collapsed;
            AboutWorkspace.Visibility = Visibility.Visible;
            AboutCatiaStatusText.Text = _catia == null ? "Not connected" : "Connected";
            AboutDocumentText.Text = _destination == null ? "No active CATPart" : GetDocumentName(_destination);
        }

        private void BackToLibraryButton_OnClick(object sender, RoutedEventArgs e)
        {
            ShowLibraryWorkspace();
        }

        private async void SignOutButton_OnClick(object sender, RoutedEventArgs e)
        {
            try { _lifterStudio?.Close(); } catch { }
            _lifterStudio = null;
            CloseSourceDocument();
            await ReleaseLeaseSilentlyAsync();
            _isSignedIn = false;
            _api.LogoutLocal();
            CatalogItems.Clear();
            Results.Clear();
            AboutWorkspace.Visibility = Visibility.Collapsed;
            LoginStatusText.Text = string.Empty;
            LoginOverlay.Visibility = Visibility.Visible;
        }

        private void ShowLibraryWorkspace()
        {
            PowerCopiesNavButton.Tag = "Selected";
            AboutNavButton.Tag = null;
            AboutWorkspace.Visibility = Visibility.Collapsed;
            CatalogPane.Visibility = Visibility.Visible;
            DetailPane.Visibility = Visibility.Visible;
            CatalogList.Focus();
            if (CatalogList.SelectedItem != null)
                CatalogList.ScrollIntoView(CatalogList.SelectedItem);
        }

        private void CatalogList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedItem = CatalogList.SelectedItem as CatalogItem;
            Results.Clear();
            OverallStatusText.Text = "NOT RUN";
            // Visible proof that the lifter workflow flag resolved for this card —
            // if this badge is missing on a lifter template, "Use in CATIA" would
            // run the plain PowerCopy flow without the STROKE_Distance setup.
            LifterWorkflowBadge.Visibility = IsLifterTemplate(SelectedItem)
                ? Visibility.Visible
                : Visibility.Collapsed;
            TemplateMetaText.Text = BuildTemplateMetaText();
            StatusText.Text = SelectedItem == null
                ? "Select a verified engineering template."
                : "Ready. Use the template in CATIA, then run its integrated check.";
            UseInCatiaButton.IsEnabled = SelectedItem != null;
            RunCheckButton.IsEnabled = false;
            PackageStatusText.Text = SelectedItem == null
                ? "Select a template."
                : _packageManager.IsCached(SelectedItem)
                    ? "Package v" + SelectedItem.Version + " is cached on this PC."
                    : "Package v" + SelectedItem.Version + " will download from Hostinger on first use.";
            ClearPackageCacheButton.IsEnabled = SelectedItem != null && _packageManager.IsCached(SelectedItem);
        }

        private void ConnectToCatia()
        {
            try
            {
                _catia = (INFITF.Application)Marshal.GetActiveObject("CATIA.Application");
                // A running CATIA with no document open is still a valid
                // connection - do not treat the missing document as "offline".
                try { _destination = (MECMOD.PartDocument)_catia.ActiveDocument; }
                catch { _destination = null; }

                ConnectionPill.Background = BrushFrom("#DCFCE7");
                ConnectionDot.Fill = BrushFrom("#16A34A");
                ConnectionText.Foreground = BrushFrom("#166534");
                ConnectionText.Text = "CATIA connected";
                UseInCatiaButton.IsEnabled = SelectedItem != null;
            }
            catch (Exception ex)
            {
                _catia = null;
                _destination = null;
                ConnectionPill.Background = BrushFrom("#FEE2E2");
                ConnectionDot.Fill = BrushFrom("#DC2626");
                ConnectionText.Foreground = BrushFrom("#991B1B");
                ConnectionText.Text = "CATIA offline";
                // Keep the action available so the user can start CATIA and click again.
                UseInCatiaButton.IsEnabled = SelectedItem != null;
                StatusText.Text = ex.Message;
            }
        }

        /// <summary>Returns true when a live CATIA connection is available.
        /// If CATIA was closed and restarted since the last connection, the old
        /// COM proxy points to a dead process and every call would fail with an
        /// HRESULT error - this detects it and re-attaches automatically.</summary>
        private bool EnsureCatiaConnection()
        {
            if (_catia != null)
            {
                try
                {
                    INFITF.Document probe = _catia.ActiveDocument; // liveness probe
                    return true;
                }
                catch
                {
                    _catia = null; // stale proxy from a previous CATIA session
                }
            }
            ConnectToCatia();
            return _catia != null;
        }

        private async void UseInCatiaButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedItem == null) throw new InvalidOperationException("Select a catalog item.");
                if (!EnsureCatiaConnection()) return;

                bool isLifter = IsLifterTemplate(SelectedItem);
                if (isLifter)
                {
                    // ============================================================
                    // The lifter workflow is the Lifter Studio macro itself: it
                    // runs INSIDE CATIA on the ACTIVE CATPart, so the document
                    // is validated here, before any license seat is taken and
                    // before the secure package is downloaded.
                    // ============================================================
                    if (!(_catia.ActiveDocument is MECMOD.PartDocument))
                        throw new InvalidOperationException(
                            "Open (or activate) the destination CATPart in CATIA first — the lifter script reads and creates STROKE_Distance on it.");
                    _destination = (MECMOD.PartDocument)_catia.ActiveDocument;
                }

                UseInCatiaButton.IsEnabled = false;
                StatusText.Text = "Requesting a license seat…";
                LeaseResponse lease = await _api.AcquireLeaseAsync(
                    SelectedItem.Id,
                    _deviceId,
                    Environment.MachineName);
                _activeLeaseToken = lease.lease_token;
                _heartbeatTimer.Start();

                bool usedServerPackage = false;
                try
                {
                    StatusText.Text = "Checking the secure Hostinger package…";
                    var progress = new Progress<PackageDownloadProgress>(value =>
                    {
                        if (value.TotalBytes.HasValue && value.TotalBytes.Value > 0)
                        {
                            PackageStatusText.Text = string.Format(
                                "Downloading {0}% • {1:F1} / {2:F1} MB",
                                value.Percent,
                                value.BytesReceived / 1024d / 1024d,
                                value.TotalBytes.Value / 1024d / 1024d);
                        }
                        else
                        {
                            PackageStatusText.Text = string.Format(
                                "Downloading • {0:F1} MB received",
                                value.BytesReceived / 1024d / 1024d);
                        }
                    });

                    PreparedPackage package = await _packageManager.PrepareAsync(
                        SelectedItem,
                        _api,
                        _activeLeaseToken,
                        progress);

                    ApplyPreparedPackage(SelectedItem, package);
                    PackagePerformanceLog.Append(SelectedItem, package);
                    usedServerPackage = true;
                    ClearPackageCacheButton.IsEnabled = true;

                    if (package.FromCache)
                    {
                        PackageStatusText.Text = string.Format(
                            "Loaded package v{0} from local cache in {1:F0} ms.",
                            SelectedItem.Version,
                            package.PreparationSeconds * 1000d);
                    }
                    else
                    {
                        PackageStatusText.Text = string.Format(
                            "Downloaded {0:F1} MB in {1:F2} s • {2:F1} MB/s",
                            package.Transfer.Megabytes,
                            package.Transfer.Seconds,
                            package.Transfer.MegabytesPerSecond);
                    }
                }
                catch (InvalidOperationException packageError)
                {
                    // Lifter templates are server-only: no local development fallback,
                    // the PowerCopy must always come from the licensed server package.
                    if (!isLifter &&
                        IsPackageUnavailableError(packageError.Message) &&
                        HasLocalRuntimeFiles(SelectedItem))
                    {
                        PackageStatusText.Text = "Server package is not published yet; using local development files.";
                    }
                    else
                    {
                        throw;
                    }
                }

                // The package manifest may carry the workflow flag even when the
                // server catalog / catalog.json do not: re-check after download.
                isLifter = IsLifterTemplate(SelectedItem);

                if (!HasLocalRuntimeFiles(SelectedItem))
                {
                    throw new FileNotFoundException(
                        usedServerPackage
                            ? "The downloaded package does not contain usable CATIA files."
                            : "No server package or local CATPart/CATScript is available for this template.");
                }

                if (isLifter)
                {
                    // ========================================================
                    // LIFTER WORKFLOW = the licensed Lifter Studio macro.
                    // The package (and the license seat) are already secured,
                    // so the macro now runs INSIDE CATIA: one-time
                    // STROKE_Distance measurement, STROKE link, Draft
                    // formulas, the parameter dashboard, the Boolean Remove
                    // card and the PowerCopy hand-off.
                    // ========================================================
                    if (_destination == null)
                    {
                        if (!(_catia.ActiveDocument is MECMOD.PartDocument))
                            throw new InvalidOperationException(
                                "Open (or activate) the destination CATPart in CATIA first — the lifter script reads and creates STROKE_Distance on it.");
                        _destination = (MECMOD.PartDocument)_catia.ActiveDocument;
                    }

                    _lifterSessionItem = SelectedItem;
                    StatusText.Text = "Lifter Studio is running inside CATIA — use the dashboard on the CATIA side…";
                    OverallStatusText.Text = "AWAITING CATIA";
                    NativeWindows.FocusCatia();

                    _lifterStudioRunning = true;
                    string macroResult;
                    try { macroResult = await RunLifterStudioScriptAsync(); }
                    finally { _lifterStudioRunning = false; }
                    ApplyLifterStudioResult(macroResult);
                    ToastWindow.Show("LIFTER STUDIO", StatusText.Text);

                    RunCheckButton.IsEnabled = true;
                    return;
                }

                StatusText.Text = "Package ready. Opening the CATIA template…";
                // Non-lifter flow: always capture the active document (as before).
                _destination = (MECMOD.PartDocument)_catia.ActiveDocument;

                _source = (MECMOD.PartDocument)_catia.Documents.Open(SelectedItem.CatPartPath);
                _source.Activate();

                INFITF.AnyObject reference = FindAndSelectReference(_source, SelectedItem.PowerCopyName);
                if (reference == null)
                    throw new InvalidOperationException("Power Copy not found: " + SelectedItem.PowerCopyName);

                _destination.Activate();
                _catia.StartCommand("Instantiate From Selection");
                _catia.RefreshDisplay = true;

                RunCheckButton.IsEnabled = true;
                CloseSourceButton.IsEnabled = true;
                OverallStatusText.Text = "AWAITING CATIA";

                StatusText.Text = "Complete CATIA's native Insert Object dialog. After clicking OK, return here and choose Run check.";
            }
            catch (Exception ex)
            {
                CloseSourceDocument();
                await ReleaseLeaseSilentlyAsync();
                MessageBox.Show(FriendlyApiError(ex.Message), "Use in CATIA", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UseInCatiaButton.IsEnabled = SelectedItem != null;
            }
        }

        // ================================================================
        // Public controller API used by the toolbar and the tool windows
        // ================================================================

        public bool IsSignedIn { get { return _isSignedIn; } }

        public bool CatiaConnected { get { return _catia != null; } }

        public string AccountName
        {
            get { return string.IsNullOrWhiteSpace(AboutUserNameText.Text) ? "Licensed user" : AboutUserNameText.Text; }
        }

        public string LicenseSummary
        {
            get { return string.IsNullOrWhiteSpace(LicenseTimeText.Text) ? "Licence status unknown" : LicenseTimeText.Text; }
        }

        public string RememberedEmail { get { return LoginEmailBox.Text; } }

        public string OverallStatus { get { return OverallStatusText.Text; } }

        public string DestinationName
        {
            get { return _destination == null ? "No destination CATPart" : GetDocumentName(_destination); }
        }

        public bool IsLifter(CatalogItem item) { return IsLifterTemplate(item); }

        public string DescribePackage(CatalogItem item)
        {
            if (item == null) return string.Empty;
            return _packageManager.IsCached(item)
                ? "Package v" + item.Version + " is ready on this PC."
                : "Package v" + item.Version + " will be downloaded securely on first use.";
        }

        /// <summary>Activation from the small licence panel. Returns null on
        /// success, or the message to display.</summary>
        public async Task<string> SignInAsync(string email, string password, bool remember)
        {
            LoginEmailBox.Text = email;
            LoginPasswordBox.Password = password;
            RememberMeCheck.IsChecked = remember;
            LoginStatusText.Text = string.Empty;

            await DoLoginAsync();

            if (_isSignedIn)
            {
                Action handler = CatalogChanged;
                if (handler != null) handler();
                return null;
            }
            return string.IsNullOrWhiteSpace(LoginStatusText.Text)
                ? "The licence could not be verified."
                : LoginStatusText.Text;
        }

        public async Task RefreshLicenseAsync()
        {
            await RefreshLicenseDisplayAsync();
        }

        public async Task RefreshCatalogAsync()
        {
            if (!_isSignedIn) return;
            await LoadAuthorizedCatalogAsync();
            Action handler = CatalogChanged;
            if (handler != null) handler();
        }

        public void SignOut()
        {
            SignOutButton_OnClick(this, null);
            Action handler = CatalogChanged;
            if (handler != null) handler();
        }

        public void SelectItem(CatalogItem item)
        {
            CatalogList.SelectedItem = item;
        }

        public void UseInCatia(CatalogItem item)
        {
            SelectItem(item);
            // The library steps out of the way: the work continues in CATIA.
            if (_libraryWindow != null) _libraryWindow.Hide();
            NativeWindows.FocusCatia();
            UseInCatiaButton_OnClick(this, null);
        }

        public void RunCheck()
        {
            RunCheckButton_OnClick(this, null);
        }

        // ================================================================
        // PW-User floating toolbar (the four product commands, docked to
        // the CATIA window - no CATIA customization needed on the client)
        // ================================================================

        /// <summary>Closes the whole product from the toolbar: the panels are
        /// allowed to close (they normally only hide), then the app exits.</summary>
        public void ShutdownProduct()
        {
            try
            {
                if (_licenseWindow != null) { _licenseWindow.ForceClose = true; _licenseWindow.Close(); }
                if (_libraryWindow != null) { _libraryWindow.ForceClose = true; _libraryWindow.Close(); }
                CheckerWindow checker = CheckerWindow.Current;
                if (checker != null) { checker.ForceClose = true; checker.Close(); }
                _licenseWindow = null;
                _libraryWindow = null;
                ClosePwToolbar();
            }
            catch { }
            try { Application.Current.Shutdown(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            ClosePwToolbar();
            base.OnClosed(e);
        }

        private void ShowPwToolbar()
        {
            try
            {
                if (_pwToolbar == null)
                {
                    _pwToolbar = new PwToolbarWindow(
                        PwToolbarLicense,
                        PwToolbarStroke,
                        PwToolbarLibrary,
                        PwToolbarParameters,
                        PwToolbarChecker,
                        ShutdownProduct);
                    _pwToolbar.Closed += delegate { _pwToolbar = null; };
                }
                _pwToolbar.Show();
            }
            catch
            {
                // The toolbar is a convenience: never block the sign-in for it.
            }
        }

        private void ClosePwToolbar()
        {
            try
            {
                if (_pwToolbar != null)
                {
                    _pwToolbar.Close();
                    _pwToolbar = null;
                }
            }
            catch { }
        }

        /// <summary>Toolbar 1/4 - licence: the small activation panel.</summary>
        private void PwToolbarLicense()
        {
            if (_licenseWindow == null) _licenseWindow = new LicenseWindow(this);
            _licenseWindow.Refresh();
            PlaceNextToToolbar(_licenseWindow, 430);
            _licenseWindow.Show();
            _licenseWindow.Activate();
        }

        /// <summary>Puts a tool panel next to the toolbar, inside the screen.</summary>
        private void PlaceNextToToolbar(Window window, double assumedHeight)
        {
            try
            {
                if (window.IsVisible) return;    // keep the position the user chose
                double right = _pwToolbar != null && _pwToolbar.IsVisible
                    ? _pwToolbar.Left
                    : SystemParameters.WorkArea.Right - 80;
                window.Left = Math.Max(SystemParameters.WorkArea.Left + 10, right - window.Width - 12);
                window.Top = Math.Max(SystemParameters.WorkArea.Top + 10,
                    Math.Min(SystemParameters.WorkArea.Bottom - assumedHeight - 10,
                             (_pwToolbar != null ? _pwToolbar.Top : 120)));
            }
            catch { }
        }

        /// <summary>Toolbar 2/4 - stroke: runs the STROKE_Distance script
        /// inside CATIA on the active CATPart.</summary>
        private async void PwToolbarStroke()
        {
            if (!_isSignedIn)
            {
                ToastWindow.Show("LICENCE REQUIRED", "Activate your licence first.", true);
                PwToolbarLicense();
                return;
            }

            ToastWindow progress = null;
            try
            {
                if (!EnsureCatiaConnection())
                    throw new InvalidOperationException("CATIA is not running.");
                if (!(_catia.ActiveDocument is MECMOD.PartDocument))
                    throw new InvalidOperationException(
                        "Open (or activate) the destination CATPart in CATIA first.");

                // The bounding box measurement takes a few seconds: tell the
                // user something is happening, without blocking the toolbar.
                progress = ToastWindow.ShowProgress("STROKE TOOL",
                    "Measuring the main body in CATIA…");

                string scriptPath = LifterEngine.WriteStrokeSetupScript();
                string result = await RunCatiaScriptAsync(scriptPath, "CATMain", false);

                if (result.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(result.Substring(6).Trim());

                string message;
                if (result.StartsWith("EXISTS:", StringComparison.OrdinalIgnoreCase))
                    message = "STROKE_Distance = " + result.Substring(7) + " mm (already on the CATPart).";
                else if (result.StartsWith("CREATED:", StringComparison.OrdinalIgnoreCase))
                    message = "STROKE_Distance = " + result.Substring(8) + " mm created.";
                else if (string.Equals(result, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    message = "Stroke setup cancelled.";
                else
                    message = result;

                StatusText.Text = message;
                if (progress != null) progress.Complete("STROKE TOOL", message);
                else ToastWindow.Show("STROKE TOOL", message);
            }
            catch (Exception ex)
            {
                string message = FriendlyApiError(ex.Message);
                StatusText.Text = message;
                if (progress != null) progress.Complete("STROKE TOOL", message, true);
                else ToastWindow.Show("STROKE TOOL", message, true);
            }
        }

        /// <summary>Toolbar 4/5 - lifter parameters: reopens the CATIA-side
        /// dashboard (parameters, Draft, Boolean Remove, PowerCopy hand-off) on
        /// the CATPart that is already open, WITHOUT going through the library
        /// again. This is the tool needed for a second Boolean Remove after a
        /// PowerCopy has been inserted: no new seat, no new download, the paid
        /// content is already in the part.</summary>
        private async void PwToolbarParameters()
        {
            if (!_isSignedIn)
            {
                ToastWindow.Show("LICENCE REQUIRED", "Activate your licence first.", true);
                PwToolbarLicense();
                return;
            }

            if (_lifterStudioRunning)
            {
                ToastWindow.Show("LIFTER PARAMETERS",
                    "The CATIA dashboard is already open - finish or close it first.", true);
                NativeWindows.FocusCatia();
                return;
            }

            try
            {
                if (!EnsureCatiaConnection())
                    throw new InvalidOperationException("CATIA is not running.");
                if (!(_catia.ActiveDocument is MECMOD.PartDocument))
                    throw new InvalidOperationException(
                        "Open (or activate) the CATPart that holds the lifter instances in CATIA first.");

                _destination = (MECMOD.PartDocument)_catia.ActiveDocument;
                _lifterStudioRunning = true;

                if (_libraryWindow != null) _libraryWindow.Hide();
                StatusText.Text = "Lifter parameters dashboard is running inside CATIA…";
                ToastWindow.Show("LIFTER PARAMETERS", "The dashboard is opening in CATIA…");
                NativeWindows.FocusCatia();

                string result = await RunLifterStudioScriptAsync();
                ApplyLifterStudioResult(result);
                ToastWindow.Show("LIFTER PARAMETERS", StatusText.Text);
            }
            catch (Exception ex)
            {
                string message = FriendlyApiError(ex.Message);
                StatusText.Text = message;
                ToastWindow.Show("LIFTER PARAMETERS", message, true);
            }
            finally
            {
                _lifterStudioRunning = false;
            }
        }

        /// <summary>Toolbar 3/4 - library: the premium catalog window.</summary>
        private void PwToolbarLibrary()
        {
            if (!_isSignedIn)
            {
                ToastWindow.Show("LICENCE REQUIRED", "Activate your licence first.", true);
                PwToolbarLicense();
                return;
            }
            if (_libraryWindow == null) _libraryWindow = new LibraryWindow(this);
            _libraryWindow.Refresh();
            _libraryWindow.Show();
            _libraryWindow.Activate();
        }

        /// <summary>Toolbar 4/4 - checker: compact result panel.</summary>
        private void PwToolbarChecker()
        {
            if (!_isSignedIn)
            {
                ToastWindow.Show("LICENCE REQUIRED", "Activate your licence first.", true);
                PwToolbarLicense();
                return;
            }
            CheckerWindow.ShowFor(this);
        }

        private static void ApplyPreparedPackage(CatalogItem item, PreparedPackage package)
        {
            item.CatPartPath = package.CatPartPath;
            item.CheckScriptDirectory = Path.GetDirectoryName(package.ScriptPath);
            item.CheckScriptFile = Path.GetFileName(package.ScriptPath);
            item.CheckFunction = package.CheckFunction;
            item.PowerCopyName = package.PowerCopyName;
            if (!string.IsNullOrWhiteSpace(package.Workflow)) item.Workflow = package.Workflow;
        }

        // ================================================================
        // Lifter Studio integration (C# port of the Lifter catvba macro)
        // ================================================================

        private string BuildTemplateMetaText()
        {
            if (SelectedItem == null) return string.Empty;
            return "ID: " + (string.IsNullOrWhiteSpace(SelectedItem.Id) ? "—" : SelectedItem.Id.Trim())
                   + "  •  PowerCopy: " + (string.IsNullOrWhiteSpace(SelectedItem.PowerCopyName) ? "—" : SelectedItem.PowerCopyName)
                   + "  •  Workflow: " + (string.IsNullOrWhiteSpace(SelectedItem.Workflow) ? "none" : SelectedItem.Workflow);
        }

        private void UpdateLifterIndicator()
        {
            LifterWorkflowBadge.Visibility = IsLifterTemplate(SelectedItem)
                ? Visibility.Visible
                : Visibility.Collapsed;
            TemplateMetaText.Text = BuildTemplateMetaText();
        }

        // ------------------------------------------------------------------
        // Manual lifter marking: the "STROKE setup" button flags the selected
        // card as a lifter template on this PC (persisted under
        // %LOCALAPPDATA%\Estichara\MoldAutomationCatalog\lifter-templates.json).
        // This makes the workflow independent from the server catalog column,
        // the package manifest and catalog.json Id matching: the user marks a
        // card once, every later session runs the full lifter workflow.
        // ------------------------------------------------------------------

        private readonly HashSet<string> _lifterOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string LifterOverridesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Estichara", "MoldAutomationCatalog", "lifter-templates.json");
        }

        private void LoadLifterOverrides()
        {
            try
            {
                if (!File.Exists(LifterOverridesPath())) return;
                var serializer = new JavaScriptSerializer();
                List<string> ids = serializer.Deserialize<List<string>>(File.ReadAllText(LifterOverridesPath()));
                if (ids == null) return;
                foreach (string id in ids)
                {
                    if (!string.IsNullOrWhiteSpace(id)) _lifterOverrides.Add(id.Trim());
                }
            }
            catch
            {
                // A corrupted override file must never block the application.
            }
        }

        private void MarkSelectedAsLifter()
        {
            if (SelectedItem != null) SelectedItem.Workflow = "lifter";

            string id = (SelectedItem?.Id ?? string.Empty).Trim();
            if (id.Length == 0 || _lifterOverrides.Contains(id))
            {
                UpdateLifterIndicator();
                return;
            }

            _lifterOverrides.Add(id);
            try
            {
                string path = LifterOverridesPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(_lifterOverrides.ToList()));
            }
            catch
            {
                // Persistence is a convenience only - the in-memory flag still
                // applies for this session.
            }
            UpdateLifterIndicator();
        }

        /// <summary>Runs the licensed Lifter Studio macro INSIDE CATIA.
        /// The script is the VBScript edition of the original CATVBA macro:
        /// it measures STROKE_Distance on first run, links every PowerCopy
        /// instance to that single value, (re)creates the Draft formulas,
        /// opens the Lifter Studio dashboard (parameters + Boolean Remove)
        /// and finally hands over to the PowerCopy instantiation.
        ///
        /// It is executed on a dedicated STA thread with its own CATIA
        /// connection: the macro keeps CATIA busy for as long as the
        /// dashboard is open, and this keeps the WPF window responsive.
        /// Returns POWERCOPY:&lt;linked&gt;:&lt;drafts&gt; / CLOSED:&lt;linked&gt;:&lt;drafts&gt; /
        /// CANCELLED / ERROR:&lt;reason&gt;.</summary>
        private Task<string> RunLifterStudioScriptAsync()
        {
            string scriptPath = LifterStudioScript.Write();
            return RunCatiaScriptAsync(scriptPath, "CATMain", true);
        }

        /// <summary>Executes a CATScript inside CATIA on a dedicated STA thread,
        /// so a macro that keeps CATIA busy (dashboards, measurements) never
        /// freezes the PW toolbar and its panels.</summary>
        private Task<string> RunCatiaScriptAsync(string scriptPath, string function, bool deleteAfterwards)
        {
            string directory = Path.GetDirectoryName(scriptPath);
            string fileName = Path.GetFileName(scriptPath);

            var completion = new TaskCompletionSource<string>();
            var worker = new Thread(delegate ()
            {
                try
                {
                    // Own connection = own apartment: the long running macro
                    // never blocks the dashboard's own COM proxy.
                    var catia = (INFITF.Application)Marshal.GetActiveObject("CATIA.Application");
                    object rawResult = catia.SystemService.ExecuteScript(
                        directory,
                        INFITF.CatScriptLibraryType.catScriptLibraryTypeDirectory,
                        fileName,
                        function,
                        new object[0]);
                    completion.SetResult(rawResult == null ? string.Empty : rawResult.ToString().Trim());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    // The macro never stays on the customer's disk in clear text.
                    if (deleteAfterwards)
                    {
                        try { if (File.Exists(scriptPath)) File.Delete(scriptPath); }
                        catch { }
                    }
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
            return completion.Task;
        }

        /// <summary>Turns the macro's return code into dashboard feedback.</summary>
        private void ApplyLifterStudioResult(string result)
        {
            result = (result ?? string.Empty).Trim();

            if (result.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(result.Substring(6).Trim());

            if (result.Length == 0)
                throw new InvalidOperationException(
                    "The Lifter Studio script returned no result. Check that CATIA allows macros to run.");

            if (string.Equals(result, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                OverallStatusText.Text = "NOT RUN";
                StatusText.Text = "Lifter setup was cancelled in CATIA - nothing was changed on the CATPart.";
                return;
            }

            string[] parts = result.Split(':');
            string linked = parts.Length > 1 ? parts[1] : "0";
            string drafts = parts.Length > 2 ? parts[2] : "0";

            if (result.StartsWith("POWERCOPY:", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = string.Format(
                    "Lifter Studio finished - STROKE linked ({0}), Draft formulas ensured ({1}). "
                    + "Complete CATIA's PowerCopy instantiation, then choose Run check.",
                    linked, drafts);
            }
            else
            {
                StatusText.Text = string.Format(
                    "Lifter Studio closed - STROKE linked ({0}), Draft formulas ensured ({1}). "
                    + "Choose Run check to validate the part.",
                    linked, drafts);
            }
        }

        /// <summary>A template flagged with Workflow = "lifter" (server catalog,
        /// package manifest or local catalog.json) runs the integrated Lifter
        /// Studio instead of the plain instantiate + check flow.</summary>
        private static bool IsLifterTemplate(CatalogItem item)
        {
            return item != null &&
                   string.Equals((item.Workflow ?? string.Empty).Trim(), "lifter",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Opens (or re-activates) the Lifter Studio window bound to the
        /// current destination CATPart. Modeless and not top-most so the user can
        /// keep working in CATIA next to it.</summary>
        private void ShowLifterStudio()
        {
            try
            {
                CatalogItem item = _lifterSessionItem ?? SelectedItem;
                if (item == null) return;

                if (_lifterStudio != null && _lifterStudio.IsLoaded)
                {
                    if (ReferenceEquals(_lifterStudio.Item, item) &&
                        ReferenceEquals(_lifterStudio.Document, _destination))
                    {
                        _lifterStudio.Activate();
                        return;
                    }
                    _lifterStudio.Close();
                    _lifterStudio = null;
                }

                _lifterStudio = new LifterStudioWindow(this, _catia, _destination, item, LaunchLifterInstantiateAsync)
                {
                    Owner = this
                };
                _lifterStudio.Closed += delegate { _lifterStudio = null; };
                _lifterStudio.Show();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Lifter Studio could not be opened: " + ex.Message;
            }
        }

        /// <summary>Used by Lifter Studio's "New instance" button. Re-acquires a
        /// lease and re-downloads the encrypted server package when the previous
        /// session wiped it, then relaunches CATIA's native Instantiate From
        /// Selection with the PowerCopy reference selected.</summary>
        private async Task<bool> LaunchLifterInstantiateAsync(CatalogItem item)
        {
            bool reopenedHere = false;
            try
            {
                if (item == null) return false;
                if (_catia == null) { ConnectToCatia(); if (_catia == null) return false; }

                // The lifter package never stays on disk between sessions: make sure
                // the secure package is available again (lease + fresh download).
                if (string.IsNullOrWhiteSpace(item.CatPartPath) || !File.Exists(item.CatPartPath))
                {
                    if (string.IsNullOrWhiteSpace(_activeLeaseToken))
                    {
                        LeaseResponse lease = await _api.AcquireLeaseAsync(
                            item.Id, _deviceId, Environment.MachineName);
                        _activeLeaseToken = lease.lease_token;
                        _heartbeatTimer.Start();
                    }

                    StatusText.Text = "Re-downloading the encrypted template package from the server…";
                    var progress = new Progress<PackageDownloadProgress>(value =>
                    {
                        PackageStatusText.Text = value.TotalBytes.HasValue && value.TotalBytes.Value > 0
                            ? string.Format("Downloading {0}%…", value.Percent)
                            : "Downloading…";
                    });
                    PreparedPackage package = await _packageManager.PrepareAsync(
                        item, _api, _activeLeaseToken, progress);
                    ApplyPreparedPackage(item, package);
                    _lifterSessionItem = item;
                }

                // Tolerate a source document the user closed directly inside CATIA.
                if (_source != null)
                {
                    try { _source.Activate(); }
                    catch { _source = null; }
                }
                if (_source == null)
                {
                    _source = (MECMOD.PartDocument)_catia.Documents.Open(item.CatPartPath);
                    reopenedHere = true;
                }
                _source.Activate();

                INFITF.AnyObject reference = FindAndSelectReference(_source, item.PowerCopyName);
                if (reference == null) return false;

                _destination.Activate();
                _catia.StartCommand("Instantiate From Selection");
                _catia.RefreshDisplay = true;

                CloseSourceButton.IsEnabled = true;
                OverallStatusText.Text = "AWAITING CATIA";
                return true;
            }
            catch (Exception)
            {
                if (reopenedHere) CloseSourceDocument();
                return false;
            }
        }

        /// <summary>Deletes the encrypted lifter package from this PC when the
        /// template session ends (lease released), so the paid PowerCopy is never
        /// stored locally. The next "Use in CATIA" downloads it again from the
        /// server after a fresh entitlement check.</summary>
        private void WipeLifterSessionPackage()
        {
            if (!WipeLifterPackageOnSessionEnd) return;
            CatalogItem item = _lifterSessionItem;
            if (item == null) return;
            _lifterSessionItem = null;
            try
            {
                _packageManager.Clear(item);
                if (SelectedItem != null &&
                    string.Equals(SelectedItem.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                {
                    PackageStatusText.Text = "Session ended. The encrypted package was removed from this PC.";
                    ClearPackageCacheButton.IsEnabled = false;
                }
            }
            catch { }
        }

        private static bool HasLocalRuntimeFiles(CatalogItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.CatPartPath) ||
                string.IsNullOrWhiteSpace(item.CheckScriptDirectory) ||
                string.IsNullOrWhiteSpace(item.CheckScriptFile))
                return false;

            return File.Exists(item.CatPartPath) &&
                   File.Exists(Path.Combine(item.CheckScriptDirectory, item.CheckScriptFile));
        }

        private static bool IsPackageUnavailableError(string value)
        {
            return value == "package_not_authorized_or_not_available" ||
                   value == "package_file_missing";
        }

        private void ClearPackageCacheButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null) return;
            if (_source != null)
            {
                MessageBox.Show(
                    "Close the active CATIA source before clearing its package cache.",
                    "Clear package cache",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                _packageManager.Clear(SelectedItem);
                if (IsLifterTemplate(SelectedItem)) _lifterSessionItem = null;
                CatalogItem local = FindLocalConfig(SelectedItem);
                if (local != null)
                {
                    SelectedItem.CatPartPath = local.CatPartPath;
                    SelectedItem.CheckScriptDirectory = local.CheckScriptDirectory;
                    SelectedItem.CheckScriptFile = local.CheckScriptFile;
                    SelectedItem.CheckFunction = local.CheckFunction;
                    if (!string.IsNullOrWhiteSpace(local.PowerCopyName))
                        SelectedItem.PowerCopyName = local.PowerCopyName;
                }
                PackageStatusText.Text = "Cache cleared. The next Use in CATIA will measure a fresh Hostinger download.";
                ClearPackageCacheButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Clear package cache", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPackageSpeedLogButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                PackagePerformanceLog.EnsureCreated();
                Process.Start(PackagePerformanceLog.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Package speed log", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void RunCheckButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedItem == null) return;
                if (_destination == null) throw new InvalidOperationException("Destination CATPart is unavailable.");
                if (!Directory.Exists(SelectedItem.CheckScriptDirectory))
                    throw new DirectoryNotFoundException("Check script folder not found: " + SelectedItem.CheckScriptDirectory);

                string scriptPath = Path.Combine(SelectedItem.CheckScriptDirectory, SelectedItem.CheckScriptFile);
                if (!File.Exists(scriptPath))
                {
                    // Lifter packages are wiped when their session ends, so a later
                    // check needs a fresh "Use in CATIA" (new lease + download).
                    if (IsLifterTemplate(SelectedItem))
                        throw new FileNotFoundException(
                            "The lifter session ended and its secure package was removed from this PC. " +
                            "Use in CATIA again to start a new session and run the check.");
                    throw new FileNotFoundException("Check script not found.", scriptPath);
                }

                _destination.Activate();
                StatusText.Text = "Running CATIA interference check...";
                OverallStatusText.Text = "RUNNING";

                object[] parameters = new object[0];
                object rawResult = _catia.SystemService.ExecuteScript(
                    SelectedItem.CheckScriptDirectory,
                    INFITF.CatScriptLibraryType.catScriptLibraryTypeDirectory,
                    SelectedItem.CheckScriptFile,
                    SelectedItem.CheckFunction,
                    parameters);

                string resultText = rawResult == null ? string.Empty : rawResult.ToString();
                PopulateResults(ParseResults(resultText));
                StatusText.Text = "Check complete. CATIA geometry and this report are synchronized.";
                CloseSourceDocument();
                await ReleaseLeaseSilentlyAsync();
            }
            catch (Exception ex)
            {
                OverallStatusText.Text = "ERROR";
                StatusText.Text = "The integrated check could not be completed.";
                MessageBox.Show(ex.Message, "Run check", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CloseSourceButton_OnClick(object sender, RoutedEventArgs e)
        {
            CloseSourceDocument();
            await ReleaseLeaseSilentlyAsync();
            StatusText.Text = "Source template closed and license seat released.";
        }

        private void PopulateResults(List<CheckRow> rows)
        {
            Results.Clear();
            bool hasFail = false;
            bool hasError = false;

            foreach (CheckRow row in rows)
            {
                ApplyStatusTheme(row);
                Results.Add(row);
                if (row.Status == "FAIL") hasFail = true;
                if (row.Status == "ERROR") hasError = true;
            }

            OverallStatusText.Text = hasError ? "ERROR" : hasFail ? "ISSUES FOUND" : "ALL CLEAR";
            OverallStatusText.Foreground = hasError
                ? BrushFrom("#C2410C")
                : hasFail ? BrushFrom("#B91C1C") : BrushFrom("#166534");
        }

        private static List<CheckRow> ParseResults(string text)
        {
            var rows = new List<CheckRow>();
            string[] lines = text.Replace("\r", string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] values = line.Split('|');
                rows.Add(new CheckRow
                {
                    Position = values.Length > 0 ? values[0] : "—",
                    Status = values.Length > 1 ? values[1] : "ERROR",
                    Distance = values.Length > 2 && values[2].Length > 0 ? values[2] + " mm" : "N/A",
                    NearestBody = values.Length > 3 ? values[3] : string.Empty
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new CheckRow
                {
                    Position = "—",
                    Status = "ERROR",
                    Distance = "N/A",
                    NearestBody = "No result returned by CATIA"
                });
            }

            return rows;
        }

        private static void ApplyStatusTheme(CheckRow row)
        {
            switch (row.Status)
            {
                case "CLEAR":
                    row.StatusGlyph = "✓";
                    row.StatusBackground = BrushFrom("#DCFCE7");
                    row.StatusForeground = BrushFrom("#166534");
                    break;
                case "FAIL":
                    row.StatusGlyph = "!";
                    row.StatusBackground = BrushFrom("#FEE2E2");
                    row.StatusForeground = BrushFrom("#991B1B");
                    break;
                default:
                    row.StatusGlyph = "×";
                    row.StatusBackground = BrushFrom("#FFEDD5");
                    row.StatusForeground = BrushFrom("#9A3412");
                    break;
            }
        }

        private static INFITF.AnyObject FindAndSelectReference(MECMOD.PartDocument source, string name)
        {
            INFITF.AnyObject result = null;
            try { result = (INFITF.AnyObject)source.Part.FindObjectByName(name); } catch { }

            INFITF.Selection selection = source.Selection;
            selection.Clear();

            if (result != null)
            {
                selection.Add(result);
                return result;
            }

            selection.Search("Name=" + name + ",all");
            if (selection.Count2 == 0) return null;

            result = (INFITF.AnyObject)selection.Item2(1).Value;
            selection.Clear();
            selection.Add(result);
            return result;
        }

        private async Task RenewLeaseSilentlyAsync()
        {
            if (string.IsNullOrWhiteSpace(_activeLeaseToken)) return;
            try
            {
                await _api.HeartbeatAsync(_activeLeaseToken);
            }
            catch
            {
                _heartbeatTimer.Stop();
                StatusText.Text = "License heartbeat failed. Finish or close the active template session.";
            }
        }

        private async Task ReleaseLeaseSilentlyAsync()
        {
            if (string.IsNullOrWhiteSpace(_activeLeaseToken)) return;
            string token = _activeLeaseToken;
            _activeLeaseToken = null;
            _heartbeatTimer.Stop();
            try { await _api.ReleaseAsync(token); } catch { }
            WipeLifterSessionPackage();   // paid PowerCopy never stays on disk
        }

        private void CloseSourceDocument()
        {
            if (_source == null) return;

            try
            {
                _destination?.Activate();
                bool alerts = _catia.DisplayFileAlerts;
                _catia.DisplayFileAlerts = false;
                _source.Close();
                _catia.DisplayFileAlerts = alerts;
            }
            finally
            {
                _source = null;
                CloseSourceButton.IsEnabled = false;
            }
        }

        private void EmailSupportButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternal("mailto:support@yourcompany.com");
        }

        private void WebsiteButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternal("https://www.yourcompany.com");
        }

        private static void OpenExternal(string target)
        {
            try
            {
                Process.Start(target);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open link", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string GetDocumentName(INFITF.Document document)
        {
            try { return document.get_Name(); }
            catch { return "active CATPart"; }
        }

        private static SolidColorBrush BrushFrom(string value)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
