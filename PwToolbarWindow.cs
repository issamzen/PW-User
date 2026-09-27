using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>
    /// PW-User floating toolbar.
    ///
    /// A slim always-on-top window with the four product commands
    /// (Licence, Stroke, Library, Checker) that docks itself to the right
    /// edge of the CATIA window and follows it when CATIA is moved,
    /// resized, minimised or restored.
    ///
    /// Why not real CATIA toolbar icons? The CATIA V5 automation API cannot
    /// create commands or toolbars - the only supported way is a CAA V5
    /// (RADE) C++ add-in, and the CATSettings deployment route has to be
    /// prepared per CATIA release. This window needs ZERO setup on the
    /// customer's PC: it appears by itself, works on every V5 release, and
    /// cannot be blocked by a locked-down CATIA environment.
    /// </summary>
    internal sealed class PwToolbarWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private readonly DispatcherTimer _follow;
        private readonly Action _onLicense;
        private readonly Action _onStroke;
        private readonly Action _onLibrary;
        private readonly Action _onChecker;
        private readonly Action _onQuit;
        private bool _userMoved;

        public PwToolbarWindow(Action onLicense, Action onStroke, Action onLibrary, Action onChecker, Action onQuit)
        {
            _onLicense = onLicense;
            _onStroke = onStroke;
            _onLibrary = onLibrary;
            _onChecker = onChecker;
            _onQuit = onQuit;

            Title = "PW-User";
            Width = 58;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;

            Content = BuildBody();

            SourceInitialized += delegate
            {
                // A tool window that never steals the focus from CATIA.
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int style = GetWindowLong(handle, GWL_EXSTYLE);
                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            };

            _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _follow.Tick += delegate { FollowCatia(); };
            _follow.Start();
        }

        private UIElement BuildBody()
        {
            var shell = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x22, 0x2E)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x3A, 0x4A)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 8, 6, 8)
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // drag handle + brand
            var grip = new TextBlock
            {
                Text = "PW",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA7, 0xB4)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.SizeAll,
                ToolTip = "Drag to move the PW-User toolbar"
            };
            grip.MouseLeftButtonDown += delegate
            {
                try { _userMoved = true; DragMove(); } catch { }
            };
            stack.Children.Add(grip);

            stack.Children.Add(MakeButton("\uE8D7", "Licence check - verify the seat and unlock the libraries",
                new SolidColorBrush(Color.FromRgb(0x0F, 0x88, 0x91)), _onLicense));
            stack.Children.Add(MakeButton("\uE9D9", "Stroke tool - measure and create STROKE_Distance on the active CATPart",
                new SolidColorBrush(Color.FromRgb(0x16, 0xA7, 0xB4)), _onStroke));
            stack.Children.Add(MakeButton("\uE8F1", "PowerCopy library - browse and use the licensed templates",
                new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), _onLibrary));
            stack.Children.Add(MakeButton("\uE9D5", "Checker - feasibility and clash check against the main CATPart",
                new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), _onChecker));

            var quit = new TextBlock
            {
                Text = "\uE7E8",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x96, 0xA5)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Quit PW-User (the tools stay open until you click here)"
            };
            quit.MouseLeftButtonDown += delegate
            {
                MessageBoxResult answer = MessageBox.Show(
                    "Close the PW-User tools?\n\nThe toolbar and all its panels will be closed.",
                    "PW-User",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes && _onQuit != null) _onQuit();
            };
            stack.Children.Add(quit);

            shell.Child = stack;
            return shell;
        }

        private UIElement MakeButton(string glyph, string tip, Brush accent, Action action)
        {
            var button = new Button
            {
                Width = 42,
                Height = 42,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = tip,
                Cursor = Cursors.Hand,
                Focusable = false,
                Content = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 17,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            // Flat, CATIA-like square button.
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, accent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;

            button.Click += delegate
            {
                try { if (action != null) action(); }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "PW-User", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            return button;
        }

        /// <summary>Docks the toolbar to the right edge of the CATIA window.</summary>
        private void FollowCatia()
        {
            try
            {
                IntPtr catia = FindCatiaWindow();

                if (catia == IntPtr.Zero)
                {
                    // CATIA is not started yet: the bar stays usable (Licence and
                    // Library work without CATIA) and parks on the right of the
                    // desktop work area.
                    if (!IsVisible) Show();
                    if (_userMoved) return;
                    Left = SystemParameters.WorkArea.Right - Width - 14;
                    Top = SystemParameters.WorkArea.Top + 120;
                    return;
                }

                if (!IsVisible) Show();

                if (IsIconic(catia) || !IsWindowVisible(catia))
                {
                    // CATIA minimised: the bar stays on screen (the user asked
                    // for tools that are always reachable) and parks on the
                    // right of the desktop.
                    if (_userMoved) return;
                    Left = SystemParameters.WorkArea.Right - Width - 14;
                    Top = SystemParameters.WorkArea.Top + 120;
                    return;
                }

                if (_userMoved) return;   // the user placed it by hand: leave it there

                RECT rect;
                if (!GetWindowRect(catia, out rect)) return;

                double scale = 1.0;
                var source = System.Windows.PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                    scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0) scale = 1.0;

                double right = rect.Right / scale;
                double top = rect.Top / scale;
                double height = (rect.Bottom - rect.Top) / scale;

                Left = right - Width - 14;
                Top = top + Math.Max(90, (height - ActualHeight) / 3);
            }
            catch
            {
                // Never let the follow timer break the application.
            }
        }

        private static IntPtr FindCatiaWindow()
        {
            try
            {
                // CNEXT.exe is the CATIA V5 interactive process.
                Process[] processes = Process.GetProcessesByName("CNEXT");
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _follow.Stop(); } catch { }
            base.OnClosed(e);
        }
    }
}
