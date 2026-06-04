using System.Collections.ObjectModel;
using AiUsageBar.Core;
using AiUsageBar.Core.Models;
using AiUsageBar.Core.TokenTracking;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUsageBar.Presentation.ViewModels;

/// <summary>One vendor tab — header + the section rows for its snapshot.</summary>
public sealed partial class VendorTabViewModel : ObservableObject
{
    public VendorId Vendor { get; }
    public string Header { get; }

    /// <summary>Gemini-only: group the per-model quota buckets by variant (from config).</summary>
    private readonly bool _geminiGroupByVariant;

    [ObservableProperty] private ObservableCollection<SectionVm> _sections = [];
    [ObservableProperty] private bool _isLoading = true;

    /// <summary>
    /// The three independent pieces that make up the section list, kept apart so each can
    /// be refreshed on its own cadence (the API snapshot in <see cref="Apply"/>, the local
    /// token/dashboard reads via <see cref="SetTokens"/>/<see cref="SetDashboard"/>) and
    /// then re-composed into <see cref="Sections"/> by an in-place reconcile.
    /// </summary>
    private IReadOnlyList<SectionVm> _baseSections;
    private TokenSectionVm? _tokenSection;
    private DashboardSectionVm? _dashboardSection;

    /// <summary>Short status for the tray tooltip (e.g. "29% · 1h 12m"), or null.</summary>
    public string? TrayLine { get; private set; }

    public VendorTabViewModel(VendorId vendor, bool geminiGroupByVariant = true)
    {
        Vendor = vendor;
        Header = vendor.Label();
        _geminiGroupByVariant = geminiGroupByVariant;
        _baseSections = [new TextSectionVm { Value = "Loading…" }];
        Compose();
    }

    public void Apply(TabResult result, DateTimeOffset now, int paceTolerance)
    {
        _baseSections = SectionBuilder.Build(result, now, paceTolerance, _geminiGroupByVariant);
        Compose();
        IsLoading = false;
        TrayLine = ComputeTrayLine(result, now);
    }

    /// <summary>Set (or clear) the local token-usage section.</summary>
    public void SetTokens(ProjectTokenUsage? usage, string title)
    {
        _tokenSection = usage is null ? null : TokenSectionVm.From(usage, title);
        Compose();
    }

    /// <summary>Set (or clear) the activity dashboard card.</summary>
    public void SetDashboard(VendorUsageStats? stats)
    {
        _dashboardSection = stats is null ? null : DashboardSectionVm.From(stats);
        Compose();
    }

    /// <summary>Re-compose the full ordered section list and reconcile it into <see cref="Sections"/>.</summary>
    private void Compose()
    {
        var target = new List<SectionVm>(_baseSections);
        if (_tokenSection is not null) target.Add(_tokenSection);
        if (_dashboardSection is not null) target.Add(_dashboardSection);
        Reconcile(target);
    }

    /// <summary>
    /// Update <see cref="Sections"/> to match <paramref name="target"/> while reusing the
    /// existing row instances wherever the type at a position is unchanged — so the visual
    /// container survives and only the bound values repaint. This is what removes the
    /// per-refresh flicker; the collection itself is never reassigned.
    /// </summary>
    private void Reconcile(IReadOnlyList<SectionVm> target)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (i < Sections.Count && Sections[i].GetType() == target[i].GetType())
                Sections[i].UpdateFrom(target[i]);   // same row kind → update in place
            else if (i < Sections.Count)
                Sections[i] = target[i];             // kind changed → swap this row
            else
                Sections.Add(target[i]);             // list grew
        }
        while (Sections.Count > target.Count)
            Sections.RemoveAt(Sections.Count - 1);   // list shrank
    }

    private static string? ComputeTrayLine(TabResult result, DateTimeOffset now)
    {
        if (result is not TabResult.Ready r) return null;
        return r.Outcome.Snapshot switch
        {
            AnthropicSnapshot s => $"{s.Session.UtilizationPct}% · {Core.Pacing.Countdown.Format(s.Session.ResetsAt, now)}",
            OpenAiSnapshot s => $"{s.Session.UtilizationPct}% · {Core.Pacing.Countdown.Format(s.Session.ResetsAt, now)}",
            CopilotSnapshot s => CopilotTrayLine(s, now),
            GeminiSnapshot s => GeminiTrayLine(s, now),
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

    /// <summary>Tray tooltip: the most-used model bucket + its reset countdown.</summary>
    private static string? GeminiTrayLine(GeminiSnapshot s, DateTimeOffset now)
    {
        if (s.Quotas.Count == 0) return null;
        var top = s.Quotas.MaxBy(q => q.UtilizationPct)!;
        return top.ResetsAt is { } r
            ? $"{top.UtilizationPct}% · {Core.Pacing.Countdown.Format(r, now)}"
            : $"{top.UtilizationPct}%";
    }
}
