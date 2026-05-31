using System.Collections.ObjectModel;
using AiUsageBar.Core;
using AiUsageBar.Core.Models;
using AiUsageBar.Core.TokenTracking;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUsageBar.App.ViewModels;

/// <summary>One vendor tab — header + the section rows for its snapshot.</summary>
public sealed partial class VendorTabViewModel : ObservableObject
{
    public VendorId Vendor { get; }
    public string Header { get; }

    [ObservableProperty] private ObservableCollection<SectionVm> _sections = [];
    [ObservableProperty] private bool _isLoading = true;

    /// <summary>Short status for the tray tooltip (e.g. "29% · 1h 12m"), or null.</summary>
    public string? TrayLine { get; private set; }

    public VendorTabViewModel(VendorId vendor)
    {
        Vendor = vendor;
        Header = vendor.Label();
        Sections = [new TextSectionVm { Value = "Loading…" }];
    }

    public void Apply(TabResult result, DateTimeOffset now, int paceTolerance)
    {
        Sections = new ObservableCollection<SectionVm>(SectionBuilder.Build(result, now, paceTolerance));
        IsLoading = false;
        TrayLine = ComputeTrayLine(result, now);
    }

    /// <summary>Append a local token-usage section (no-op when unavailable).</summary>
    public void SetTokens(ProjectTokenUsage? usage, string title)
    {
        if (usage is null) return;
        Sections.Add(TokenSectionVm.From(usage, title));
    }

    /// <summary>Append the activity dashboard card (no-op when unavailable).</summary>
    public void SetDashboard(VendorUsageStats? stats)
    {
        if (stats is null) return;
        Sections.Add(DashboardSectionVm.From(stats));
    }

    private static string? ComputeTrayLine(TabResult result, DateTimeOffset now)
    {
        if (result is not TabResult.Ready r) return null;
        return r.Outcome.Snapshot switch
        {
            AnthropicSnapshot s => $"{s.Session.UtilizationPct}% · {Core.Pacing.Countdown.Format(s.Session.ResetsAt, now)}",
            OpenAiSnapshot s => $"{s.Session.UtilizationPct}% · {Core.Pacing.Countdown.Format(s.Session.ResetsAt, now)}",
            CopilotSnapshot s => CopilotTrayLine(s, now),
            ZaiSnapshot s when s.Session is { } se => $"{se.UtilizationPct}% · {Core.Pacing.Countdown.Format(se.ResetsAt, now)}",
            OpenRouterSnapshot s => $"{s.ConsumedPct()}% used",
            _ => null,
        };
    }

    /// <summary>Tray tooltip: the first metered quota (premium requests), else "unlimited".</summary>
    private static string? CopilotTrayLine(CopilotSnapshot s, DateTimeOffset now)
    {
        var metered = s.Quotas.FirstOrDefault(q => !q.Unlimited);
        if (metered is null)
            return s.Quotas.Count > 0 ? "unlimited" : null;
        return $"{metered.UtilizationPct}% · {Core.Pacing.Countdown.Format(s.ResetsAt, now)}";
    }
}
