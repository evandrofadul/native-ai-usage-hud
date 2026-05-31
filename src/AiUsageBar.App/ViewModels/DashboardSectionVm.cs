using AiUsageBar.Core.TokenTracking;

namespace AiUsageBar.App.ViewModels;

/// <summary>
/// Dashboard card for a vendor tab (currently Claude Code / Anthropic): headline
/// stat cards plus an activity heatmap, derived from <see cref="VendorUsageStats"/>.
/// Rendered by the <c>DashboardSectionVm</c> DataTemplate in <c>Controls.xaml</c>.
/// </summary>
public sealed class DashboardSectionVm : SectionVm
{
    public required string Sessions { get; init; }
    public required string Messages { get; init; }
    public required string TotalTokens { get; init; }
    public required string ActiveDays { get; init; }
    public required string CurrentStreak { get; init; }
    public required string LongestStreak { get; init; }
    public required string PeakHour { get; init; }
    public required string FavoriteModel { get; init; }
    public required IReadOnlyList<HeatCell> Heatmap { get; init; }

    public static DashboardSectionVm From(VendorUsageStats s)
    {
        // Heatmap intensity is relative to the busiest day in the window, bucketed
        // into 4 levels (plus level 0 = no activity), à la a contribution graph.
        var max = s.Heatmap.Count > 0 ? s.Heatmap.Max(d => d.Count) : 0;
        var cells = s.Heatmap
            .Select(d => new HeatCell(Level(d.Count, max), $"{d.Date:yyyy-MM-dd}: {d.Count} msg"))
            .ToList();

        return new DashboardSectionVm
        {
            Sessions = s.Sessions.ToString("N0"),
            Messages = s.Messages.ToString("N0"),
            TotalTokens = FmtTokens(s.TotalTokens),
            ActiveDays = s.ActiveDays.ToString("N0"),
            CurrentStreak = Days(s.CurrentStreak),
            LongestStreak = Days(s.LongestStreak),
            PeakHour = s.PeakHour is { } h ? $"{h}h" : "—",
            FavoriteModel = s.FavoriteModel ?? "—",
            Heatmap = cells,
        };
    }

    private static int Level(int count, int max)
    {
        if (count <= 0 || max <= 0) return 0;
        var ratio = (double)count / max;
        return ratio >= 0.75 ? 4 : ratio >= 0.5 ? 3 : ratio >= 0.25 ? 2 : 1;
    }

    private static string Days(int n) => $"{n}d";

    /// <summary>Compact token count: 1.2k / 7.9M.</summary>
    private static string FmtTokens(long n) => n >= 1_000_000
        ? $"{n / 1_000_000.0:0.#}M"
        : n >= 1_000 ? $"{n / 1_000.0:0.#}k" : n.ToString();
}

/// <summary>One heatmap day: an intensity level (0–4) and a tooltip.</summary>
public readonly record struct HeatCell(int Level, string Tooltip);
