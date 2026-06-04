using AiUsageBar.Presentation.Theming;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AiUsageBar.AvaloniaApp;

/// <summary>
/// Borderless popup window. Port of the WPF MainWindow: drag from anywhere on the
/// header, edge/corner resize, pin-on-top, and close-to-tray. Show/hide is instant
/// (no transition).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>True while the window is pinned on top and can't be minimized.</summary>
    public bool IsPinned { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        // Keep-on-top relies on the EWMH _NET_WM_STATE_ABOVE hint (what Avalonia's X11
        // Topmost maps to). Wayland has no portable always-on-top, and compositors like
        // COSMIC don't advertise _NET_WM_STATE_ABOVE under XWayland, so the hint is silently
        // dropped and the pin does nothing. Hide the button on Linux rather than offer a
        // control that can't work; Windows/GNOME/KDE keep it.
        if (OperatingSystem.IsLinux())
        {
            PinButton.IsVisible = false;

            // Swap the square Windows caption buttons for flat circular GNOME/libadwaita
            // ones so the close/minimize controls follow the Linux desktop convention.
            foreach (var btn in WindowControls.Children.OfType<Button>())
                btn.Classes.Add("gnome");
        }

        Opacity = OpacityManager.WindowOpacity;
        OpacityManager.Changed += OnOpacityChanged;
    }

    /// <summary>Live-apply the window transparency preference (the resting opacity).</summary>
    private void OnOpacityChanged()
    {
        if (IsVisible) Opacity = OpacityManager.WindowOpacity;
    }

    /// <summary>Show/focus the window (from the tray).</summary>
    public void Reveal()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Opacity = OpacityManager.WindowOpacity;
        Activate();
        Topmost = true;
        Topmost = IsPinned;
    }

    /// <summary>Hide the window to the tray.</summary>
    public void HideToTray() => Hide();

    /// <summary>
    /// Drag the borderless window from the header — but not when the press lands on one
    /// of the header buttons (settings / pin / minimize / close), so they still get their
    /// click instead of being swallowed by the move-drag loop. (ToggleButton derives from
    /// Button, so the pin toggle is covered too.)
    /// </summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        BeginMoveDrag(e);
    }

    private void OnResizeLeft(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.West, e);
    private void OnResizeRight(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.East, e);
    private void OnResizeTop(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.North, e);
    private void OnResizeTopLeft(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.NorthWest, e);
    private void OnResizeBottom(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.South, e);
    private void OnResizeBottomLeft(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.SouthWest, e);
    private void OnResizeBottomRight(object? sender, PointerPressedEventArgs e) => BeginEdgeResize(WindowEdge.SouthEast, e);

    private void BeginEdgeResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Any vertical drag means the user is taking over the height — stop letting
        // SizeToContent snap it back so the resize sticks.
        if (edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.South
            or WindowEdge.SouthWest or WindowEdge.SouthEast)
            SizeToContent = SizeToContent.Manual;

        e.Handled = true;
        BeginResizeDrag(edge, e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (IsPinned) return;
        WindowState = WindowState.Minimized;
    }

    /// <summary>The close button hides to the tray; quitting is done from the tray menu.</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => HideToTray();

    /// <summary>
    /// Pin toggle: keep the window on top, block minimizing, and drop it from the
    /// taskbar while it's on (restoring the taskbar entry when unpinned).
    /// </summary>
    private void OnPinChanged(object? sender, RoutedEventArgs e)
    {
        IsPinned = PinButton.IsChecked == true;
        Topmost = IsPinned;
        ShowInTaskbar = !IsPinned;
        MinimizeButton.IsEnabled = !IsPinned;

        if (IsPinned && WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }

    /// <summary>Dismiss the settings overlay only when the dimmed backdrop itself is clicked.</summary>
    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, SettingsBackdrop) && DataContext is Presentation.ViewModels.MainViewModel vm)
            vm.CloseSettingsCommand.Execute(null);
    }

    /// <summary>Closing the window hides it to the tray instead of exiting the app.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            HideToTray();
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// This borderless popup must never maximize, and a pinned window can never stay
    /// minimized.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty) return;

        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else if (IsPinned && WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }
}
