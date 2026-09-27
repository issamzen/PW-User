using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>
    /// The premium PowerCopy library: the only "big" window of the product.
    /// It is a view on the hidden controller (catalog, licence, CATIA state);
    /// all the work is still done by MainWindow.
    /// </summary>
    public partial class LibraryWindow : Window
    {
        private readonly MainWindow _controller;
        private string _category = "All";
        private bool _building;

        public LibraryWindow(MainWindow controller)
        {
            InitializeComponent();
            _controller = controller;

            CatalogList.ItemsSource = _controller.CatalogItems;
            var view = CollectionViewSource.GetDefaultView(CatalogList.ItemsSource);
            view.Filter = FilterItem;

            _controller.StatusChanged += OnControllerStatus;
            _controller.CatalogChanged += OnCatalogChanged;

            Loaded += delegate { Refresh(); };
        }

        // ------------------------------------------------------------ view
        public void Refresh()
        {
            BuildCategories();
            CollectionViewSource.GetDefaultView(CatalogList.ItemsSource).Refresh();
            UpdateCounts();

            bool catia = _controller.CatiaConnected;
            CatiaDot.Fill = new SolidColorBrush(catia
                ? Color.FromRgb(0x22, 0xC5, 0x5E)
                : Color.FromRgb(0xDC, 0x26, 0x26));
            CatiaText.Text = catia ? "CATIA connected" : "CATIA offline";
            LicenseText.Text = _controller.LicenseSummary;
            RailSubText.Text = _controller.IsSignedIn ? "Licensed templates" : "Not activated";
        }

        private void UpdateCounts()
        {
            int total = _controller.CatalogItems.Count;
            int shown = CollectionViewSource.GetDefaultView(CatalogList.ItemsSource).Cast<object>().Count();
            CountText.Text = total == 0
                ? "No template available on this account."
                : shown + " of " + total + " template(s)";
        }

        private void BuildCategories()
        {
            _building = true;
            CategoryPanel.Children.Clear();

            var categories = new List<string> { "All", "Favorites" };
            foreach (CatalogItem item in _controller.CatalogItems)
            {
                string category = (item.Category ?? string.Empty).Trim();
                if (category.Length > 0 && !categories.Contains(category)) categories.Add(category);
            }

            foreach (string category in categories)
            {
                var button = new Button
                {
                    Content = category,
                    Style = (Style)FindResource("ChipButton"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = category == _category ? "Selected" : null
                };
                string captured = category;
                button.Click += delegate
                {
                    _category = captured;
                    Refresh();
                };
                CategoryPanel.Children.Add(button);
            }
            _building = false;
        }

        private bool FilterItem(object value)
        {
            var item = value as CatalogItem;
            if (item == null) return false;

            if (_category == "Favorites" && !item.IsFavorite) return false;
            if (_category != "All" && _category != "Favorites" &&
                !string.Equals(item.Category, _category, StringComparison.OrdinalIgnoreCase)) return false;

            string query = (SearchBox.Text ?? string.Empty).Trim();
            if (query.Length == 0) return true;

            return Has(item.Name, query) || Has(item.Id, query) ||
                   Has(item.Category, query) || Has(item.Description, query);
        }

        private static bool Has(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // --------------------------------------------------------- events
        private void OnControllerStatus(string text)
        {
            Dispatcher.Invoke(delegate { StatusText.Text = text; });
        }

        private void OnCatalogChanged()
        {
            Dispatcher.Invoke(delegate { Refresh(); });
        }

        private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_building) return;
            CollectionViewSource.GetDefaultView(CatalogList.ItemsSource).Refresh();
            UpdateCounts();
        }

        private void CatalogList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = CatalogList.SelectedItem as CatalogItem;
            _controller.SelectItem(item);

            bool has = item != null;
            UseButton.IsEnabled = has;
            CheckButton.IsEnabled = has;

            if (!has)
            {
                DetailTitle.Text = "No template selected";
                DetailMeta.Text = "Pick a card on the left";
                DetailDescription.Text = "—";
                DetailInputs.Text = string.Empty;
                DetailOutput.Text = string.Empty;
                LifterBadge.Visibility = Visibility.Collapsed;
                PackageText.Text = string.Empty;
                return;
            }

            DetailTitle.Text = item.Name;
            DetailMeta.Text = item.Id + "  •  " + item.Category + "  •  v" + item.Version;
            DetailDescription.Text = string.IsNullOrWhiteSpace(item.Description) ? "—" : item.Description;
            DetailInputs.Text = string.IsNullOrWhiteSpace(item.Inputs) ? string.Empty : "Inputs: " + item.Inputs;
            DetailOutput.Text = string.IsNullOrWhiteSpace(item.Output) ? string.Empty : "Output: " + item.Output;
            LifterBadge.Visibility = _controller.IsLifter(item) ? Visibility.Visible : Visibility.Collapsed;
            PackageText.Text = _controller.DescribePackage(item);
        }

        private void UseButton_OnClick(object sender, RoutedEventArgs e)
        {
            var item = CatalogList.SelectedItem as CatalogItem;
            if (item == null) return;
            StatusText.Text = "Starting " + item.Name + "…";
            _controller.UseInCatia(item);
        }

        private void CheckButton_OnClick(object sender, RoutedEventArgs e)
        {
            var item = CatalogList.SelectedItem as CatalogItem;
            if (item == null) return;
            _controller.RunCheck();
            CheckerWindow.ShowFor(_controller);
        }

        private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Synchronizing the authorized catalog…";
            await _controller.RefreshCatalogAsync();
            Refresh();
        }

        private void Close_OnClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!ForceClose)
            {
                // The product lives in the toolbar: the panel only hides.
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        /// <summary>Set by the controller when the whole product is closing.</summary>
        public bool ForceClose { get; set; }
    }
}
