namespace AiUsageBar.Core.TokenTracking;

/// <summary>
/// Shared day-series math for the vendor activity dashboards: current/longest
/// streaks and the weekday-aligned heatmap. Both <see cref="ClaudeUsageStatsReader"/>
/// and <see cref="CodexUsageStatsReader"/> feed it a per-day message count so the
/// alignment logic (and the heatmap window length) lives in exactly one place —
/// the dashboard layout (7 rows per column, see <c>Controls.xaml</c>) is the same
/// for every vendor.
/// </summary>
public static class UsageDayStats
{
    /// <summary>
    /// How many trailing days the heatmap covers. The window is padded so its oldest
    /// cell always falls on a Sunday: with the vertical 7-rows-per-column layout that
    /// pins every weekday to a fixed row — Sunday at the top, today on its own weekday
    /// row. Recomputed per call so the alignment survives the app staying open past
    /// midnight.
    /// </summary>
    public static int HeatmapDays => HeatmapDaysFor(DateOnly.FromDateTime(DateTime.Now));

    /// <summary>Pads the trailing window (~20 weeks) so <paramref name="today"/>'s column starts on a Sunday.</summary>
    private static int HeatmapDaysFor(DateOnly today) => 148 + (int)today.DayOfWeek;

    /// <summary>Consecutive active days ending today (or yesterday — a gap of one day still counts as "current").</summary>
    public static int CurrentStreak(IEnumerable<DateOnly> days)
    {
        var set = days.ToHashSet();
        if (set.Count == 0) return 0;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var cursor = set.Contains(today) ? today : today.AddDays(-1);
        if (!set.Contains(cursor)) return 0;

        var streak = 0;
        while (set.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        return streak;
    }

    /// <summary>Longest run of consecutive active days anywhere in the history.</summary>
    public static int LongestStreak(IEnumerable<DateOnly> days)
    {
        var sorted = days.Distinct().OrderBy(d => d).ToList();
        if (sorted.Count == 0) return 0;

        int best = 1, run = 1;
        for (var i = 1; i < sorted.Count; i++)
        {
            run = sorted[i] == sorted[i - 1].AddDays(1) ? run + 1 : 1;
            if (run > best) best = run;
        }
        return best;
    }

    /// <summary>
    /// The trailing heatmap window (oldest → newest), zero-filled. The day count is
    /// derived from today's weekday so the oldest cell lands on a Sunday and every
    /// weekday stays pinned to its row (see <see cref="HeatmapDays"/>). Uses the local
    /// "today", matching the local-time day buckets the readers produce.
    /// </summary>
    public static List<DayActivity> BuildHeatmap(Dictionary<DateOnly, int> byDay)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = HeatmapDaysFor(today);
        var cells = new List<DayActivity>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            cells.Add(new DayActivity(d, byDay.GetValueOrDefault(d)));
        }
        return cells;
    }
}
