namespace AiUsageBar.App.Themes;

/// <summary>
/// Holds the user's window-transparency preference at runtime and broadcasts changes,
/// mirroring <see cref="ThemeManager"/>. The main window uses <see cref="WindowOpacity"/>
/// as its resting opacity (the show/hide fade animates to it), and the tray service
/// dims its custom popup/menu to match when <see cref="AffectsTray"/> is on.
/// </summary>
public static class OpacityManager
{
    /// <summary>Fully opaque — the default when nothing is configured.</summary>
    public const int DefaultPercent = 100;

    /// <summary>Lower bound; below this the window would be hard to find/click.</summary>
    public const int MinPercent = 10;

    /// <summary>Window opacity as a percentage, 10–100.</summary>
    public static int Percent { get; private set; } = DefaultPercent;

    /// <summary>Whether the transparency also dims the tray's custom popup and menu.</summary>
    public static bool AffectsTray { get; private set; }

    /// <summary><see cref="Percent"/> as a 0.1–1.0 opacity factor.</summary>
    public static double WindowOpacity => Percent / 100.0;

    /// <summary>Opacity to use for the tray popup/menu: the window's if it opts in, else opaque.</summary>
    public static double TrayOpacity => AffectsTray ? WindowOpacity : 1.0;

    /// <summary>Raised after a change so live elements (window, tray) can re-apply.</summary>
    public static event Action? Changed;

    /// <summary>Apply the preference, clamping the percentage into the valid range.</summary>
    public static void Apply(int percent, bool affectsTray)
    {
        Percent = Math.Clamp(percent, MinPercent, DefaultPercent);
        AffectsTray = affectsTray;
        Changed?.Invoke();
    }
}
