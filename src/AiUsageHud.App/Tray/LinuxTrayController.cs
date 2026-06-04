using AiUsageHud.Core.Models;
using AiUsageHud.Core.Pacing;
using AiUsageHud.Presentation.Theming;
using AiUsageHud.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AiUsageHud.App.Tray;

/// <summary>
/// Cross-platform tray for the Linux head, built on Avalonia's <see cref="TrayIcon"/> and a
/// <see cref="NativeMenu"/>. Unlike <see cref="WindowsTrayController"/> this cannot draw a
/// hover card or a hand-styled menu: the StatusNotifierItem model (DBus) used by GNOME/KDE/
/// waybar gives no per-icon hover or move events and lets the desktop shell own the menu
/// rendering. So the menu is the desktop-native one (Open / Refresh all / vendor radios /
/// Quit) and left-click activation opens the window where the host forwards it. The glyph is
/// the same "ai" badge tinted by severity, rendered via <see cref="TrayGlyphRenderer"/>.
/// </summary>
internal sealed class LinuxTrayController : ITrayController
{
    private readonly Application _app;
    private readonly MainViewModel _vm;
    private readonly IThemeService _theme;
    private readonly Action _showWindow;
    private readonly Action _quit;

    private TrayIcon? _icon;

    public LinuxTrayController(Application app, MainViewModel vm, IThemeService theme, Action showWindow, Action quit)
    {
        _app = app;
        _vm = vm;
        _theme = theme;
        _showWindow = showWindow;
        _quit = quit;

        _vm.TrayChanged += OnTrayChanged;
        _theme.Changed += OnThemeChanged;
    }

    public bool Start()
    {
        try
        {
            _icon = new TrayIcon
            {
                Icon = BuildIcon(),
                ToolTipText = BuildTipText(),
                Menu = BuildMenu(),
                IsVisible = true,
            };
            _icon.Clicked += (_, _) => Dispatcher.UIThread.Post(_showWindow);
            TrayIcon.SetIcons(_app, new TrayIcons { _icon });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnTrayChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Update);
    private void OnThemeChanged() => Dispatcher.UIThread.Post(Update);

    private void Update()
    {
        if (_icon is null) return;
        _icon.Icon = BuildIcon();
        _icon.ToolTipText = BuildTipText();
        _icon.Menu = BuildMenu();
    }

    // ============================ menu ============================

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        menu.Add(Item("Open", () => _showWindow()));
        menu.Add(AsyncItem("Refresh all", () => _vm.RefreshAllAsync()));
        menu.Add(new NativeMenuItemSeparator());

        for (var i = 0; i < _vm.Tabs.Count; i++)
        {
            var index = i;
            var item = new NativeMenuItem(_vm.Tabs[i].Vendor.Label())
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = i == _vm.SelectedIndex,
            };
            item.Click += (_, _) => _vm.SelectedIndex = index;
            menu.Add(item);
        }

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit", () => _quit()));
        return menu;
    }

    private static NativeMenuItem Item(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static NativeMenuItem AsyncItem(string header, Func<Task> onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += async (_, _) => await onClick();
        return item;
    }

    // ============================ icon + tooltip ============================

    private WindowIcon BuildIcon()
    {
        var severity = SeverityColorForActive();
        var fg = ResColor("BgBrush", Color.Parse("#282C34"));
        return TrayGlyphRenderer.Render(severity, fg);
    }

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

    private Color ResColor(string key, Color fallback) =>
        _app.TryGetResource(key, null, out var v) && v is ISolidColorBrush b ? b.Color : fallback;

    public void Dispose()
    {
        _vm.TrayChanged -= OnTrayChanged;
        _theme.Changed -= OnThemeChanged;
        if (_icon is not null)
        {
            _icon.IsVisible = false;
            TrayIcon.SetIcons(_app, new TrayIcons());
            _icon.Dispose();
            _icon = null;
        }
    }
}
