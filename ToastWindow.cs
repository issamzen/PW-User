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

        private ToastWindow(string title, string message, bool error)
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

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(error
                    ? Color.FromRgb(0xFE, 0xCA, 0xCA)
                    : Color.FromRgb(0x16, 0xA7, 0xB4)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });
            stack.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            shell.Child = stack;
            Content = shell;

            MouseLeftButtonDown += delegate { Close(); };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(error ? 8 : 5) };
            _timer.Tick += delegate
            {
                _timer.Stop();
                try { Close(); } catch { }
            };
            _timer.Start();
        }

        public static void Show(string title, string message, bool error = false)
        {
            try
            {
                var toast = new ToastWindow(title, message, error);
                toast.Show();
                toast.Left = SystemParameters.WorkArea.Right - toast.ActualWidth - 84;
                toast.Top = SystemParameters.WorkArea.Bottom - toast.ActualHeight - 40;
            }
            catch
            {
                // A notification must never break a command.
            }
        }
    }
}
