using System;
using System.Windows;
using System.Windows.Input;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>
    /// Small licence panel opened from the PW toolbar. It is the only place
    /// where the customer types anything: e-mail + licence key, one button.
    /// Everything else (catalog, leases, packages) is driven by the hidden
    /// controller window.
    /// </summary>
    public partial class LicenseWindow : Window
    {
        private readonly MainWindow _controller;

        public LicenseWindow(MainWindow controller)
        {
            InitializeComponent();
            _controller = controller;
            Loaded += delegate { Refresh(); };
        }

        /// <summary>Shows either the sign-in form or the "licence active" card.</summary>
        public void Refresh()
        {
            bool signedIn = _controller.IsSignedIn;
            FormPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
            ActivePanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            HeaderSubText.Text = signedIn ? "Workstation activated" : "Activate this workstation";

            if (signedIn)
            {
                ActiveUserText.Text = _controller.AccountName;
                ActiveExpiryText.Text = _controller.LicenseSummary;
                ActiveTemplatesText.Text = _controller.CatalogItems.Count + " authorized template(s)";
                StatusText.Text = "The library, stroke and checker tools are unlocked.";
            }
            else
            {
                EmailBox.Text = _controller.RememberedEmail ?? string.Empty;
                StatusText.Text = "Enter your licence account.";
            }
        }

        private async void ActivateButton_OnClick(object sender, RoutedEventArgs e)
        {
            ActivateButton.IsEnabled = false;
            StatusText.Text = "Checking the licence…";
            try
            {
                string error = await _controller.SignInAsync(
                    EmailBox.Text.Trim(),
                    KeyBox.Password,
                    RememberCheck.IsChecked == true);

                if (error != null)
                {
                    StatusText.Text = error;
                    return;
                }

                KeyBox.Clear();
                Refresh();
            }
            finally
            {
                ActivateButton.IsEnabled = true;
            }
        }

        private void KeyBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ActivateButton_OnClick(sender, null);
        }

        private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Refreshing the licence…";
            await _controller.RefreshLicenseAsync();
            Refresh();
        }

        private void SignOutButton_OnClick(object sender, RoutedEventArgs e)
        {
            _controller.SignOut();
            Refresh();
        }

        private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void Close_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Hide();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
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
