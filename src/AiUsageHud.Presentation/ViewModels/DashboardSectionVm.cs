using System.Collections.ObjectModel;
using AiUsageHud.Core.TokenTracking;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUsageHud.Presentation.ViewModels;

/// <summary>
/// Dashboard card for a vendor tab (currently Claude Code / Anthropic): headline
/// stat cards plus an activity heatmap, derived from <see cref="VendorUsageStats"/>.
/// Rendered by the <c>DashboardSectionVm</c> DataTemplate in <c>Controls.xaml</c>.
/// </summary>
public sealed partial class DashboardSectionVm : SectionVm
{
    [ObservableProperty] private string _sessions = "";
    [ObservableProperty] private string _messages = "";
    [ObservableProperty] private string _totalTokens = "";
    [ObservableProperty] private string _activeDays = "";
    [ObservableProperty] private string _currentStreak = "";
    [ObservableProperty] private string _longestStreak = "";
    [ObservableProperty] private string _peakHour = "";
    [ObservableProperty] private string _favoriteModel = "";

    /// <summary>
    /// Heatmap cells. A live collection so a refresh updates the busy/today cells in place
    /// instead of replacing the whole grid (which would re-create every day square).
    /// </summary>
    public ObservableCollection<HeatCell> Heatmap { get; } = [];

    public static DashboardSectionVm From(VendorUsageStats s)
    {
        var vm = new DashboardSectionVm();
        vm.Populate(s);
        return vm;
    }

    public override void UpdateFrom(SectionVm other)
    {
        if (other is DashboardSectionVm o)
            CopyFrom(o);
    }

    /// <summary>Fill this instance from raw stats (used when first created).</summary>
    private void Populate(VendorUsageStats s)
    {
        Sessions = s.Sessions.ToString("N0");
        Messages = s.Messages.ToString("N0");
        TotalTokens = FmtTokens(s.TotalTokens);
        ActiveDays = s.ActiveDays.ToString("N0");
        CurrentStreak = Days(s.CurrentStreak);
        LongestStreak = Days(s.LongestStreak);
        PeakHour = s.PeakHour is { } h ? $"{h}h" : "—";
        FavoriteModel = s.FavoriteModel ?? "—";

        // Heatmap intensity is relative to the busiest day in the window, bucketed
        // into 4 levels (plus level 0 = no activity), à la a contribution graph.
        var max = s.Heatmap.Count > 0 ? s.Heatmap.Max(d => d.Count) : 0;
        var cells = s.Heatmap
            .Select(d => new HeatCell(Level(d.Count, max), $"{d.Date:yyyy-MM-dd}: {d.Count} msg"))
            .ToList();
        SyncHeatmap(cells);
    }

    /// <summary>Copy another (already-built) dashboard's values into this one in place.</summary>
    private void CopyFrom(DashboardSectionVm o)
    {
        Sessions = o.Sessions;
        Messages = o.Messages;
        TotalTokens = o.TotalTokens;
        ActiveDays = o.ActiveDays;
        CurrentStreak = o.CurrentStreak;
        LongestStreak = o.LongestStreak;
        PeakHour = o.PeakHour;
        FavoriteModel = o.FavoriteModel;
        SyncHeatmap(o.Heatmap);
    }

    /// <summary>
    /// Reconcile the live <see cref="Heatmap"/> against <paramref name="target"/>: replace
    /// only the cells whose value actually changed, and grow/shrink the tail. Most days are
    /// historical and identical between refreshes, so this touches at most a couple of cells.
    /// </summary>
    private void SyncHeatmap(IReadOnlyList<HeatCell> target)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (i < Heatmap.Count)
            {
                if (!Heatmap[i].Equals(target[i])) Heatmap[i] = target[i];
            }
            else
            {
                Heatmap.Add(target[i]);
            }
        }
        while (Heatmap.Count > target.Count)
            Heatmap.RemoveAt(Heatmap.Count - 1);
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
