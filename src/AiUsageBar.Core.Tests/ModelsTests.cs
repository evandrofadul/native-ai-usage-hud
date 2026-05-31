using AiUsageBar.Core.Models;
using AiUsageBar.Core.Pacing;
using Xunit;

namespace AiUsageBar.Core.Tests;

public class ModelsTests
{
    [Fact]
    public void CentsFormatPositive()
    {
        Assert.Equal("$0.00", new Cents(0).FormatDollars());
        Assert.Equal("$0.50", new Cents(50).FormatDollars());
        Assert.Equal("$2.50", new Cents(250).FormatDollars());
        Assert.Equal("$50.00", new Cents(5000).FormatDollars());
    }

    [Fact]
    public void CentsFormatNegativeUsesLeadingSign()
    {
        Assert.Equal("-$1.50", new Cents(-150).FormatDollars());
        Assert.Equal("-$0.01", new Cents(-1).FormatDollars());
    }

    [Fact]
    public void ExtraPercentWithZeroLimitIsZero() =>
        Assert.Equal(0, new ExtraUsage(new Cents(0), new Cents(100)).Percent());

    [Fact]
    public void ExtraPercentTruncates() =>
        Assert.Equal(33, new ExtraUsage(new Cents(10000), new Cents(3333)).Percent());

    private static UsageWindow W(int pct) => new(pct, null, TimeSpan.FromHours(5));

    private static AnthropicSnapshot Snap(int s, int w, int? sonnet, (long limit, long spent)? extra) =>
        new(
            "Max 5x",
            W(s),
            W(w),
            sonnet is { } so ? W(so) : null,
            extra is { } e ? new ExtraUsage(new Cents(e.limit), new Cents(e.spent)) : null);

    [Fact]
    public void SeverityPicksWorstOfThreeWindows() =>
        Assert.Equal(PaceSeverity.High, Snap(40, 60, 80, null).Severity());

    [Fact]
    public void SeverityIgnoresExtraWhenNoCapHit() =>
        Assert.Equal(PaceSeverity.Mid, Snap(50, 60, null, (10000, 9500)).Severity());

    [Fact]
    public void SeverityPromotesExtraWhenSessionAt100() =>
        Assert.Equal(PaceSeverity.Critical, Snap(100, 50, null, (10000, 9500)).Severity());

    [Fact]
    public void OpenRouterConsumedPctAndBalance()
    {
        var s = new OpenRouterSnapshot("x", 100.0, 30.0, 1, 5, 30, false, 50, 20);
        Assert.Equal(70.0, s.Balance(), 9);
        Assert.Equal(30, s.ConsumedPct());
    }

    [Fact]
    public void OpenRouterConsumedPctHandlesZeroCredits()
    {
        var s = new OpenRouterSnapshot("x", 0.0, 5.0, 0, 0, 0, true, null, null);
        Assert.Equal(0, s.ConsumedPct());
    }
}
