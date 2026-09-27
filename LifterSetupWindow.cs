using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>
    /// WPF replacement of the catvba "FirstRunSetup" HTA panel, built entirely in
    /// code (no XAML Page item needed). The main body is auto-detected
    /// (part.MainBody, fallback: first body), the bounding box width is measured
    /// while this branded panel shows progress, and the user can Retry, pick
    /// another body with the native CATIA prompt, Continue or Cancel. Runs only
    /// when STROKE_Distance does not exist yet.
    /// </summary>
    public class LifterSetupWindow : Window
    {
        private readonly INFITF.Application _catia;
        private readonly MECMOD.PartDocument _document;
        private readonly string _documentName;
        private dynamic _part;
        private dynamic _detectedBody;

        private Ellipse _topDot;
        private Ellipse _detectDot;
        private TextBlock _topText;
        private TextBlock _detectText;
        private TextBlock _progressText;
        private TextBlock _valueText;
        private TextBlock _failText;
        private StackPanel _progressWrap;
        private Border _resultOk;
        private Border _resultFail;
        private Button _retryButton;
        private Button _continueButton;

        public LifterSetupWindow(INFITF.Application catia, MECMOD.PartDocument document)
        {
            _catia = catia;
            _document = document;
            try { _part = document.Part; } catch { _part = null; }
            try { _documentName = document.get_Name(); } catch { _documentName = null; }

            Title = "Lifter Studio — Setup";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = LifterUi.CanvasBrush;

            BuildUi();
            Loaded += OnLoadedOnce;
        }

        private async void OnLoadedOnce(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoadedOnce;
            // Let the panel paint before the blocking CATIA measurement.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            DetectAndMeasure();
        }

        // ================================================================
        // UI construction (pure code - no XAML)
        // ================================================================

        private void BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());

            Border header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            Border card = BuildCard();
            Grid.SetRow(card, 1);
            Grid.SetRowSpan(card, 1);
            root.Children.Add(card);

            Content = root;
        }

        private Border BuildHeader()
        {
            var logo = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = LifterUi.Accent,
                Child = new TextBlock
                {
                    Text = "L", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };

            var brand = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(new TextBlock { Text = "Lifter Studio", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
            brand.Children.Add(new TextBlock { Text = "One-time setup", Foreground = LifterUi.BrushFrom("#94A3B8"), FontSize = 10 });

            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(logo);
            left.Children.Add(brand);

            _topDot = new Ellipse { Width = 8, Height = 8, Fill = LifterUi.Busy, Margin = new Thickness(0, 0, 6, 0) };
            _topText = new TextBlock { Text = "Setup", Foreground = LifterUi.BrushFrom("#93C5FD"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };

            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(_topDot);
            right.Children.Add(_topText);

            var grid = new Grid();
            left.HorizontalAlignment = HorizontalAlignment.Left;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(left);
            grid.Children.Add(right);

            return new Border { Background = LifterUi.Navy, Height = 58, Child = grid };
        }

        private Border BuildCard()
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = LifterUi.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(16),
                Padding = new Thickness(18),
                VerticalAlignment = VerticalAlignment.Top
            };

            var stack = new StackPanel();
            card.Child = stack;

            TextBlock kicker = LifterUi.MakeText("FIRST RUN", 10, LifterUi.Accent);
            kicker.FontWeight = FontWeights.Bold;
            kicker.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(kicker);

            stack.Children.Add(new TextBlock { Text = "STROKE_Distance not found", FontSize = 17, FontWeight = FontWeights.Bold });

            stack.Children.Add(new TextBlock
            {
                FontSize = 11,
                Foreground = LifterUi.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 12),
                Text = "This value is measured once from the main body's bounding box width and becomes the single source of truth for every lifter instance of this CATPart."
            });

            if (!string.IsNullOrEmpty(_documentName))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Destination: " + _documentName,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LifterUi.Slate,
                    Margin = new Thickness(0, 0, 0, 12),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            _detectDot = new Ellipse { Width = 8, Height = 8, Fill = LifterUi.Busy, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            _detectText = new TextBlock { Text = "Detecting the main body…", FontSize = 11, Foreground = LifterUi.Slate, VerticalAlignment = VerticalAlignment.Center };
            var detectRow = new StackPanel { Orientation = Orientation.Horizontal };
            detectRow.Children.Add(_detectDot);
            detectRow.Children.Add(_detectText);
            stack.Children.Add(new Border
            {
                Background = LifterUi.BrushFrom("#EFF6FF"),
                BorderBrush = LifterUi.BrushFrom("#BFDBFE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 12),
                Child = detectRow
            });

            _progressText = new TextBlock { Text = "Measuring the bounding box width…", FontSize = 10, Foreground = LifterUi.Muted, Margin = new Thickness(0, 0, 0, 6) };
            var progress = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 8,
                Background = LifterUi.BrushFrom("#E2E8F0"),
                BorderThickness = new Thickness(0),
                Foreground = LifterUi.Accent
            };
            _progressWrap = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            _progressWrap.Children.Add(_progressText);
            _progressWrap.Children.Add(progress);
            stack.Children.Add(_progressWrap);

            _valueText = new TextBlock { Text = "—", FontSize = 26, FontWeight = FontWeights.Bold };
            var valueRow = new StackPanel { Orientation = Orientation.Horizontal };
            valueRow.Children.Add(_valueText);
            valueRow.Children.Add(new TextBlock { Text = "mm", FontSize = 12, Foreground = LifterUi.Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 9, 0, 0) });
            var okStack = new StackPanel();
            okStack.Children.Add(valueRow);
            okStack.Children.Add(new TextBlock { Text = "STROKE_Distance created — the Lifter dashboard opens next.", FontSize = 10, Foreground = LifterUi.BrushFrom("#166534"), Margin = new Thickness(0, 4, 0, 0) });
            _resultOk = new Border
            {
                Background = LifterUi.BrushFrom("#F0FDF4"),
                BorderBrush = LifterUi.BrushFrom("#BBF7D0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = okStack,
                Visibility = Visibility.Collapsed
            };
            stack.Children.Add(_resultOk);

            _failText = new TextBlock { Text = "The body could not be measured.", FontSize = 10, Foreground = LifterUi.BrushFrom("#B91C1C"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            var failStack = new StackPanel();
            failStack.Children.Add(new TextBlock { Text = "Measurement failed", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = LifterUi.BrushFrom("#991B1B") });
            failStack.Children.Add(_failText);
            _resultFail = new Border
            {
                Background = LifterUi.BrushFrom("#FEF2F2"),
                BorderBrush = LifterUi.BrushFrom("#FECACA"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = failStack,
                Visibility = Visibility.Collapsed
            };
            stack.Children.Add(_resultFail);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            Button cancelButton = LifterUi.MakeButton("Cancel", "ghost", CancelButton_OnClick);
            Button manualButton = LifterUi.MakeButton("Choose another body", "secondary", ManualButton_OnClick);
            manualButton.Margin = new Thickness(8, 0, 0, 0);
            _retryButton = LifterUi.MakeButton("Retry", "secondary", RetryButton_OnClick);
            _retryButton.Margin = new Thickness(8, 0, 0, 0);
            _retryButton.Visibility = Visibility.Collapsed;
            _continueButton = LifterUi.MakeButton("Continue", "primary", ContinueButton_OnClick);
            _continueButton.Margin = new Thickness(8, 0, 0, 0);
            _continueButton.IsEnabled = false;
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(manualButton);
            buttons.Children.Add(_retryButton);
            buttons.Children.Add(_continueButton);
            stack.Children.Add(buttons);

            stack.Children.Add(new TextBlock
            {
                Text = "Setup runs only once per CATPart — the next launch opens the Lifter dashboard directly.",
                FontSize = 9,
                Foreground = LifterUi.BrushFrom("#94A3B8"),
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            return card;
        }

        // ------------------------------------------------------------------
        // Measurement flow
        // ------------------------------------------------------------------

        private void DetectAndMeasure()
        {
            if (_part == null)
            {
                _detectDot.Fill = LifterUi.Danger;
                _detectText.Text = "The destination CATPart is not available.";
                Fail("The destination CATPart is not available.");
                return;
            }

            SetWorking("Detecting the main body…");
            Exception detectError;
            _detectedBody = LifterEngine.DetectMainBody(_part, out detectError);
            if (_detectedBody == null)
            {
                _detectDot.Fill = LifterUi.Danger;
                _detectText.Text = "No main body found — use 'Choose another body'.";
                string reason = detectError != null
                    ? LifterEngine.FriendlyCatiaError(detectError)
                    : "This CATPart contains no solid Body to measure.";
                Fail("The main body could not be detected.\n" + reason
                     + "\n\nIf a CATIA dialog is open (e.g. Insert Object), close it and click Retry — or pick the body manually.");
                return;
            }

            _detectDot.Fill = LifterUi.Success;
            _detectText.Text = "Main body detected — " + LifterEngine.GetName(_detectedBody);
            MeasureBody(_detectedBody);
        }

        private void MeasureBody(dynamic body)
        {
            SetWorking("Measuring the bounding box width (Y direction)…");
            Exception measureError = null;
            bool ok = body != null && LifterEngine.MeasureStrokeOnBody(_part, body, out measureError);
            if (ok)
            {
                Succeed(LifterEngine.GetStrokeText(_part));
            }
            else
            {
                string reason = measureError != null
                    ? LifterEngine.FriendlyCatiaError(measureError)
                    : "The body could not be measured (it may be empty).";
                Fail(reason + "\n\nIf a CATIA dialog is open, close it and click Retry — or choose another body.");
            }
        }

        // ------------------------------------------------------------------
        // UI states
        // ------------------------------------------------------------------

        private void SetWorking(string text)
        {
            _progressWrap.Visibility = Visibility.Visible;
            _progressText.Text = text;
            _resultOk.Visibility = Visibility.Collapsed;
            _resultFail.Visibility = Visibility.Collapsed;
            _continueButton.IsEnabled = false;
            _retryButton.Visibility = Visibility.Collapsed;
            _topDot.Fill = LifterUi.Busy;
            _topText.Text = "Working";
        }

        private void Succeed(string value)
        {
            _progressWrap.Visibility = Visibility.Collapsed;
            _resultOk.Visibility = Visibility.Visible;
            _resultFail.Visibility = Visibility.Collapsed;
            _valueText.Text = string.IsNullOrEmpty(value) ? "—" : value;
            _continueButton.IsEnabled = true;
            _retryButton.Visibility = Visibility.Collapsed;
            _topDot.Fill = LifterUi.Success;
            _topText.Text = "Done";
        }

        private void Fail(string message)
        {
            _progressWrap.Visibility = Visibility.Collapsed;
            _resultOk.Visibility = Visibility.Collapsed;
            _resultFail.Visibility = Visibility.Visible;
            _failText.Text = message;
            // STROKE_Distance is mandatory for the lifter workflow (single
            // source of truth for every instance) — the only ways out of a
            // failed measurement are Retry, choosing another body, or Cancel.
            _continueButton.IsEnabled = false;
            _retryButton.Visibility = Visibility.Visible;
            _topDot.Fill = LifterUi.Danger;
            _topText.Text = "Failed";
        }

        // ------------------------------------------------------------------
        // Buttons
        // ------------------------------------------------------------------

        private void ContinueButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void RetryButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_detectedBody != null) MeasureBody(_detectedBody);
            else DetectAndMeasure();
        }

        private async void ManualButton_OnClick(object sender, RoutedEventArgs e)
        {
            SetWorking("Select the main body in CATIA…");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            try
            {
                try { _document.Activate(); } catch { }
                Hide();
                dynamic body;
                string error;
                bool picked = LifterEngine.SelectBodyFromUser(
                    _document, "Select the MAIN body for STROKE_Distance…", out body, out error);
                Show();
                if (!picked)
                {
                    Fail(error);
                    return;
                }
                _detectedBody = body;
                _detectDot.Fill = LifterUi.Success;
                _detectText.Text = "Main body picked — " + LifterEngine.GetName(body);
                MeasureBody(body);
            }
            catch (Exception ex)
            {
                try { Show(); } catch { }
                Fail(LifterEngine.FriendlyCatiaError(ex));
            }
        }
    }
}
