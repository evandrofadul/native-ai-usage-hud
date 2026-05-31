namespace AiUsageBar.Core.Pacing;

/// <summary>
/// Human-readable countdown between two instants. Port of claudebar's
/// <c>countdown()</c>:
/// missing reset → "—", past → "now", ≥1 day → "{d}d {h}h", else "{h}h {mm}m".
/// </summary>
public static class Countdown
{
    public static string Format(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "—";

        var secs = (long)(reset.Value - now).TotalSeconds;
        if (secs <= 0) return "now";

        var days = secs / 86_400;
        var hours = secs % 86_400 / 3_600;
        var mins = secs % 3_600 / 60;

        return days > 0 ? $"{days}d {hours}h" : $"{hours}h {mins:D2}m";
    }
}
