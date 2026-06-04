using AiUsageHud.Core.Pacing;

namespace AiUsageHud.Core.Tests;

public class CountdownTests
{
    private static DateTimeOffset At(int year, int month, int day, int h, int m) =>
        new(year, month, day, h, m, 0, TimeSpan.Zero);

    [Fact]
    public void MissingResetRendersEmDash() =>
        Assert.Equal("—", Countdown.Format(null, At(2026, 5, 23, 12, 0)));

    [Fact]
    public void PastResetRendersNow() =>
        Assert.Equal("now", Countdown.Format(At(2026, 5, 23, 11, 0), At(2026, 5, 23, 12, 0)));

    [Fact]
    public void ExactZeroRendersNow()
    {
        var t = At(2026, 5, 23, 12, 0);
        Assert.Equal("now", Countdown.Format(t, t));
    }

    [Fact]
    public void HoursMinutesZeroPadded() =>
        Assert.Equal("1h 05m", Countdown.Format(At(2026, 5, 23, 13, 5), At(2026, 5, 23, 12, 0)));

    [Fact]
    public void HoursMinutesNoDaysUnderOneDay() =>
        Assert.Equal("23h 59m", Countdown.Format(At(2026, 5, 24, 11, 59), At(2026, 5, 23, 12, 0)));

    [Fact]
    public void OneDayOneHour() =>
        Assert.Equal("1d 1h", Countdown.Format(At(2026, 5, 24, 13, 30), At(2026, 5, 23, 12, 0)));

    [Fact]
    public void MultipleDaysDropsMinutes() =>
        Assert.Equal("4d 1h", Countdown.Format(At(2026, 5, 27, 13, 45), At(2026, 5, 23, 12, 0)));

    [Fact]
    public void OneSecondRemainingRendersZeroHours()
    {
        var now = At(2026, 5, 23, 12, 0);
        Assert.Equal("0h 00m", Countdown.Format(now + TimeSpan.FromSeconds(1), now));
    }
}
