using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AiUsageBar.App.Converters;
using AiUsageBar.Core.Models;
using AiUsageBar.Presentation.Theming;
using AiUsageBar.Presentation.ViewModels;
using FluentIcons.Common;
using FluentIcons.Wpf;
using H.NotifyIcon;

namespace AiUsageBar.App.Tray;

/// <summary>
/// Owns the system-tray icon. The tray is the entry point to the (single) main
/// window — left click shows it; the context menu offers refresh / vendor switch /
/// settings / quit. This is the Windows replacement for the Waybar module.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MainViewModel _vm;
    private readonly Action _showWindow;
    private readonly Action _quit;

    /// <summary>The icon currently shown, kept so the previous GDI handle can be freed on the next update.</summary>
    private System.Drawing.Icon? _currentIcon;

    /// <summary>Reused to map a severity to the active theme's gauge brush.</summary>
    private readonly SeverityToBrushConverter _severity = new();

    /// <summary>The vendor-switcher label in the tray menu; updated as the active vendor changes.</summary>
    private TextBlock? _vendorLabel;

    /// <summary>Our own tooltip popup (the library's NIN_POPUPOPEN path is unreliable on Win10/11).</summary>
    private readonly Popup _toolTipPopup;

    /// <summary>Polls (while shown) whether the cursor is still over the tray icon.</summary>
    private readonly DispatcherTimer _toolTipTimer;

    /// <summary>Last time the tray reported a mouse move — fallback when the icon rect is unavailable.</summary>
    private DateTime _lastMove;

    public TrayIconService(MainViewModel vm, Action showWindow, Action quit)
    {
        _vm = vm;
        _showWindow = showWindow;
        _quit = quit;

        _icon = new TaskbarIcon
        {
            ContextMenu = BuildMenu(),
        };
        SetIcon((Brush)Application.Current.Resources["AccentBrush"]);

        // We drive the tooltip ourselves: a Popup shown on hover (TrayMouseMove),
        // positioned at the cursor. This avoids the library's szTip/NIN_POPUPOPEN
        // path, which on Win10/11 just shows the native text and not the custom UI.
        _toolTipPopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.AbsolutePoint,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        _toolTipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _toolTipTimer.Tick += (_, _) => PollToolTip();

        _icon.TrayMouseMove += (_, _) => { _lastMove = DateTime.UtcNow; ShowToolTip(); };
        _icon.TrayLeftMouseUp += (_, _) => { HideToolTip(); _showWindow(); };
        _icon.ForceCreate();

        _vm.TrayChanged += (_, _) => Update();

        // A detached ContextMenu doesn't get the application-level DynamicResource
        // invalidation when the palette is swapped, so rebuild it (fresh resource
        // resolution against the new palette) whenever the theme changes.
        Themes.ThemeManager.Changed += OnThemeChanged;

        // Follow the window-transparency preference (when it opts to affect the tray).
        OpacityManager.Changed += OnOpacityChanged;

        Update();
    }

    /// <summary>Re-apply the tray opacity to the (possibly open) menu and tooltip card.</summary>
    private void OnOpacityChanged()
    {
        var opacity = OpacityManager.TrayOpacity;
        if (_icon.ContextMenu is { } menu) menu.Opacity = opacity;
        if (_toolTipPopup.Child is UIElement card) card.Opacity = opacity;
    }

    private void OnThemeChanged()
    {
        _icon.ContextMenu = BuildMenu();
        Update(); // re-render the tray glyph against the new palette
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Opacity = OpacityManager.TrayOpacity };

        // The tray ContextMenu is a free-floating element (anchored to the
        // TaskbarIcon, not to a window's visual tree). In that case the implicit
        // Style's DynamicResource setters do NOT get the change notification when
        // ThemeManager.Apply swaps the palette, so the menu kept its old colors
        // until the app restarted. Binding the themeable brushes directly on the
        // element (as the menu glyphs already do) makes them recolor live.
        menu.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Bg2Brush");
        menu.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "FgBrush");
        menu.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");

        // Vendor switcher: [◀] active vendor [▶], set off with separators above and
        // below. The arrows keep the menu open so the user can step through vendors
        // and watch the label update in place.
        menu.Items.Add(BuildVendorSwitcher());
        menu.Items.Add(ThemedSeparator());

        var open = ThemedItem("Open", Symbol.Open);
        open.Click += (_, _) => _showWindow();
        menu.Items.Add(open);

        var refresh = ThemedItem("Refresh all", Symbol.ArrowSync);
        refresh.Click += async (_, _) => await _vm.RefreshAllAsync();
        menu.Items.Add(refresh);
        menu.Items.Add(ThemedSeparator());

        var quit = ThemedItem("Quit", Symbol.Power);
        quit.Click += (_, _) => _quit();
        menu.Items.Add(quit);

        // Re-sync the label each time the menu opens (the selection may have changed
        // from the window or a previous switch).
        menu.Opened += (_, _) => RefreshVendorLabel();

        return menu;
    }

    /// <summary>
    /// Builds the tray menu's vendor switcher row: a left/right arrow flanking the
    /// active vendor's name. Clicking an arrow cycles the selection and refreshes the
    /// label without dismissing the menu (the click is marked handled).
    /// </summary>
    private FrameworkElement BuildVendorSwitcher()
    {
        var grid = new Grid() { Margin = new Thickness(-28, 0, -25, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prev = ArrowButton(Symbol.ChevronLeft, "Previous vendor");
        prev.Click += (_, e) => { _vm.CyclePrev(); RefreshVendorLabel(); e.Handled = true; };
        Grid.SetColumn(prev, 0);
        grid.Children.Add(prev);

        _vendorLabel = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        _vendorLabel.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        Grid.SetColumn(_vendorLabel, 1);
        grid.Children.Add(_vendorLabel);

        var next = ArrowButton(Symbol.ChevronRight, "Next vendor");
        next.Click += (_, e) => { _vm.CycleNext(); RefreshVendorLabel(); e.Handled = true; };
        Grid.SetColumn(next, 2);
        grid.Children.Add(next);

        RefreshVendorLabel();
        return grid;
    }

    /// <summary>A compact, rounded arrow button sized to match the text menu items.</summary>
    private Button ArrowButton(Symbol symbol, string tip) => new()
    {
        Style = (Style)Application.Current.Resources["MenuArrowButton"],
        Content = MenuIcon(symbol),
        ToolTip = tip,
    };

    private void RefreshVendorLabel()
    {
        if (_vendorLabel is not null)
            _vendorLabel.Text = _vm.ActiveVendor?.Label() ?? "—";
    }

    /// <summary>The standard menu separator, styled thin and palette-aware (see MenuSeparator).</summary>
    private static Separator ThemedSeparator() => new()
    {
        Style = (Style)Application.Current.Resources["MenuSeparator"],
    };

    /// <summary>
    /// Creates a themed menu item, binding its foreground directly to the active
    /// palette so it recolors live (see <see cref="BuildMenu"/> for why the implicit
    /// Style's DynamicResource doesn't suffice for the detached tray menu).
    /// </summary>
    private MenuItem ThemedItem(string header, Symbol symbol)
    {
        var item = new MenuItem { Header = header, Icon = MenuIcon(symbol) };
        item.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "FgBrush");
        return item;
    }

    private static SymbolIcon MenuIcon(Symbol symbol)
    {
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            IconVariant = IconVariant.Regular,
            FontSize = 13,
        };
        // Bind to the theme brush rather than snapshotting it, so the menu glyphs
        // recolor live when the palette is swapped (ThemeManager.Apply).
        icon.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "FgBrush");
        return icon;
    }

    private const int IconSize = 32;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Builds the tray glyph for the given background and applies it, freeing the
    /// previously shown icon's GDI handle.
    /// </summary>
    private void SetIcon(Brush background)
    {
        var icon = BuildTrayIcon(background);
        _icon.Icon = icon;
        _currentIcon?.Dispose();
        _currentIcon = icon;
    }

    /// <summary>
    /// Renders the tray glyph as a copy of the "ai" badge from the main window
    /// header (<c>MainWindow.xaml</c>): a rounded square — here tinted by usage
    /// severity instead of the header's fixed accent — with the "ai" lettering in
    /// the window background colour. The badge is authored as a vector
    /// <see cref="DrawingImage"/> (brushes pulled from the app resources, so it
    /// matches the header) and then rasterised into a Win32 <see cref="System.Drawing.Icon"/>.
    /// H.NotifyIcon's <c>IconSource</c> pipeline can't consume an in-memory
    /// <see cref="ImageSource"/> (it expects a <c>BitmapImage</c> with a file
    /// <c>UriSource</c>), so we hand it a ready-made icon directly — the same
    /// approach WPF-UI's TrayIcon takes.
    /// </summary>
    private static System.Drawing.Icon BuildTrayIcon(Brush background)
    {
        var fg = (Brush)Application.Current.Resources["BgBrush"];

        // Author the "ai" badge as a vector drawing, centred in a 32x32 canvas.
        var group = new DrawingGroup();
        var badge = new RectangleGeometry(new Rect(2, 3, 28, 26), 4, 4);
        group.Children.Add(new GeometryDrawing(background, null, badge));

        var typeface = new Typeface(new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText("ai", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 18, fg, 1.0);
        var origin = new Point(2 + (28 - text.Width) / 2, 3 + (26 - text.Height) / 2);
        group.Children.Add(new GeometryDrawing(fg, null, text.BuildGeometry(origin)));

        var drawing = new DrawingImage(group);
        drawing.Freeze();

        // Rasterise the vector badge to a bitmap...
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(drawing, new Rect(0, 0, IconSize, IconSize));

        var rtb = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        // ...then convert it into a Win32 icon. GetHicon() hands back an unmanaged
        // HICON we must own, so clone into a managed Icon and free the handle.
        using var bitmap = new System.Drawing.Bitmap(ms);
        var hicon = bitmap.GetHicon();
        try
        {
            using var unmanaged = System.Drawing.Icon.FromHandle(hicon);
            return (System.Drawing.Icon)unmanaged.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    private void Update()
    {
        var vendor = _vm.ActiveVendor;
        if (vendor is null) return;

        SetIcon(SeverityBrushForActive(vendor.Value));

        // If the tooltip is currently visible, refresh it with the new data.
        if (_toolTipPopup.IsOpen)
            _toolTipPopup.Child = BuildToolTip();
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    /// <summary>Show the tooltip card centered just above the tray icon — only while the cursor is over it.</summary>
    private void ShowToolTip()
    {
        // Guard against spurious moves (and the icon appearing under the cursor at
        // startup): if we can locate the icon and the cursor isn't on it, do nothing.
        var haveRect = TryGetTrayRect(out var rect);
        if (haveRect && !CursorInside(rect)) return;

        if (!_toolTipPopup.IsOpen)
        {
            var card = BuildToolTip();
            _toolTipPopup.Child = card;

            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = card.DesiredSize;

            var scale = 96.0 / GetDpiForSystem(); // physical px -> DIPs
            double left, top;
            if (haveRect)
            {
                // Center the card above the icon (the tray sits at the bottom-right,
                // so growing upward keeps it on screen).
                var centerX = (rect.Left + rect.Right) / 2.0 * scale;
                left = centerX - size.Width / 2.0;
                top = rect.Top * scale - size.Height - 2;
            }
            else
            {
                // Icon rect unavailable (e.g. overflow flyout) — fall back to the cursor.
                GetCursorPos(out var p);
                left = p.X * scale - size.Width - 6;
                top = p.Y * scale - size.Height - 6;
            }
            _toolTipPopup.HorizontalOffset = Math.Max(4, left);
            _toolTipPopup.VerticalOffset = Math.Max(4, top);

            _toolTipPopup.IsOpen = true;
        }

        _toolTipTimer.Start();
    }

    /// <summary>While shown, hide as soon as the cursor leaves the tray icon.</summary>
    private void PollToolTip()
    {
        if (TryGetTrayRect(out var rect))
        {
            if (!CursorInside(rect)) HideToolTip();
        }
        else if (DateTime.UtcNow - _lastMove > TimeSpan.FromSeconds(1))
        {
            // Couldn't resolve the icon rect (e.g. it's in the overflow flyout) —
            // fall back to hiding shortly after the last reported mouse move.
            HideToolTip();
        }
    }

    private void HideToolTip()
    {
        _toolTipTimer.Stop();
        _toolTipPopup.IsOpen = false;
    }

    private bool TryGetTrayRect(out RECT rect)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            guidItem = _icon.Id,
        };
        return Shell_NotifyIconGetRect(ref id, out rect) == 0; // S_OK
    }

    private static bool CursorInside(RECT r)
    {
        GetCursorPos(out var p);
        return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>
    /// A themed tooltip card: an "ai" badge + title, then one row per vendor showing
    /// a severity dot, the vendor name, and its short usage line. Themeable brushes
    /// use <see cref="FrameworkElement.SetResourceReference"/> so the card recolors
    /// when the palette is swapped; severity colors come from the data.
    /// </summary>
    private UIElement BuildToolTip()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 11, 13, 11),
            MinWidth = 212,
            SnapsToDevicePixels = true,
            Opacity = OpacityManager.TrayOpacity,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Bg2Brush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var root = new StackPanel();
        card.Child = root;

        // Header: small accent "ai" badge + title.
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
        var badge = new Border
        {
            Width = 22, Height = 20, CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var badgeText = new TextBlock
        {
            Text = "ai", FontSize = 12, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        badge.Child = badgeText;
        header.Children.Add(badge);

        var title = new TextBlock
        {
            Text = "AI Usage", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        header.Children.Add(title);
        root.Children.Add(header);

        if (_vm.Tabs.Count == 0)
        {
            root.Children.Add(DimText("Loading…"));
            return card;
        }

        foreach (var tab in _vm.Tabs)
            root.Children.Add(BuildToolTipRow(tab));

        return card;
    }

    private FrameworkElement BuildToolTipRow(VendorTabViewModel tab)
    {
        var hasData = tab.TrayLine is not null;
        var sevBrush = hasData
            ? SeverityBrushForActive(tab.Vendor)
            : (Brush)Application.Current.Resources["DimBrush"];

        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Ellipse
        {
            Width = 8, Height = 8, Fill = sevBrush,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock { Text = tab.Vendor.Label(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        left.Children.Add(name);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var value = new TextBlock
        {
            Text = tab.TrayLine ?? "—", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = sevBrush,
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        return grid;
    }

    private TextBlock DimText(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        return t;
    }

    private Brush SeverityBrushForActive(VendorId vendor)
    {
        var tab = _vm.Tabs.FirstOrDefault(t => t.Vendor == vendor);
        var sev = tab?.Sections
            .OfType<MetricSectionVm>()
            .Select(m => (Core.Pacing.PaceSeverity?)m.Severity)
            .FirstOrDefault();
        return sev is { } s
            ? (Brush)_severity.Convert(s, typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture)
            : (Brush)Application.Current.Resources["AccentBrush"];
    }

    public void Dispose()
    {
        Themes.ThemeManager.Changed -= OnThemeChanged;
        OpacityManager.Changed -= OnOpacityChanged;
        _toolTipTimer.Stop();
        _toolTipPopup.IsOpen = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
