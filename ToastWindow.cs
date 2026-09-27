using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>
    /// Small self-closing notification used by the commands that must not open
    /// a window (the Stroke tool "just runs"). Appears near the PW toolbar and
    /// disappears after a few seconds.
    /// </summary>
    internal sealed class ToastWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private TextBlock _titleBlock;
        private TextBlock _messageBlock;
        private Border _shell;
        private ToastWindow(string title, string message, bool error, bool sticky = false)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;

            var shell = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                MaxWidth = 340,
                Background = new SolidColorBrush(error
                    ? Color.FromRgb(0x7F, 0x1D, 0x1D)
                    : Color.FromRgb(0x0F, 0x22, 0x2E)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Color = Colors.Black
                }
            };

            _shell = shell;
            var stack = new StackPanel();
            _titleBlock = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(error
                    ? Color.FromRgb(0xFE, 0xCA, 0xCA)
                    : Color.FromRgb(0x16, 0xA7, 0xB4)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            stack.Children.Add(_titleBlock);
            _messageBlock = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(_messageBlock);
            shell.Child = stack;
            Content = shell;

            MouseLeftButtonDown += delegate { Close(); };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(error ? 8 : 5) };
            _timer.Tick += delegate
            {
                _timer.Stop();
                try { Close(); } catch { }
            };
            if (!sticky) _timer.Start();
        }

        /// <summary>A toast that stays on screen until Complete() is called -
        /// used by the commands that take a while inside CATIA.</summary>
        public static ToastWindow ShowProgress(string title, string message)
        {
            try
            {
                var toast = new ToastWindow(title, message, false, true);
                toast.Show();
                toast.Place();
                return toast;
            }
            catch { return null; }
        }

        /// <summary>Turns a progress toast into its final message, then fades out.</summary>
        public void Complete(string title, string message, bool error = false)
        {
            try
            {
                _titleBlock.Text = title;
                _titleBlock.Foreground = new SolidColorBrush(error
                    ? Color.FromRgb(0xFE, 0xCA, 0xCA)
                    : Color.FromRgb(0x16, 0xA7, 0xB4));
                _messageBlock.Text = message;
                _shell.Background = new SolidColorBrush(error
                    ? Color.FromRgb(0x7F, 0x1D, 0x1D)
                    : Color.FromRgb(0x0F, 0x22, 0x2E));
                Place();
                _timer.Interval = TimeSpan.FromSeconds(error ? 8 : 5);
                _timer.Start();
            }
            catch { }
        }

        private void Place()
        {
            try
            {
                UpdateLayout();
                Left = SystemParameters.WorkArea.Right - ActualWidth - 84;
                Top = SystemParameters.WorkArea.Bottom - ActualHeight - 40;
            }
            catch { }
        }

        public static void Show(string title, string message, bool error = false)
        {
            try
            {
                var toast = new ToastWindow(title, message, error);
                toast.Show();
                toast.Place();
            }
            catch
            {
                // A notification must never break a command.
            }
        }
    }
}
