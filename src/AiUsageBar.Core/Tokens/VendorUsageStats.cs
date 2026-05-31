namespace AiUsageBar.Core.TokenTracking;

/// <summary>One day's message activity, used to build the heatmap / streaks.</summary>
public readonly record struct DayActivity(DateOnly Date, int Count);

/// <summary>
/// Aggregated usage statistics for the active Claude Code project, derived from its
/// session transcripts. Powers the dashboard card: counts, streaks, peak hour, the
/// favourite model and a per-day activity series for the heatmap.
/// </summary>
public sealed record VendorUsageStats(
    string Project,
    int Sessions,
    long Messages,
    long TotalTokens,
    int ActiveDays,
    int CurrentStreak,
    int LongestStreak,
    int? PeakHour,
    string? FavoriteModel,
    IReadOnlyList<DayActivity> Heatmap);
