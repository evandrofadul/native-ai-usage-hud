namespace AiUsageBar.Core.Pacing;

/// <summary>Three visual pace states. <see cref="Glyph"/> matches claudebar's arrows.</summary>
public enum Pace
{
    Ahead,
    OnTrack,
    Under,
}

public static class PaceExtensions
{
    public static string Glyph(this Pace pace) => pace switch
    {
        Pace.Ahead => "↑",
        Pace.OnTrack => "→",
        Pace.Under => "↓",
        _ => "→",
    };
}

/// <summary>
/// Result of pacing math. Field naming mirrors the original placeholders.
/// </summary>
public readonly record struct PacingResult(
    int ElapsedPct,
    Pace RatioPace,
    Pace PointPace,
    int Delta,
    string RatioLabel,
    string PointLabel)
{
    /// <summary>Neutral pacing for windows with no reset time.</summary>
    public static PacingResult Neutral() => new(
        ElapsedPct: 0,
        RatioPace: Pace.OnTrack,
        PointPace: Pace.OnTrack,
        Delta: 0,
        RatioLabel: "on track",
        PointLabel: "on track");
}

/// <summary>
/// Pacing math — port of claudebar's <c>calc_pacing</c>. Two parallel notions:
/// ratio (actual/elapsed with a tolerance band, capped at 999%) and signed point
/// delta (actual - elapsed). Integer arithmetic is preserved to match the original.
/// </summary>
public static class PacingMath
{
    public const int DefaultTolerance = 5;

    public static PacingResult Calc(
        int usagePct,
        DateTimeOffset? reset,
        DateTimeOffset now,
        TimeSpan window,
        int tolerance)
    {
        if (reset is null) return PacingResult.Neutral();

        var total = (long)window.TotalSeconds;
        if (total <= 0) return PacingResult.Neutral();

        var remaining = (long)(reset.Value - now).TotalSeconds;
        var elapsedPct = (int)((total - remaining) * 100 / total);
        elapsedPct = Math.Clamp(elapsedPct, 0, 100);

        // Point-based delta and label.
        var delta = usagePct - elapsedPct;
        Pace pointPace;
        string pointLabel;
        if (delta > 0)
        {
            pointPace = Pace.Ahead;
            pointLabel = $"{delta}pts ahead";
        }
        else if (delta < 0)
        {
            pointPace = Pace.Under;
            pointLabel = $"{-delta}pts under";
        }
        else
        {
            pointPace = Pace.OnTrack;
            pointLabel = "on track";
        }

        // Ratio-based icon and label (only meaningful once any time has elapsed).
        Pace ratioPace;
        string ratioLabel;
        if (elapsedPct > 0)
        {
            var pacingX100 = usagePct * 100 / elapsedPct;
            if (pacingX100 > 100 + tolerance)
            {
                var dev = Math.Min(pacingX100 - 100, 999);
                ratioPace = Pace.Ahead;
                ratioLabel = $"{dev}% ahead";
            }
            else if (pacingX100 < 100 - tolerance)
            {
                var dev = Math.Min(100 - pacingX100, 999);
                ratioPace = Pace.Under;
                ratioLabel = $"{dev}% under";
            }
            else
            {
                ratioPace = Pace.OnTrack;
                ratioLabel = "on track";
            }
        }
        else
        {
            ratioPace = Pace.OnTrack;
            ratioLabel = "on track";
        }

        return new PacingResult(elapsedPct, ratioPace, pointPace, delta, ratioLabel, pointLabel);
    }
}
