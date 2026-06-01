using System.Runtime.Versioning;
using AiUsageBar.Core.Models;
using AiUsageBar.Core.Pacing;
using AiUsageBar.Presentation.Theming;
using AiUsageBar.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using SD = System.Drawing;
using SDD = System.Drawing.Drawing2D;

namespace AiUsageBar.AvaloniaApp.Tray;

/// <summary>
/// Owns the system-tray icon via Win32 <see cref="NativeTrayIcon"/>. Left click shows
/// the main window; right click opens an Avalonia-styled context menu (vendor switcher +
/// open / refresh / quit); hovering shows a rich card. The glyph is the "ai" badge tinted
/// by the active vendor's usage severity, re-rendered when the data or palette changes.
/// This mirrors the WPF tray (custom styled menu + hover card), which Avalonia's built-in
/// TrayIcon can't provide. The Linux head uses <see cref="LinuxTrayController"/> instead.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTrayController : ITrayController
{
    private const int IconSize = 32;

    private readonly Application _app;
    private readonly MainViewModel _vm;
    private readonly IThemeService _theme;
    private readonly Action _showWindow;
    private readonly Action _quit;

    private readonly NativeTrayIcon _native = new();
    private readonly DispatcherTimer _hoverPoll;

    private Window? _menu;
    private Window? _card;
    private TextBlock? _vendorLabel;

    public WindowsTrayController(Application app, MainViewModel vm, IThemeService theme, Action showWindow, Action quit)
    {
        _app = app;
        _vm = vm;
        _theme = theme;
        _showWindow = showWindow;
        _quit = quit;

        _native.LeftClick += () => Dispatcher.UIThread.Post(() => { HideCard(); _showWindow(); });
        _native.RightClick += (x, y) => Dispatcher.UIThread.Post(() => ShowMenu(x, y));
        _native.MouseMove += () => Dispatcher.UIThread.Post(ShowCard);

        _hoverPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _hoverPoll.Tick += (_, _) => PollHover();

        _vm.TrayChanged += OnTrayChanged;
        _theme.Changed += OnThemeChanged;
    }

    /// <summary>Add the tray icon. Returns false if the shell rejected it (caller should fall back).</summary>
    public bool Start()
    {
        var ok = _native.Show(BuildHIcon(SeverityColorForActive()), BuildTipText());
        return ok;
    }

    private void OnTrayChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Update);
    private void OnThemeChanged() => Dispatcher.UIThread.Post(Update);

    private void Update()
    {
        if (_vm.ActiveVendor is null) return;
        _native.SetIcon(BuildHIcon(SeverityColorForActive()));
        _native.SetTip(BuildTipText());
        if (_card is not null) RefreshCardContent();
        RefreshVendorLabel();
    }

    // ============================ context menu ============================

    private void ShowMenu(int screenX, int screenY)
    {
        HideMenu();
        HideCard();

        var panel = new StackPanel();

        panel.Children.Add(BuildVendorSwitcher());
        panel.Children.Add(Separator());

        panel.Children.Add(MenuItem("Open", Symbol.Open, () => { HideMenu(); _showWindow(); }));
        panel.Children.Add(MenuItem("Refresh all", Symbol.ArrowSync, async () => { HideMenu(); await _vm.RefreshAllAsync(); }));
        panel.Children.Add(Separator());
        panel.Children.Add(MenuItem("Quit", Symbol.Power, () => { HideMenu(); _quit(); }));

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Background = Res("Bg2Brush"),
            BorderBrush = Res("BorderBrush"),
            Child = panel,
        };

        _menu = PopupWindow(card, activated: true);
        _menu.Deactivated += (_, _) => HideMenu();
        ShowAndAnchor(_menu, () => AnchorBottomRight(_menu!, screenX, screenY));
        RefreshVendorLabel();
    }

    private Control BuildVendorSwitcher()
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 2, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var prev = ArrowButton(Symbol.ChevronLeft);
        prev.Click += (_, _) => { _vm.CyclePrev(); RefreshVendorLabel(); };
        Grid.SetColumn(prev, 0);
        grid.Children.Add(prev);

        _vendorLabel = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Foreground = Res("FgBrush"),
        };
        Grid.SetColumn(_vendorLabel, 1);
        grid.Children.Add(_vendorLabel);

        var next = ArrowButton(Symbol.ChevronRight);
        next.Click += (_, _) => { _vm.CycleNext(); RefreshVendorLabel(); };
        Grid.SetColumn(next, 2);
        grid.Children.Add(next);

        return grid;
    }

    private Button ArrowButton(Symbol symbol) => new()
    {
        Theme = Theme("MenuArrowButton"),
        Content = MenuIcon(symbol),
    };

    private Button MenuItem(string header, Symbol symbol, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = MenuIcon(symbol);
        icon.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button { Theme = Theme("TrayMenuItem"), Content = row };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private SymbolIcon MenuIcon(Symbol symbol) => new()
    {
        Symbol = symbol,
        IconVariant = IconVariant.Regular,
        FontSize = 13,
        Foreground = Res("FgBrush"),
    };

    private void RefreshVendorLabel()
    {
        if (_vendorLabel is not null)
            _vendorLabel.Text = _vm.ActiveVendor?.Label() ?? "—";
    }

    private Border Separator() => new()
    {
        Height = 1,
        Margin = new Thickness(4, 3, 4, 3),
        Background = Res("BorderBrush"),
        Opacity = 0.5,
    };

    private void HideMenu()
    {
        _menu?.Close();
        _menu = null;
    }

    // ============================ hover card ============================

    private void ShowCard()
    {
        // Guard against spurious moves: only show while the cursor is over the icon.
        if (_native.TryGetRect(out var rect) && NativeTrayIcon.TryGetCursorPos(out var cx, out var cy)
            && !(cx >= rect.Left && cx < rect.Right && cy >= rect.Top && cy < rect.Bottom))
            return;

        if (_card is null)
        {
            _card = PopupWindow(BuildCard(), activated: false);
            ShowAndAnchor(_card, () => AnchorAboveIcon(_card!));
        }
        _hoverPoll.Start();
    }

    private void PollHover()
    {
        if (_native.TryGetRect(out var rect) && NativeTrayIcon.TryGetCursorPos(out var cx, out var cy))
        {
            if (!(cx >= rect.Left && cx < rect.Right && cy >= rect.Top && cy < rect.Bottom)) HideCard();
        }
        else HideCard();
    }

    private void HideCard()
    {
        _hoverPoll.Stop();
        _card?.Close();
        _card = null;
    }

    private void RefreshCardContent()
    {
        if (_card is not null) _card.Content = BuildCard();
    }

    private Border BuildCard()
    {
        var root = new StackPanel();

        // Header: small accent "ai" badge + title.
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
        var badge = new Border
        {
            Width = 22, Height = 20, CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center, Background = Res("AccentBrush"),
            Child = new TextBlock
            {
                Text = "ai", FontSize = 12, FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("BgBrush"),
            },
        };
        header.Children.Add(badge);
        header.Children.Add(new TextBlock
        {
            Text = "AI Usage", FontSize = 13, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res("FgBrush"),
        });
        root.Children.Add(header);

        if (_vm.Tabs.Count == 0)
        {
            root.Children.Add(new TextBlock { Text = "Loading…", FontSize = 12, Foreground = Res("DimBrush") });
        }
        else
        {
            foreach (var tab in _vm.Tabs)
                root.Children.Add(BuildCardRow(tab));
        }

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 11, 13, 11),
            MinWidth = 212,
            Background = Res("Bg2Brush"),
            BorderBrush = Res("BorderBrush"),
            BoxShadow = BoxShadows.Parse("0 1 5 0 #1A000000"),
            Child = root,
        };
    }

    private Control BuildCardRow(VendorTabViewModel tab)
    {
        var hasData = tab.TrayLine is not null;
        var sevBrush = hasData ? new ImmutableSolidColorBrush(SeverityColor(tab)) : Res("DimBrush");

        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Ellipse
        {
            Width = 8, Height = 8, Fill = sevBrush,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        left.Children.Add(new TextBlock { Text = tab.Vendor.Label(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("FgBrush") });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var value = new TextBlock
        {
            Text = tab.TrayLine ?? "—", FontSize = 12, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = sevBrush,
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        return grid;
    }

    // ============================ window plumbing ============================

    private static Window PopupWindow(Control content, bool activated) => new()
    {
        SystemDecorations = SystemDecorations.None,
        Background = Brushes.Transparent,
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
        ShowInTaskbar = false,
        Topmost = true,
        CanResize = false,
        SizeToContent = SizeToContent.WidthAndHeight,
        ShowActivated = activated,
        Content = content,
    };

    /// <summary>Show then position once layout has produced a size.</summary>
    private static void ShowAndAnchor(Window w, Action anchor)
    {
        w.Opened += (_, _) => Dispatcher.UIThread.Post(anchor, DispatcherPriority.Loaded);
        w.Show();
    }

    /// <summary>Place the window so its bottom-right sits at the screen point (context menu).</summary>
    private static void AnchorBottomRight(Window w, int screenX, int screenY)
    {
        var scale = w.RenderScaling;
        var pxW = (int)(w.Bounds.Width * scale);
        var pxH = (int)(w.Bounds.Height * scale);
        w.Position = new PixelPoint(Math.Max(4, screenX - pxW), Math.Max(4, screenY - pxH));
    }

    /// <summary>Center the window above the tray icon (card), falling back to the cursor.</summary>
    private void AnchorAboveIcon(Window w)
    {
        var scale = w.RenderScaling;
        var pxW = (int)(w.Bounds.Width * scale);
        var pxH = (int)(w.Bounds.Height * scale);

        int left, top;
        if (_native.TryGetRect(out var r))
        {
            var centerX = (r.Left + r.Right) / 2;
            left = centerX - pxW / 2;
            top = r.Top - pxH - 2;
        }
        else
        {
            NativeTrayIcon.TryGetCursorPos(out var cx, out var cy);
            left = cx - pxW - 6;
            top = cy - pxH - 6;
        }
        w.Position = new PixelPoint(Math.Max(4, left), Math.Max(4, top));
    }

    // ============================ icon + helpers ============================

    /// <summary>
    /// Renders the "ai" badge (rounded square tinted by <paramref name="bg"/>, "ai" in the
    /// window background color) to a 32×32 HICON via GDI+ (grayscale AA, so no subpixel
    /// color fringing), centered with typographic metrics — mirroring the WPF tray glyph.
    /// </summary>
    private IntPtr BuildHIcon(Color bg)
    {
        var fg = ToGdi(ResColor("BgBrush", Color.Parse("#282C34")));

        using var bmp = new SD.Bitmap(IconSize, IconSize, SD.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SDD.SmoothingMode.AntiAlias;
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
            using (var path = Rounded(new SD.RectangleF(-1, -1, 33, 33), 6))
            using (var brush = new SD.SolidBrush(ToGdi(bg)))
                g.FillPath(brush, path);

            // Segoe UI Bold em 19 ≈ the WPF glyph (FormattedText em 18); GenericTypographic
            // gives tight metrics so the "ai" centers without GDI's default padding.
            using var font = new SD.Font("Segoe UI", 23f, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var fgBrush = new SD.SolidBrush(fg);
            var fmt = SD.StringFormat.GenericTypographic;
            var size = g.MeasureString("ai", font, SD.PointF.Empty, fmt);
            var ox = (32 - size.Width) / 2f;
            var oy = (32 - size.Height) / 2f;
            g.DrawString("ai", font, fgBrush, new SD.PointF(ox, oy), fmt);
        }

        return bmp.GetHicon();
    }

    private static SDD.GraphicsPath Rounded(SD.RectangleF r, float radius)
    {
        var p = new SDD.GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static SD.Color ToGdi(Color c) => SD.Color.FromArgb(c.A, c.R, c.G, c.B);

    private string BuildTipText()
    {
        if (_vm.Tabs.Count == 0) return "AI Usage — loading…";
        return "AI Usage\n" + string.Join("\n", _vm.Tabs.Select(t => $"{t.Vendor.Label()}: {t.TrayLine ?? "—"}"));
    }

    private Color SeverityColorForActive()
    {
        var vendor = _vm.ActiveVendor;
        var tab = vendor is { } v ? _vm.Tabs.FirstOrDefault(t => t.Vendor == v) : null;
        return tab is not null ? SeverityColor(tab) : ResColor("AccentBrush", Color.Parse("#61AFEF"));
    }

    private Color SeverityColor(VendorTabViewModel tab)
    {
        var sev = tab.Sections.OfType<MetricSectionVm>().Select(m => (PaceSeverity?)m.Severity).FirstOrDefault();
        return sev switch
        {
            PaceSeverity.Low => ResColor("SevLowBrush", Color.Parse("#98C379")),
            PaceSeverity.Mid => ResColor("SevMidBrush", Color.Parse("#E5C07B")),
            PaceSeverity.High => ResColor("SevHighBrush", Color.Parse("#D19A66")),
            PaceSeverity.Critical => ResColor("SevCriticalBrush", Color.Parse("#E06C75")),
            _ => ResColor("AccentBrush", Color.Parse("#61AFEF")),
        };
    }

    private IBrush Res(string key) =>
        _app.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Transparent;

    private Color ResColor(string key, Color fallback) =>
        _app.TryGetResource(key, null, out var v) && v is ISolidColorBrush b ? b.Color : fallback;

    private ControlTheme? Theme(string key) =>
        _app.TryGetResource(key, null, out var v) ? v as ControlTheme : null;

    public void Dispose()
    {
        _vm.TrayChanged -= OnTrayChanged;
        _theme.Changed -= OnThemeChanged;
        _hoverPoll.Stop();
        HideCard();
        HideMenu();
        _native.Dispose();
    }
}
