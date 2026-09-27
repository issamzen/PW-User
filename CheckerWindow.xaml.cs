using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>Compact result panel for the feasibility / clash check.</summary>
    public partial class CheckerWindow : Window
    {
        private static CheckerWindow _instance;
        private readonly MainWindow _controller;

        public CheckerWindow(MainWindow controller)
        {
            InitializeComponent();
            _controller = controller;
            ResultList.ItemsSource = _controller.Results;
            _controller.StatusChanged += OnStatus;
            Loaded += delegate { Refresh(); };
        }

        /// <summary>Single shared instance, opened from the toolbar or the library.</summary>
        /// <summary>The shared instance, or null when it was never opened.</summary>
        public static CheckerWindow Current { get { return _instance; } }

        public static CheckerWindow ShowFor(MainWindow controller)
        {
            if (_instance == null)
            {
                _instance = new CheckerWindow(controller);
                _instance.Closed += delegate { _instance = null; };
            }
            _instance.Refresh();
            _instance.Show();
            _instance.Activate();
            return _instance;
        }

        public void Refresh()
        {
            CatalogItem item = _controller.SelectedItem;
            TemplateText.Text = item == null ? "No template selected" : item.Name;
            DocumentText.Text = _controller.DestinationName;
            SetVerdict(_controller.OverallStatus);
        }

        private void SetVerdict(string verdict)
        {
            VerdictText.Text = string.IsNullOrWhiteSpace(verdict) ? "NOT RUN" : verdict.ToUpperInvariant();
            Color background = Color.FromRgb(0xE2, 0xE8, 0xF0);
            Color foreground = Color.FromRgb(0x33, 0x41, 0x55);

            string value = VerdictText.Text;
            if (value.Contains("PASS") || value.Contains("OK"))
            {
                background = Color.FromRgb(0xDC, 0xFC, 0xE7);
                foreground = Color.FromRgb(0x16, 0x65, 0x34);
            }
            else if (value.Contains("ERROR") || value.Contains("FAIL") || value.Contains("CLASH"))
            {
                background = Color.FromRgb(0xFE, 0xE2, 0xE2);
                foreground = Color.FromRgb(0x99, 0x1B, 0x1B);
            }
            else if (value.Contains("RUNNING") || value.Contains("AWAITING"))
            {
                background = Color.FromRgb(0xFE, 0xF3, 0xC7);
                foreground = Color.FromRgb(0x92, 0x40, 0x0E);
            }

            VerdictBadge.Background = new SolidColorBrush(background);
            VerdictText.Foreground = new SolidColorBrush(foreground);
        }

        private void OnStatus(string text)
        {
            Dispatcher.Invoke(delegate
            {
                StatusText.Text = text;
                SetVerdict(_controller.OverallStatus);
                DocumentText.Text = _controller.DestinationName;
            });
        }

        private void RunButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_controller.SelectedItem == null)
            {
                StatusText.Text = "Select a template in the library first.";
                return;
            }
            StatusText.Text = "Running the CATIA check…";
            _controller.RunCheck();
        }

        private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void Close_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Hide();
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
