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

    /// <summary>
    /// Format an amount in minor units with its own currency and scale. Rendering
    /// R$ 141.57 as "$141.57" is a claim about the wrong currency — the same class
    /// of defect as a fabricated number. Known codes get their symbol; anything
    /// else renders as <c>AMOUNT CODE</c>, still truthful.
    /// </summary>
    public static string FormatMinor(long minor, int decimalPlaces, string? currency)
    {
        var scale = (long)Math.Pow(10, decimalPlaces);
        var sign = minor < 0 ? "-" : "";
        var abs = Math.Abs(minor);
        var number = decimalPlaces == 0
            ? abs.ToString()
            : $"{abs / scale}.{(abs % scale).ToString().PadLeft(decimalPlaces, '0')}";
        return currency switch
        {
            null or "USD" => $"{sign}${number}",
            "BRL" => $"{sign}R${number}",
            "EUR" => $"{sign}€{number}",
            "GBP" => $"{sign}£{number}",
            "JPY" or "CNY" => $"{sign}¥{number}",
            var other => $"{sign}{number} {other}",
        };
    }

    /// <summary>
    /// A currency code alone does not determine its ISO minor-unit exponent
    /// (JPY=0, USD=2, KWD=3, …). When the wire didn't report <c>decimal_places</c>,
    /// keep the amount truthful instead of silently guessing the wrong scale.
    /// </summary>
    public static string FormatMinorUnits(long minor, string currency)
    {
        var sign = minor < 0 ? "-" : "";
        return $"{sign}{Math.Abs(minor)} minor units {currency}";
    }
}

/// <summary>"Extra usage" pay-as-you-go block (claudebar's <c>extra_usage</c>).</summary>
public readonly record struct ExtraUsage(Cents? Limit, Cents Spent, string? Currency = null, int? DecimalPlaces = null)
{
    /// <summary>
    /// Integer percentage of the monthly limit consumed (0..=100), saturating at 0
    /// when there is no limit (an uncapped plan) or it is non-positive. With no
    /// cap there is no denominator, so no meaningful percentage exists.
    /// </summary>
    public int Percent()
    {
        if (Limit is not { Value: > 0 } l) return 0;
        return (int)(Spent.Value * 100 / l.Value);
    }

    public string FmtSpent() => FmtAmount(Spent);

    /// <summary>Null when the payload carries no usable <c>monthly_limit</c> (an
    /// explicit null — observed for uncapped plans like Claude Pro — or an
    /// absent field). The spend stays visible either way; only the limit is
    /// unreported.</summary>
    public string? FmtLimit() => Limit is { } l ? FmtAmount(l) : null;

    private string FmtAmount(Cents amount) => (DecimalPlaces, Currency) switch
    {
        ({ } dp, var currency) => Cents.FormatMinor(amount.Value, dp, currency),
        // Legacy payloads predate both fields and were always cents/USD.
        (null, null) => Cents.FormatMinor(amount.Value, 2, null),
        (null, { } currency) => Cents.FormatMinorUnits(amount.Value, currency),
    };
}
