namespace AiUsageBar.Core.Pacing;

/// <summary>Four severity tiers driving bar/text colors. Port of Rust <c>PaceSeverity</c>.</summary>
public enum PaceSeverity
{
    Low,
    Mid,
    High,
    Critical,
}

public static class SeverityRules
{
    /// <summary>
    /// Map a usage percentage to a severity tier (claudebar's <c>color_for</c>):
    /// &gt;=90 critical, &gt;=75 high, &gt;=50 mid, else low.
    /// </summary>
    public static PaceSeverity SeverityFor(int pct)
    {
        if (pct >= 90) return PaceSeverity.Critical;
        if (pct >= 75) return PaceSeverity.High;
        if (pct >= 50) return PaceSeverity.Mid;
        return PaceSeverity.Low;
    }

    /// <summary>
    /// Severity keyed on signed point delta (claudebar's <c>pace_color_for</c>):
    /// delta&gt;=10 critical, &gt;0 high, &gt;=-10 mid, else low.
    /// </summary>
    public static PaceSeverity PaceSeverityFor(int delta)
    {
        if (delta >= 10) return PaceSeverity.Critical;
        if (delta > 0) return PaceSeverity.High;
        if (delta >= -10) return PaceSeverity.Mid;
        return PaceSeverity.Low;
    }
}
