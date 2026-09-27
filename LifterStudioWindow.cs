using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ProfessionalPowerCopyCatalogModern
{
    // ================================================================
    // LifterUi - shared UI factory. The Lifter windows are built 100%
    // in C# (no XAML, no Page build action, no generated .g.cs), so
    // adding the two .cs files to the project is all that is needed.
    // ================================================================
    internal static class LifterUi
    {
        public static readonly SolidColorBrush Navy = BrushFrom("#0F172A");
        public static readonly SolidColorBrush Slate = BrushFrom("#334155");
        public static readonly SolidColorBrush Muted = BrushFrom("#64748B");
        public static readonly SolidColorBrush CanvasBrush = BrushFrom("#F5F7FB");
        public static readonly SolidColorBrush Accent = BrushFrom("#2563EB");
        public static readonly SolidColorBrush Success = BrushFrom("#16A34A");
        public static readonly SolidColorBrush Danger = BrushFrom("#DC2626");
        public static readonly SolidColorBrush Busy = BrushFrom("#F59E0B");
        public static readonly SolidColorBrush CardBorder = BrushFrom("#E2E8F0");
        public static readonly SolidColorBrush FieldBorder = BrushFrom("#CBD5E1");
        public static readonly SolidColorBrush FieldBackground = BrushFrom("#FBFCFE");
        public static readonly SolidColorBrush FieldReadonlyBackground = BrushFrom("#EEF2F6");

        public static SolidColorBrush BrushFrom(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public static TextBlock MakeText(string text, double fontSize, Brush foreground)
        {
            return new TextBlock { Text = text, FontSize = fontSize, Foreground = foreground };
        }

        public static TextBlock MakeLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFrom("#475569"),
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        public static TextBlock MakeHint(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 9,
                Foreground = BrushFrom("#94A3B8"),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static Border MakeCard()
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        public static Border MakeUnitPill(string unit)
        {
            return new Border
            {
                Background = BrushFrom("#E9EEF3"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 4, 7, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = unit, FontSize = 9, Foreground = Muted }
            };
        }

        public static Border MakeBadge(string text)
        {
            return new Border
            {
                Background = BrushFrom("#94A3B8"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            };
        }

        /// <summary>kind: "primary" | "secondary" | "ghost" | "danger"</summary>
        public static Button MakeButton(string text, string kind, RoutedEventHandler click)
        {
            Brush background;
            Brush foreground;
            switch (kind)
            {
                case "primary": background = Accent; foreground = Brushes.White; break;
                case "danger": background = Danger; foreground = Brushes.White; break;
                case "secondary": background = BrushFrom("#E8EEF7"); foreground = Slate; break;
                default: background = Brushes.Transparent; foreground = Muted; break;
            }

            var button = new Button
            {
                Content = text,
                Background = background,
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 10, 16, 10),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            if (click != null) button.Click += click;
            button.Template = CreateButtonTemplate();
            return button;
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
            template.Triggers.Add(pressed);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabled);

            return template;
        }

        public static TextBox MakeValueBox()
        {
            var box = new TextBox
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy,
                Background = FieldBackground,
                BorderBrush = FieldBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 8, 9, 8),
                CaretBrush = Accent
            };
            box.Template = CreateTextBoxTemplate();
            return box;
        }

        public static TextBox MakeReadOnlyBox(string text)
        {
            var box = MakeValueBox();
            box.Text = text;
            box.IsReadOnly = true;
            box.FontSize = 12;
            box.Foreground = Muted;
            box.Background = FieldReadonlyBackground;
            return box;
        }

        private static ControlTemplate CreateTextBoxTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(FrameworkElement.NameProperty, "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TextBox.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(TextBox.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(TextBox.BorderThicknessProperty));

            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.SetValue(FrameworkElement.NameProperty, "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(TextBox.PaddingProperty));
            host.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            border.AppendChild(host);

            var template = new ControlTemplate(typeof(TextBox)) { VisualTree = border };

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent) { TargetName = "Bd" });
            focus.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White) { TargetName = "Bd" });
            template.Triggers.Add(focus);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Border.BackgroundProperty, FieldReadonlyBackground) { TargetName = "Bd" });
            disabled.Setters.Add(new Setter(Border.BorderBrushProperty, CardBorder) { TargetName = "Bd" });
            template.Triggers.Add(disabled);

            return template;
        }
    }

    /// <summary>
    /// WPF port of the "Lifter Studio" HTA dashboard (catvba v2), built entirely
    /// in code so it needs no XAML Page item. The VBA &lt;-&gt; HTA temp-file
    /// command protocol is replaced by direct COM calls through LifterEngine:
    /// REFRESH = TryRefreshInstances, UPDATE = ApplyButton, SELECTBODY =
    /// PickBody, REMOVEONE = RemoveInstanceButton, POWERCOPY = NewInstanceButton.
    /// Per-instance copy/target body pairs are kept in memory exactly like the
    /// VBA module-level collections.
    /// </summary>
    public class LifterStudioWindow : Window
    {
        private readonly INFITF.Application _catia;
        private readonly MECMOD.PartDocument _document;
        private readonly Func<CatalogItem, Task<bool>> _launchInstantiate;
        private dynamic _part;
        private bool _busy;
        private int _current = 1;
        private int _count;

        public CatalogItem Item { get; }
        public MECMOD.PartDocument Document { get { return _document; } }

        private sealed class LifterInstanceRow
        {
            public int Index { get; set; }
            public string Title { get; set; }
            public string Meta { get; set; }
        }

        private sealed class StoredBody
        {
            public object Body;
            public string Name;
        }

        private readonly Dictionary<int, StoredBody> _copyBodies = new Dictionary<int, StoredBody>();
        private readonly Dictionary<int, StoredBody> _targetBodies = new Dictionary<int, StoredBody>();

        // ---- controls (built in BuildUi) ----
        private Grid _mainContent;
        private ListBox _instanceList;
        private TextBlock _emptyListText;
        private TextBlock _sectionTag;
        private TextBlock _rmTag;
        private TextBox _upperBox;
        private TextBox _verticalBox;
        private TextBox _horizontalBox;
        private TextBox _headLengthBox;
        private TextBox _strokeBox;
        private TextBox _draftBox;
        private CheckBox _externalFaceCheck;
        private TextBlock _externalFaceText;
        private TextBlock _statCountText;
        private TextBlock _statStrokeText;
        private TextBlock _statDraftText;
        private TextBox _copyBodyBox;
        private TextBox _targetBodyBox;
        private Border _rmBox;
        private TextBlock _rmInfoText;
        private Ellipse _statusDot;
        private TextBlock _statusText;
        private TextBlock _dirtyText;
        private Ellipse _connDot;
        private TextBlock _connText;
        private TextBlock _docNameText;

        public LifterStudioWindow(Window owner, INFITF.Application catia, MECMOD.PartDocument document,
                                  CatalogItem item, Func<CatalogItem, Task<bool>> launchInstantiate)
        {
            _catia = catia;
            _document = document;
            Item = item;
            _launchInstantiate = launchInstantiate;
            try { _part = document.Part; } catch { _part = null; }

            Title = "Lifter Studio — " + DocumentName();
            Width = 940;
            Height = 720;
            MinWidth = 840;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = LifterUi.CanvasBrush;
            Owner = owner;

            BuildUi();

            _upperBox.TextChanged += delegate { MarkDirty(); };
            _verticalBox.TextChanged += delegate { MarkDirty(); };
            _horizontalBox.TextChanged += delegate { MarkDirty(); };
            _headLengthBox.TextChanged += delegate { MarkDirty(); };
            _externalFaceCheck.Checked += delegate { _externalFaceText.Text = "Yes"; MarkDirty(); };
            _externalFaceCheck.Unchecked += delegate { _externalFaceText.Text = "No"; MarkDirty(); };

            Loaded += delegate
            {
                SetStatus("Complete CATIA's Insert Object dialog, then click Refresh instances.", "busy");
                TryRefreshInstances(true);
            };
            Activated += delegate { if (!_busy) TryRefreshInstances(true); };
        }

        private string DocumentName()
        {
            try { return _document.get_Name(); }
            catch { return "active CATPart"; }
        }

        // ================================================================
        // UI construction (pure code - no XAML)
        // ================================================================

        private void BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // tiles
            root.RowDefinitions.Add(new RowDefinition());                              // main
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer

            Border header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            StackPanel tiles = BuildTiles();
            Grid.SetRow(tiles, 1);
            root.Children.Add(tiles);

            _mainContent = BuildMain();
            Grid.SetRow(_mainContent, 2);
            root.Children.Add(_mainContent);

            Border footer = BuildFooter();
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
        }

        private Border BuildHeader()
        {
            var logo = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(9),
                Background = LifterUi.Accent,
                Child = new TextBlock
                {
                    Text = "L", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };

            var brand = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(new TextBlock { Text = "Lifter Studio", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold });
            brand.Children.Add(new TextBlock { Text = "PowerCopy parameter control", Foreground = LifterUi.BrushFrom("#7E96A5"), FontSize = 10 });

            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(logo);
            left.Children.Add(brand);

            _docNameText = new TextBlock { Text = DocumentName(), Foreground = LifterUi.BrushFrom("#93C5FD"), FontSize = 10 };
            var docPill = new Border
            {
                Background = LifterUi.BrushFrom("#1E293B"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _docNameText
            };
            _connDot = new Ellipse { Width = 8, Height = 8, Fill = LifterUi.BrushFrom("#22C55E"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _connText = new TextBlock { Text = "Connected", Foreground = LifterUi.BrushFrom("#93C5FD"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };

            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(docPill);
            right.Children.Add(_connDot);
            right.Children.Add(_connText);

            var grid = new Grid();
            left.HorizontalAlignment = HorizontalAlignment.Left;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(left);
            grid.Children.Add(right);

            return new Border { Background = LifterUi.Navy, Height = 62, Child = grid };
        }

        private static Border MakeStatCard(UIElement value, string label)
        {
            var panel = new StackPanel();
            panel.Children.Add(value);
            panel.Children.Add(new TextBlock { Text = label, FontSize = 9, Foreground = LifterUi.Muted, Margin = new Thickness(0, 2, 0, 0) });
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = LifterUi.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 10, 0),
                Child = panel
            };
        }

        private StackPanel BuildTiles()
        {
            var tiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 14, 18, 0) };

            _statCountText = new TextBlock { Text = "0", FontSize = 18, FontWeight = FontWeights.Bold };
            tiles.Children.Add(MakeStatCard(_statCountText, "INSTANCES"));

            _statStrokeText = new TextBlock { Text = "—", FontSize = 18, FontWeight = FontWeights.Bold };
            var strokeRow = new StackPanel { Orientation = Orientation.Horizontal };
            strokeRow.Children.Add(_statStrokeText);
            strokeRow.Children.Add(new TextBlock { Text = "mm", FontSize = 10, Foreground = LifterUi.Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 7, 0, 0) });
            tiles.Children.Add(MakeStatCard(strokeRow, "STROKE DISTANCE"));

            _statDraftText = new TextBlock { Text = "—", FontSize = 18, FontWeight = FontWeights.Bold };
            var draftRow = new StackPanel { Orientation = Orientation.Horizontal };
            draftRow.Children.Add(_statDraftText);
            draftRow.Children.Add(new TextBlock { Text = "°", FontSize = 12, Foreground = LifterUi.Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 4, 0, 0) });
            tiles.Children.Add(MakeStatCard(draftRow, "DRAFT ANGLE"));

            return tiles;
        }

        private Grid BuildMain()
        {
            var grid = new Grid { Margin = new Thickness(18, 12, 18, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            Border rail = BuildRail();
            Grid.SetColumn(rail, 0);
            grid.Children.Add(rail);

            ScrollViewer cards = BuildCards();
            Grid.SetColumn(cards, 2);
            grid.Children.Add(cards);

            return grid;
        }

        private Border BuildRail()
        {
            _instanceList = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            _instanceList.ItemTemplate = CreateInstanceTemplate();
            _instanceList.ItemContainerStyle = CreateInstanceContainerStyle();
            _instanceList.SelectionChanged += InstanceList_OnSelectionChanged;

            _emptyListText = new TextBlock
            {
                Text = "No lifter instance yet. Complete the CATIA insertion, then click Refresh instances.",
                FontSize = 10,
                Foreground = LifterUi.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 2, 0),
                Visibility = Visibility.Collapsed
            };

            Button newButton = LifterUi.MakeButton("+ New instance", "secondary", NewInstanceButton_OnClick);
            newButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            newButton.Margin = new Thickness(0, 10, 0, 0);

            var note = new TextBlock
            {
                FontSize = 9,
                Foreground = LifterUi.BrushFrom("#94A3B8"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 2, 0),
                Text = "Instances are detected automatically from the UNDERCUT_DEPTH and LIFTER_HEAD parameter sets of the CATPart."
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock { Text = "POWER COPY INSTANCES", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = LifterUi.Muted, Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(title, 0);
            Grid.SetRow(_instanceList, 1);
            Grid.SetRow(_emptyListText, 1);
            Grid.SetRow(newButton, 2);
            Grid.SetRow(note, 3);
            grid.Children.Add(title);
            grid.Children.Add(_instanceList);
            grid.Children.Add(_emptyListText);
            grid.Children.Add(newButton);
            grid.Children.Add(note);

            Border card = LifterUi.MakeCard();
            card.Child = grid;
            return card;
        }

        private static DataTemplate CreateInstanceTemplate()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 10, 12, 10));

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetValue(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.FontSizeProperty, 12.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

            var meta = new FrameworkElementFactory(typeof(TextBlock));
            meta.SetValue(TextBlock.TextProperty, new Binding("Meta"));
            meta.SetValue(TextBlock.FontSizeProperty, 10.0);
            meta.SetValue(TextBlock.ForegroundProperty, LifterUi.Muted);
            meta.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));

            panel.AppendChild(title);
            panel.AppendChild(meta);
            return new DataTemplate { VisualTree = panel };
        }

        private static Style CreateInstanceContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0, 0, 0, 6)));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(FrameworkElement.NameProperty, "Bd");
            border.SetValue(Border.BackgroundProperty, Brushes.White);
            border.SetValue(Border.BorderBrushProperty, LifterUi.CardBorder);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));

            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BorderBrushProperty, LifterUi.Accent) { TargetName = "Bd" });
            selected.Setters.Add(new Setter(Border.BackgroundProperty, LifterUi.BrushFrom("#EFF6FF")) { TargetName = "Bd" });
            template.Triggers.Add(selected);

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, LifterUi.BrushFrom("#93C5FD")) { TargetName = "Bd" });
            template.Triggers.Add(hover);

            style.Setters.Add(new Setter(ListBoxItem.TemplateProperty, template));
            return style;
        }

        private ScrollViewer BuildCards()
        {
            var stack = new StackPanel();
            stack.Children.Add(BuildUndercutCard());
            stack.Children.Add(BuildHeadCard());
            stack.Children.Add(BuildRemoveCard());

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        }

        private Border BuildUndercutCard()
        {
            Border card = LifterUi.MakeCard();
            var stack = new StackPanel();
            card.Child = stack;

            _sectionTag = new TextBlock { Text = "Instance 001", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = LifterUi.BrushFrom("#1D4ED8") };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock kicker = LifterUi.MakeText("UNDERCUT DEPTH", 10, LifterUi.Accent);
            kicker.FontWeight = FontWeights.Bold;
            kicker.VerticalAlignment = VerticalAlignment.Center;
            var tagPill = new Border { Background = LifterUi.BrushFrom("#EFF6FF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), Child = _sectionTag };
            Grid.SetColumn(tagPill, 1);
            header.Children.Add(kicker);
            header.Children.Add(tagPill);
            stack.Children.Add(header);

            var fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fields.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _upperBox = LifterUi.MakeValueBox();
            StackPanel upperField = MakeFieldBlock("Upper internal distance", _upperBox, "mm", "Tooling-side clearance", null);
            Grid.SetRow(upperField, 0);
            Grid.SetColumn(upperField, 0);

            _verticalBox = LifterUi.MakeValueBox();
            StackPanel verticalField = MakeFieldBlock("Internal vertical angle", _verticalBox, "deg", "0 – 90 typical", null);
            Grid.SetRow(verticalField, 0);
            Grid.SetColumn(verticalField, 2);

            _horizontalBox = LifterUi.MakeValueBox();
            StackPanel horizontalField = MakeFieldBlock("Internal horizontal angle", _horizontalBox, "deg", "0 – 90 typical", null);
            Grid.SetRow(horizontalField, 2);
            Grid.SetColumn(horizontalField, 0);

            var externalField = new StackPanel();
            externalField.Children.Add(LifterUi.MakeLabel("Has external face"));
            var boolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            _externalFaceCheck = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            _externalFaceText = new TextBlock { Text = "No", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            boolRow.Children.Add(_externalFaceCheck);
            boolRow.Children.Add(_externalFaceText);
            externalField.Children.Add(boolRow);
            externalField.Children.Add(LifterUi.MakeHint("Face visible on molded side"));
            Grid.SetRow(externalField, 2);
            Grid.SetColumn(externalField, 2);

            fields.Children.Add(upperField);
            fields.Children.Add(verticalField);
            fields.Children.Add(horizontalField);
            fields.Children.Add(externalField);
            stack.Children.Add(fields);
            return card;
        }

        private StackPanel MakeFieldBlock(string label, TextBox box, string unit, string hint, string badge)
        {
            var field = new StackPanel();

            if (!string.IsNullOrEmpty(badge))
            {
                var labelRow = new StackPanel { Orientation = Orientation.Horizontal };
                labelRow.Children.Add(LifterUi.MakeLabel(label));
                labelRow.Children.Add(LifterUi.MakeBadge(badge));
                field.Children.Add(labelRow);
            }
            else
            {
                field.Children.Add(LifterUi.MakeLabel(label));
            }

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border pill = LifterUi.MakeUnitPill(unit);
            Grid.SetColumn(pill, 1);
            row.Children.Add(box);
            row.Children.Add(pill);
            field.Children.Add(row);

            if (!string.IsNullOrEmpty(hint)) field.Children.Add(LifterUi.MakeHint(hint));
            return field;
        }

        private Border BuildHeadCard()
        {
            Border card = LifterUi.MakeCard();
            var stack = new StackPanel();
            card.Child = stack;

            TextBlock kicker = LifterUi.MakeText("LIFTER HEAD", 10, LifterUi.Accent);
            kicker.FontWeight = FontWeights.Bold;
            kicker.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(kicker);

            var fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fields.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _headLengthBox = LifterUi.MakeValueBox();
            StackPanel lengthField = MakeFieldBlock("Lifter head length", _headLengthBox, "mm", "UNDERCUT_LENGTH of this instance", null);
            Grid.SetRow(lengthField, 0);
            Grid.SetColumn(lengthField, 0);

            _strokeBox = LifterUi.MakeReadOnlyBox("—");
            StackPanel strokeField = MakeFieldBlock("Stroke distance", _strokeBox, "mm",
                "Linked to the main body (single source of truth)", "AUTO");
            Grid.SetRow(strokeField, 0);
            Grid.SetColumn(strokeField, 2);

            _draftBox = LifterUi.MakeReadOnlyBox("—");
            StackPanel draftField = MakeFieldBlock("Draft angle", _draftBox, "deg",
                "From formula - rounded to the degree, capped at 15°", "MAX 15°");
            Grid.SetRow(draftField, 2);
            Grid.SetColumn(draftField, 0);

            Button reMeasure = LifterUi.MakeButton("Re-measure stroke from main body", "ghost", ReMeasureButton_OnClick);
            reMeasure.Padding = new Thickness(10, 6, 10, 6);
            reMeasure.HorizontalAlignment = HorizontalAlignment.Left;
            reMeasure.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(reMeasure, 2);
            Grid.SetColumn(reMeasure, 2);

            fields.Children.Add(lengthField);
            fields.Children.Add(strokeField);
            fields.Children.Add(draftField);
            fields.Children.Add(reMeasure);
            stack.Children.Add(fields);
            return card;
        }

        private Border BuildRemoveCard()
        {
            Border card = LifterUi.MakeCard();
            var stack = new StackPanel();
            card.Child = stack;

            _rmTag = new TextBlock { Text = "Instance 001", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = LifterUi.BrushFrom("#1D4ED8") };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock kicker = LifterUi.MakeText("BOOLEAN REMOVE", 10, LifterUi.Accent);
            kicker.FontWeight = FontWeights.Bold;
            kicker.VerticalAlignment = VerticalAlignment.Center;
            var tagPill = new Border { Background = LifterUi.BrushFrom("#EFF6FF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), Child = _rmTag };
            Grid.SetColumn(tagPill, 1);
            header.Children.Add(kicker);
            header.Children.Add(tagPill);
            stack.Children.Add(header);

            var fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fields.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fields.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel copyField = MakeBodyField("Copy body (tool)", out _copyBodyBox, "Select in CATIA", CopyBodyButton_OnClick, "Copied and pasted as result");
            Grid.SetRow(copyField, 0);
            Grid.SetColumn(copyField, 0);

            StackPanel targetField = MakeBodyField("Target body (to cut)", out _targetBodyBox, "Select in CATIA", TargetBodyButton_OnClick, "Receives the Remove feature");
            Grid.SetRow(targetField, 0);
            Grid.SetColumn(targetField, 2);

            var resultField = new StackPanel();
            resultField.Children.Add(LifterUi.MakeLabel("Last result"));
            _rmInfoText = new TextBlock { Text = "No remove executed yet.", FontSize = 10, Foreground = LifterUi.BrushFrom("#475569"), TextWrapping = TextWrapping.Wrap };
            _rmBox = new Border
            {
                Background = LifterUi.BrushFrom("#F8FAFC"),
                BorderBrush = LifterUi.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                MinHeight = 42,
                Child = _rmInfoText
            };
            resultField.Children.Add(_rmBox);
            Grid.SetRow(resultField, 2);
            Grid.SetColumn(resultField, 0);
            Grid.SetColumnSpan(resultField, 3);

            Button removeButton = LifterUi.MakeButton("Remove this instance", "danger", RemoveInstanceButton_OnClick);
            removeButton.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(removeButton, 4);
            Grid.SetColumn(removeButton, 0);
            Grid.SetColumnSpan(removeButton, 3);

            fields.Children.Add(copyField);
            fields.Children.Add(targetField);
            fields.Children.Add(resultField);
            fields.Children.Add(removeButton);
            stack.Children.Add(fields);
            return card;
        }

        private StackPanel MakeBodyField(string label, out TextBox box, string buttonText, RoutedEventHandler click, string hint)
        {
            var field = new StackPanel();
            field.Children.Add(LifterUi.MakeLabel(label));
            box = LifterUi.MakeReadOnlyBox("Not selected");
            field.Children.Add(box);
            Button pick = LifterUi.MakeButton(buttonText, "ghost", click);
            pick.Padding = new Thickness(10, 6, 10, 6);
            pick.HorizontalAlignment = HorizontalAlignment.Left;
            pick.Margin = new Thickness(0, 6, 0, 0);
            field.Children.Add(pick);
            field.Children.Add(LifterUi.MakeHint(hint));
            return field;
        }

        private Border BuildFooter()
        {
            _statusDot = new Ellipse { Width = 8, Height = 8, Fill = LifterUi.BrushFrom("#94A3B8"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _statusText = new TextBlock { Text = "Ready", FontSize = 10, Foreground = LifterUi.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _dirtyText = new TextBlock { Text = "Unsaved changes", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = LifterUi.BrushFrom("#C07A1F"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(_statusDot);
            left.Children.Add(_statusText);
            left.Children.Add(_dirtyText);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            Button closeButton = LifterUi.MakeButton("Close", "secondary", CloseButton_OnClick);
            Button refreshButton = LifterUi.MakeButton("Refresh instances", "secondary", RefreshButton_OnClick);
            refreshButton.Margin = new Thickness(8, 0, 0, 0);
            Button applyButton = LifterUi.MakeButton("Apply and Update", "primary", ApplyButton_OnClick);
            applyButton.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(closeButton);
            right.Children.Add(refreshButton);
            right.Children.Add(applyButton);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.HorizontalAlignment = HorizontalAlignment.Left;
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = LifterUi.CardBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(18, 10, 18, 12),
                Child = grid
            };
        }

        // ================================================================
        // Busy wrapper: disable the UI, paint, then run the blocking COM work
        // ================================================================

        private async Task RunBusyAsync(string message, Action action)
        {
            if (_busy) return;
            _busy = true;
            _mainContent.IsEnabled = false;
            try { Mouse.OverrideCursor = Cursors.Wait; } catch { }
            SetStatus(message, "busy");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            try
            {
                action();
            }
            finally
            {
                _busy = false;
                _mainContent.IsEnabled = true;
                try { Mouse.OverrideCursor = null; } catch { }
            }
        }

        // ================================================================
        // REFRESH - instance discovery + automatic STROKE link / Draft fix-up
        // ================================================================

        private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            RunBusyAsync("Reloading instances…", delegate { TryRefreshInstances(false); });
        }

        private void TryRefreshInstances(bool silent)
        {
            if (_part == null)
            {
                if (!silent) SetStatus("The destination CATPart is not available.", "err");
                return;
            }
            try
            {
                int linked = LifterEngine.LinkStrokeToMainBody(_part);
                int drafts = LifterEngine.CreateDraftParameters(_part);
                _count = LifterEngine.InstanceCount(_part);
                RebuildInstanceList();
                LoadInstance(Math.Min(Math.Max(_current, 1), Math.Max(_count, 1)));

                if (_count == 0)
                    SetStatus("No lifter instance detected yet. Complete the CATIA insertion, then click Refresh instances.", "busy");
                else
                    SetStatus("Loaded " + _count + " instance(s)" +
                              (linked > 0 ? " • " + linked + " STROKE link(s) created" : "") +
                              (drafts > 0 ? " • " + drafts + " Draft formula(s) refreshed" : "") + ".", "ok");
            }
            catch (Exception ex)
            {
                if (!silent) SetStatus(LifterEngine.FriendlyCatiaError(ex), "err");
            }
        }

        private void RebuildInstanceList()
        {
            var rows = new List<LifterInstanceRow>();
            for (int i = 1; i <= _count; i++)
            {
                string meta = "No draft";
                try
                {
                    string status;
                    LifterInstanceSnapshot snapshot = LifterEngine.ReadInstance(_part, i, out status);
                    if (status == "OK")
                    {
                        string draft = string.IsNullOrEmpty(snapshot.Draft) || snapshot.Draft == "-" ? null : snapshot.Draft;
                        string head = string.IsNullOrEmpty(snapshot.UndercutLength) ? "—" : snapshot.UndercutLength;
                        meta = draft != null ? "Draft " + draft + "° • Head " + head + " mm" : "Head " + head + " mm";
                    }
                }
                catch { }
                rows.Add(new LifterInstanceRow
                {
                    Index = i,
                    Title = "PowerCopy Instance " + i.ToString("000", CultureInfo.InvariantCulture),
                    Meta = meta
                });
            }

            int restore = Math.Min(Math.Max(_current, 1), Math.Max(_count, 1));
            _instanceList.ItemsSource = rows;
            _emptyListText.Visibility = _count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _instanceList.SelectedIndex = _count == 0 ? -1 : restore - 1;
            _current = _count == 0 ? 1 : restore;

            _statCountText.Text = _count.ToString(CultureInfo.InvariantCulture);
        }

        private void InstanceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_busy) return;
            LifterInstanceRow row = _instanceList.SelectedItem as LifterInstanceRow;
            if (row != null) LoadInstance(row.Index);
        }

        // ================================================================
        // Load one instance into the cards
        // ================================================================

        private void LoadInstance(int index)
        {
            _current = index;
            string tag = "Instance " + index.ToString("000", CultureInfo.InvariantCulture);
            _sectionTag.Text = tag;
            _rmTag.Text = tag;

            if (_part == null || _count == 0 || index > _count)
            {
                ClearFields();
                SetBodySlots(index);
                return;
            }

            string status;
            LifterInstanceSnapshot snapshot;
            try
            {
                snapshot = LifterEngine.ReadInstance(_part, index, out status);
            }
            catch (Exception ex)
            {
                ClearFields();
                SetBodySlots(index);
                SetStatus(LifterEngine.FriendlyCatiaError(ex), "err");
                return;
            }

            _upperBox.Text = snapshot.UpperInternalLength;
            _verticalBox.Text = snapshot.InternalVerticalAngle;
            _horizontalBox.Text = snapshot.InternalHorizontalAngle;
            _headLengthBox.Text = snapshot.UndercutLength;
            bool external = string.Equals(snapshot.HasExternalFace, "true", StringComparison.OrdinalIgnoreCase);
            _externalFaceCheck.IsChecked = external;
            _externalFaceText.Text = external ? "Yes" : "No";
            _strokeBox.Text = snapshot.Stroke.Length > 0 ? snapshot.Stroke : "—";
            _draftBox.Text = string.IsNullOrEmpty(snapshot.Draft) ? "—" : snapshot.Draft;
            _statStrokeText.Text = snapshot.Stroke.Length > 0 ? snapshot.Stroke : "—";
            _statDraftText.Text = string.IsNullOrEmpty(snapshot.Draft) ? "—" : snapshot.Draft;
            ClearDirty();

            if (status == "INCOMPLETE")
                SetStatus("Instance " + tag + " is missing one or more parameters.", "err");

            SetBodySlots(index);
        }

        private void ClearFields()
        {
            _upperBox.Text = "";
            _verticalBox.Text = "";
            _horizontalBox.Text = "";
            _headLengthBox.Text = "";
            _externalFaceCheck.IsChecked = false;
            _externalFaceText.Text = "No";
            _strokeBox.Text = "—";
            _draftBox.Text = "—";
            _statStrokeText.Text = "—";
            _statDraftText.Text = "—";
            ClearDirty();
        }

        private void MarkDirty()
        {
            _dirtyText.Visibility = Visibility.Visible;
        }

        private void ClearDirty()
        {
            _dirtyText.Visibility = Visibility.Collapsed;
        }

        // ================================================================
        // UPDATE - apply the five editable parameters
        // ================================================================

        private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
        {
            string tag = _current.ToString("000", CultureInfo.InvariantCulture);
            RunBusyAsync("Updating instance " + tag + "…", delegate
            {
                if (_part == null)
                {
                    SetStatus("The destination CATPart is not available.", "err");
                    return;
                }
                try
                {
                    var values = new LifterInstanceSnapshot
                    {
                        UpperInternalLength = _upperBox.Text,
                        InternalVerticalAngle = _verticalBox.Text,
                        InternalHorizontalAngle = _horizontalBox.Text,
                        HasExternalFace = _externalFaceCheck.IsChecked == true ? "true" : "false",
                        UndercutLength = _headLengthBox.Text
                    };

                    LifterEngine.UpdateInstance(_part, _current, values);
                    int drafts = LifterEngine.CreateDraftParameters(_part);   // Draft follows the new inputs
                    LoadInstance(_current);
                    ClearDirty();
                    SetStatus("Instance " + tag + " updated" +
                              (drafts > 0 ? " • Draft formula refreshed" : "") + ".", "ok");
                }
                catch (Exception ex)
                {
                    SetStatus(LifterEngine.FriendlyCatiaError(ex), "err");
                }
            });
        }

        // ================================================================
        // SELECTBODY - native CATIA picker for the Boolean Remove pair
        // ================================================================

        private void CopyBodyButton_OnClick(object sender, RoutedEventArgs e)
        {
            PickBodyAsync(true);
        }

        private void TargetBodyButton_OnClick(object sender, RoutedEventArgs e)
        {
            PickBodyAsync(false);
        }

        private async void PickBodyAsync(bool copyBody)
        {
            if (_busy) return;
            string tag = _current.ToString("000", CultureInfo.InvariantCulture);
            string label = copyBody ? "copy body (tool)" : "target body (to cut)";
            await RunBusyAsync("Select the " + label + " for instance " + tag + " in CATIA…", delegate
            {
                try
                {
                    try { _document.Activate(); } catch { }
                    Hide();
                    string prompt = "Select the " + (copyBody ? "COPY BODY (the tool)" : "TARGET BODY (the body to cut)") +
                                    " for Instance " + tag + "…";
                    dynamic body;
                    string error;
                    bool picked = LifterEngine.SelectBodyFromUser(_document, prompt, out body, out error);
                    try { Show(); } catch { }
                    if (!picked)
                    {
                        SetStatus(error, "err");
                        return;
                    }

                    // A body cannot be both the tool and the target of the same cut.
                    Dictionary<int, StoredBody> other = copyBody ? _targetBodies : _copyBodies;
                    StoredBody otherStored;
                    if (other.TryGetValue(_current, out otherStored) && otherStored != null &&
                        LifterEngine.AreSameBody(_part, body, otherStored.Body))
                    {
                        SetStatus("This body is already the " + (copyBody ? "target body" : "copy body") +
                                  " of instance " + tag + " (" + otherStored.Name + ").", "err");
                        return;
                    }

                    Dictionary<int, StoredBody> store = copyBody ? _copyBodies : _targetBodies;
                    store[_current] = new StoredBody { Body = body, Name = LifterEngine.GetName(body) };
                    SetBodySlots(_current);
                    SetStatus((copyBody ? "Copy body" : "Target body") + " stored for instance " + tag + ".", "ok");
                }
                catch (Exception ex)
                {
                    try { Show(); } catch { }
                    SetStatus(LifterEngine.FriendlyCatiaError(ex), "err");
                }
            });
        }

        private void SetBodySlots(int index)
        {
            _copyBodyBox.Text = GetStoredBodyName(_copyBodies, index);
            _targetBodyBox.Text = GetStoredBodyName(_targetBodies, index);
        }

        private string GetStoredBodyName(Dictionary<int, StoredBody> store, int index)
        {
            StoredBody stored;
            if (store.TryGetValue(index, out stored) && stored != null)
            {
                if (_part != null && !LifterEngine.IsBodyUsable(_part, stored.Body))
                {
                    store.Remove(index);       // dead reference: ask for a new one
                    return "Not selected";
                }
                return string.IsNullOrEmpty(stored.Name) ? "Not selected" : stored.Name;
            }
            return "Not selected";
        }

        // ================================================================
        // REMOVEONE - Boolean Remove for the current instance
        // ================================================================

        private void RemoveInstanceButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_count == 0)
            {
                SetStatus("No instance to remove yet.", "err");
                return;
            }
            string tag = _current.ToString("000", CultureInfo.InvariantCulture);

            MessageBoxResult confirm = MessageBox.Show(this,
                "Subtract the copy body from the target body of instance " + tag + "?",
                "Boolean Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            RunBusyAsync("CATIA is pasting and removing…", delegate
            {
                try
                {
                    StoredBody copy;
                    StoredBody target;
                    _copyBodies.TryGetValue(_current, out copy);
                    _targetBodies.TryGetValue(_current, out target);
                    if (copy == null || target == null)
                    {
                        SetRmInfo("Select the copy body and the target body for instance " + tag + " first.", "err");
                        SetStatus("Both bodies must be selected before the Remove.", "err");
                        return;
                    }

                    try { _document.Activate(); } catch { }
                    LifterRemoveResult result = LifterEngine.RunBooleanRemove(_part, _document, copy.Body, target.Body, _current);

                    if (result.DeadSlot == "copy") _copyBodies.Remove(_current);
                    if (result.DeadSlot == "target") _targetBodies.Remove(_current);

                    SetRmInfo(result.Message, result.Ok ? "ok" : "err");
                    SetStatus(result.Ok
                        ? "Boolean Remove completed — feature " + result.FeatureName + "."
                        : result.Message, result.Ok ? "ok" : "err");
                    if (result.Ok) TryRefreshInstances(true);
                }
                catch (Exception ex)
                {
                    string friendly = LifterEngine.FriendlyCatiaError(ex);
                    SetRmInfo(friendly, "err");
                    SetStatus(friendly, "err");
                }
            });
        }

        // ================================================================
        // POWERCOPY - launch another instantiation through the main dashboard
        // ================================================================

        private async void NewInstanceButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            SetStatus("Preparing a new PowerCopy insertion in CATIA…", "busy");
            bool ok = false;
            if (_launchInstantiate != null) ok = await _launchInstantiate(Item);
            if (!ok)
            {
                SetStatus("The PowerCopy insertion could not be started. Check that CATIA and the destination CATPart are open.", "err");
                return;
            }
            SetStatus("Complete CATIA's Insert Object dialog, then click Refresh instances.", "busy");
        }

        // ================================================================
        // Re-measure STROKE_Distance from the main body
        // ================================================================

        private void ReMeasureButton_OnClick(object sender, RoutedEventArgs e)
        {
            RunBusyAsync("Re-measuring STROKE_Distance from the main body…", delegate
            {
                try
                {
                    if (_part == null)
                    {
                        SetStatus("The destination CATPart is not available.", "err");
                        return;
                    }
                    dynamic body = LifterEngine.DetectMainBody(Document);
                    if (body == null || !LifterEngine.MeasureStrokeOnBody(_part, body))
                    {
                        SetStatus("STROKE_Distance could not be re-measured.", "err");
                        return;
                    }
                    LifterEngine.LinkStrokeToMainBody(_part);
                    LoadInstance(_current);
                    SetStatus("STROKE_Distance re-measured from " + LifterEngine.GetName(body) + ".", "ok");
                }
                catch (Exception ex)
                {
                    SetStatus(LifterEngine.FriendlyCatiaError(ex), "err");
                }
            });
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ================================================================
        // Status helpers
        // ================================================================

        private void SetStatus(string text, string tone)
        {
            _statusText.Text = text;
            _statusDot.Fill = tone == "ok" ? LifterUi.Success
                : tone == "err" ? LifterUi.Danger
                : tone == "busy" ? LifterUi.Busy
                : LifterUi.BrushFrom("#94A3B8");
            _connDot.Fill = tone == "busy" ? LifterUi.Busy
                : tone == "err" ? LifterUi.Danger
                : LifterUi.BrushFrom("#22C55E");
            _connText.Text = tone == "busy" ? "Processing"
                : tone == "err" ? "Attention"
                : "Connected";
        }

        private void SetRmInfo(string text, string tone)
        {
            _rmInfoText.Text = text;
            _rmBox.Background = tone == "ok" ? LifterUi.BrushFrom("#F0FDF4")
                : tone == "err" ? LifterUi.BrushFrom("#FEF2F2")
                : LifterUi.BrushFrom("#F8FAFC");
            _rmBox.BorderBrush = tone == "ok" ? LifterUi.BrushFrom("#BBF7D0")
                : tone == "err" ? LifterUi.BrushFrom("#FECACA")
                : LifterUi.CardBorder;
            _rmInfoText.Foreground = tone == "ok" ? LifterUi.BrushFrom("#166534")
                : tone == "err" ? LifterUi.BrushFrom("#991B1B")
                : LifterUi.BrushFrom("#475569");
        }
    }
}
