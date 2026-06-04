namespace AiUsageHud.Core.Models;

/// <summary>
/// Money in integer cents to avoid float roundoff. Port of the Rust <c>Cents</c>.
/// </summary>
public readonly record struct Cents(long Value)
{
    /// <summary>
    /// Format as <c>[-]$D.CC</c>. Negative values render <c>-$D.CC</c> (sign before
    /// the dollar sign), matching claudebar's <c>_fmt_dollars</c>.
    /// </summary>
    public string FormatDollars()
    {
        var (sign, abs) = Value < 0 ? ("-", -Value) : ("", Value);
        return $"{sign}${abs / 100}.{abs % 100:D2}";
    }
}

/// <summary>"Extra usage" pay-as-you-go block (claudebar's <c>extra_usage</c>).</summary>
public readonly record struct ExtraUsage(Cents Limit, Cents Spent)
{
    /// <summary>
    /// Integer percentage of the monthly limit consumed (0..=100), saturating at 0
    /// when the limit is non-positive. Uses integer division to match the original.
    /// </summary>
    public int Percent()
    {
        if (Limit.Value <= 0) return 0;
        return (int)(Spent.Value * 100 / Limit.Value);
    }
}
