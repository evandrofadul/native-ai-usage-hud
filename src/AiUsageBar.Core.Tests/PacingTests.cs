using AiUsageBar.Core.Pacing;
using Xunit;

namespace AiUsageBar.Core.Tests;

public class PacingTests
{
    private static DateTimeOffset At(int h, int m) =>
        new(2026, 5, 23, h, m, 0, TimeSpan.Zero);

    private static readonly TimeSpan FiveH = TimeSpan.FromHours(5);

    [Fact]
    public void MissingResetReturnsNeutral()
    {
        var p = PacingMath.Calc(50, null, At(12, 0), FiveH, PacingMath.DefaultTolerance);
        Assert.Equal(PacingResult.Neutral(), p);
    }

    [Fact]
    public void ZeroWindowReturnsNeutral()
    {
        var p = PacingMath.Calc(50, At(12, 0), At(12, 0), TimeSpan.Zero, 5);
        Assert.Equal(PacingResult.Neutral(), p);
    }

    [Fact]
    public void ElapsedClampsToZeroWhenFutureResetBeyondWindow()
    {
        var now = At(12, 0);
        var reset = now + TimeSpan.FromHours(6);
        var p = PacingMath.Calc(10, reset, now, FiveH, 5);
        Assert.Equal(0, p.ElapsedPct);
    }

    [Fact]
    public void ElapsedClampsToHundredWhenPastReset()
    {
        var now = At(12, 0);
        var reset = now - TimeSpan.FromHours(1);
        var p = PacingMath.Calc(50, reset, now, FiveH, 5);
        Assert.Equal(100, p.ElapsedPct);
    }

    [Fact]
    public void PerfectlyEvenPacingIsOnTrack()
    {
        var now = At(12, 0);
        var reset = now + TimeSpan.FromMinutes(150);
        var p = PacingMath.Calc(50, reset, now, FiveH, PacingMath.DefaultTolerance);
        Assert.Equal(50, p.ElapsedPct);
        Assert.Equal(0, p.Delta);
        Assert.Equal(Pace.OnTrack, p.RatioPace);
        Assert.Equal(Pace.OnTrack, p.PointPace);
        Assert.Equal("on track", p.RatioLabel);
        Assert.Equal("on track", p.PointLabel);
    }

    [Fact]
    public void AheadOfPaceAboveTolerance()
    {
        var now = At(12, 0);
        var reset = now + TimeSpan.FromMinutes(150);
        var p = PacingMath.Calc(70, reset, now, FiveH, 5);
        Assert.Equal(20, p.Delta);
        Assert.Equal(Pace.Ahead, p.PointPace);
        Assert.Equal("20pts ahead", p.PointLabel);
        Assert.Equal(Pace.Ahead, p.RatioPace);
        Assert.Equal("40% ahead", p.RatioLabel);
    }

    [Fact]
    public void UnderPaceBelowTolerance()
    {
        var now = At(12, 0);
        var reset = now + TimeSpan.FromMinutes(150);
        var p = PacingMath.Calc(30, reset, now, FiveH, 5);
        Assert.Equal(-20, p.Delta);
        Assert.Equal(Pace.Under, p.PointPace);
        Assert.Equal("20pts under", p.PointLabel);
        Assert.Equal(Pace.Under, p.RatioPace);
        Assert.Equal("40% under", p.RatioLabel);
    }

    [Fact]
    public void WithinToleranceBandIsOnTrackRatioButPointDiverges()
    {
        var now = At(12, 0);
        var reset = now + TimeSpan.FromMinutes(150);
        var p = PacingMath.Calc(52, reset, now, FiveH, PacingMath.DefaultTolerance);
        Assert.Equal(Pace.OnTrack, p.RatioPace);
        Assert.Equal("on track", p.RatioLabel);
        Assert.Equal(Pace.Ahead, p.PointPace);
        Assert.Equal("2pts ahead", p.PointLabel);
    }

    [Fact]
    public void RatioClampsAt999()
    {
        var now = At(12, 0);
        var reset = now + TimeSpan.FromMinutes(297);
        var p = PacingMath.Calc(60, reset, now, FiveH, 5);
        Assert.Equal(1, p.ElapsedPct);
        Assert.Equal("999% ahead", p.RatioLabel);
    }

    [Fact]
    public void ElapsedZeroSkipsRatio()
    {
        var now = At(12, 0);
        var reset = now + FiveH;
        var p = PacingMath.Calc(20, reset, now, FiveH, 5);
        Assert.Equal(0, p.ElapsedPct);
        Assert.Equal(Pace.OnTrack, p.RatioPace);
        Assert.Equal(20, p.Delta);
        Assert.Equal(Pace.Ahead, p.PointPace);
    }

    [Theory]
    [InlineData(-100, PaceSeverity.Low)]
    [InlineData(-10, PaceSeverity.Mid)]
    [InlineData(-1, PaceSeverity.Mid)]
    [InlineData(0, PaceSeverity.Mid)]
    [InlineData(1, PaceSeverity.High)]
    [InlineData(9, PaceSeverity.High)]
    [InlineData(10, PaceSeverity.Critical)]
    [InlineData(100, PaceSeverity.Critical)]
    public void PaceSeverityBoundariesMatchClaudebar(int delta, PaceSeverity expected)
    {
        Assert.Equal(expected, SeverityRules.PaceSeverityFor(delta));
    }

    [Theory]
    [InlineData(0, PaceSeverity.Low)]
    [InlineData(49, PaceSeverity.Low)]
    [InlineData(50, PaceSeverity.Mid)]
    [InlineData(74, PaceSeverity.Mid)]
    [InlineData(75, PaceSeverity.High)]
    [InlineData(89, PaceSeverity.High)]
    [InlineData(90, PaceSeverity.Critical)]
    [InlineData(100, PaceSeverity.Critical)]
    public void SeverityThresholdsMatchClaudebar(int pct, PaceSeverity expected)
    {
        Assert.Equal(expected, SeverityRules.SeverityFor(pct));
    }
}
