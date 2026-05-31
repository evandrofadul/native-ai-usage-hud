using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AiUsageBar.App.Themes;
using AiUsageBar.App.ViewModels;

namespace AiUsageBar.App;

/// <summary>Interaction logic for MainWindow.xaml.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // Show/hide animation tuning: the window slides down by this many pixels while
    // fading, easing in on the way out and out on the way in.
    private const double SlideOffset = 24;
    private static readonly Duration ShowDuration = TimeSpan.FromMilliseconds(220);
    private static readonly Duration HideDuration = TimeSpan.FromMilliseconds(170);

    // Bumped on every animated transition so a stale fade-out completion can't tear
    // down (hide/minimize) a window the user has since re-opened.
    private int _animToken;
    private bool _isHiding;

    // Tracks the previous window state so we can tell a restore (Minimized -> Normal)
    // apart from other state changes and animate it in.
    private WindowState _lastState = WindowState.Normal;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        OpacityManager.Changed += OnOpacityChanged;
    }

    /// <summary>
    /// Live-apply a transparency change (e.g. the settings slider): snap the resting
    /// opacity in place. Setting the base value before clearing the hold animation
    /// avoids a flash back to the old held value.
    /// </summary>
    private void OnOpacityChanged()
    {
        if (_isHiding || !IsVisible) return;
        Opacity = OpacityManager.WindowOpacity;
        BeginAnimation(OpacityProperty, null);
    }

    /// <summary>
    /// Strip the maximize box once the native window exists. ResizeMode=CanResize (needed
    /// for edge resizing) re-adds WS_MAXIMIZEBOX, which is what lets Aero Snap maximize the
    /// window when its title bar is dragged to the top — behaviour this popup doesn't want.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GwlStyle);
        SetWindowLong(hwnd, GwlStyle, style & ~WsMaximizeBox);
    }

    /// <summary>True while the window is pinned on top and can't be minimized.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>
    /// Reveal the window sliding down from above its resting spot while fading in.
    /// Cancels any in-flight hide and is a no-op (beyond focusing) if already on screen.
    /// </summary>
    public void ShowAnimated()
    {
        bool wasOffscreen = !IsVisible || _isHiding;

        if (!IsVisible)
        {
            // Start fully transparent so Show() can't flash the window at full opacity.
            Opacity = 0;
            Show();
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        // Already up — just leave it where it is rather than re-sliding.
        if (wasOffscreen) AnimateIn();
    }

    /// <summary>
    /// Conceal the window sliding further down while fading out, then hide it to the tray.
    /// </summary>
    public void HideAnimated() => AnimateOut(Hide);

    /// <summary>
    /// Slide the window down into its resting spot while fading it in. The window is
    /// expected to already be shown/restored; this only plays the reveal.
    /// </summary>
    private void AnimateIn()
    {
        _isHiding = false;
        _animToken++;

        BeginAnimation(TopProperty, null);
        double restingTop = Top;
        Top = restingTop - SlideOffset;
        Opacity = 0;

        var slide = new DoubleAnimation(restingTop - SlideOffset, restingTop, ShowDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        // Fade in to the configured resting opacity (not necessarily fully opaque).
        var fade = new DoubleAnimation(0, OpacityManager.WindowOpacity, ShowDuration);

        BeginAnimation(TopProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Slide the window further down while fading it out, then run <paramref name="onFadedOut"/>
    /// (hide or minimize). Opacity is left held at 0 so the next reveal starts clean — no flash.
    /// </summary>
    private void AnimateOut(Action onFadedOut)
    {
        if (!IsVisible || _isHiding) return;
        _isHiding = true;
        int token = ++_animToken;

        BeginAnimation(TopProperty, null);
        double restingTop = Top;

        var slide = new DoubleAnimation(restingTop, restingTop + SlideOffset, HideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        // Fade out from wherever opacity currently is (resting, or mid-reveal) down to 0.
        var fade = new DoubleAnimation { To = 0, Duration = HideDuration };
        fade.Completed += (_, _) =>
        {
            // A newer transition superseded us — leave the window alone.
            if (token != _animToken) return;

            // Reset the position (opacity stays held at 0 until the next reveal) and
            // commit the hide/minimize.
            BeginAnimation(TopProperty, null);
            Top = restingTop;
            _isHiding = false;
            onFadedOut();
        };

        BeginAnimation(TopProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Closing the window hides it to the tray instead of exiting the app.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            HideAnimated();
        }
        base.OnClosing(e);
    }

    /// <summary>Drag the borderless window from anywhere on its surface.</summary>
    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* mouse already released — ignore */ }
    }

    // Win32 plumbing: hand the drag off to the OS so a borderless, per-pixel
    // transparent window still resizes natively from its edges.
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x00010000;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void OnResizeLeft(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtLeft, e);

    private void OnResizeRight(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtRight, e);

    private void OnResizeTop(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtTop, e);

    private void OnResizeTopLeft(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtTopLeft, e);

    private void OnResizeBottom(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtBottom, e);

    private void OnResizeBottomLeft(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtBottomLeft, e);

    private void OnResizeBottomRight(object sender, MouseButtonEventArgs e) => BeginEdgeResize(HtBottomRight, e);

    /// <summary>Start a native edge/corner resize loop for the pressed grip.</summary>
    private void BeginEdgeResize(int edge, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Any vertical drag means the user is taking over the height — stop letting
        // SizeToContent snap it back so the resize (with no height cap) sticks.
        if (edge is HtTop or HtTopLeft or HtBottom or HtBottomLeft or HtBottomRight)
            SizeToContent = SizeToContent.Manual;

        e.Handled = true; // don't let the press also trigger a window drag
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, (IntPtr)edge, IntPtr.Zero);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (IsPinned) return;
        AnimateOut(() => WindowState = WindowState.Minimized);
    }

    /// <summary>
    /// Pin toggle: keep the window on top, block minimizing, and drop it from the
    /// taskbar while it's on (restoring the taskbar entry when unpinned).
    /// </summary>
    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        IsPinned = PinButton.IsChecked == true;
        Topmost = IsPinned;
        ShowInTaskbar = !IsPinned;
        MinimizeButton.IsEnabled = !IsPinned;

        // If something already minimized us, pull the window back up.
        if (IsPinned && WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }

    /// <summary>
    /// Safety net: a pinned window can never stay minimized. Otherwise, animate the
    /// reveal whenever the window is restored from the taskbar or tray.
    /// </summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // This borderless popup must never maximize (e.g. via Aero Snap when its title
        // bar is dragged to the top of the screen): a maximized window covers the
        // desktop, and the Minimized -> Maximized restore path skips the reveal
        // animation, leaving the window stuck fully transparent. Bounce it back.
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal; // re-enters here as Normal
            return;
        }

        if (IsPinned && WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal; // re-enters here as Normal
            return;
        }

        if (_lastState == WindowState.Minimized && WindowState == WindowState.Normal)
            AnimateIn();

        _lastState = WindowState;
    }

    /// <summary>The close button hides to the tray; quitting is done from the tray menu.</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => HideAnimated();

    /// <summary>Dismiss the settings overlay only when the dimmed backdrop itself is clicked.</summary>
    private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SettingsBackdrop))
            _vm.CloseSettingsCommand.Execute(null);
    }
}