using AiUsageHud.Core;
using AiUsageHud.Core.Models;
using AiUsageHud.Core.Pacing;
using AiUsageHud.Core.TokenTracking;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUsageHud.Presentation.ViewModels;

/// <summary>
/// Base for a panel row. Port of the Rust <c>panels.rs Section</c> enum.
/// Rows are <see cref="ObservableObject"/>s and refresh themselves in place via
/// <see cref="UpdateFrom"/> — the list is reconciled instead of rebuilt, so a refresh
/// only repaints the values that changed rather than tearing down and re-creating every
/// container (which is what made the panel flicker on each update).
/// </summary>
public abstract class SectionVm : ObservableObject
{
    /// <summary>
    /// Copy the values from <paramref name="other"/> (which must be the same runtime type)
    /// into this instance, raising change notifications so the existing visual updates in
    /// place. No-op if the types differ.
    /// </summary>
    public abstract void UpdateFrom(SectionVm other);
}

/// <summary>Plan/vendor title with an optional right-aligned "Updated …" annotation.</summary>
public sealed partial class TitleSectionVm : SectionVm
{
    [ObservableProperty] private string _left = "";
    [ObservableProperty] private string? _right;

    public override void UpdateFrom(SectionVm other)
    {
        if (other is not TitleSectionVm o) return;
        Left = o.Left;
        Right = o.Right;
    }
}

/// <summary>Label + gauge + value + dim footnote.</summary>
public sealed partial class MetricSectionVm : SectionVm
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private int _pct;
    [ObservableProperty] private PaceSeverity _severity;
    [ObservableProperty] private string _valueLabel = "";
    [ObservableProperty] private string _footnote = "";

    public override void UpdateFrom(SectionVm other)
    {
        if (other is not MetricSectionVm o) return;
        Label = o.Label;
        Pct = o.Pct;
        Severity = o.Severity;
        ValueLabel = o.ValueLabel;
        Footnote = o.Footnote;
    }
}

/// <summary>A label followed by one or more dim body lines.</summary>
public sealed partial class BlockSectionVm : SectionVm
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private IReadOnlyList<string> _body = [];

    public override void UpdateFrom(SectionVm other)
    {
        if (other is not BlockSectionVm o) return;
        Label = o.Label;
        Body = o.Body;
    }
}

/// <summary>Free-form key/value (or message) line.</summary>
public sealed partial class TextSectionVm : SectionVm
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _value = "";

    public override void UpdateFrom(SectionVm other)
    {
        if (other is not TextSectionVm o) return;
        Label = o.Label;
        Value = o.Value;
    }
}

/// <summary>
/// Local Claude Code token usage for the active project (read from the session
/// transcripts, not the vendor API). Shows the current session total + breakdown,
/// the project-wide total, and the top models.
/// </summary>
public sealed partial class TokenSectionVm : SectionVm
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _project = "";
    [ObservableProperty] private string _sessionTotal = "";
    [ObservableProperty] private string _sessionBreakdown = "";
    [ObservableProperty] private string _projectTotal = "";
    [ObservableProperty] private IReadOnlyList<string> _models = [];

    public override void UpdateFrom(SectionVm other)
    {
        if (other is not TokenSectionVm o) return;
        Title = o.Title;
        Project = o.Project;
        SessionTotal = o.SessionTotal;
        SessionBreakdown = o.SessionBreakdown;
        ProjectTotal = o.ProjectTotal;
        Models = o.Models;
    }

    public static TokenSectionVm From(ProjectTokenUsage u, string title) => new()
    {
        Title = title,
        Project = ShortProject(u.Project),
        SessionTotal = Fmt(u.Session.NonCache),
        SessionBreakdown = $"in {Fmt(u.Session.Input)} · out {Fmt(u.Session.Output)} · cache {Fmt(u.Session.Cache)}",
        ProjectTotal = Fmt(u.ProjectTotal.NonCache),
        Models = u.ByModel.Take(3).Select(m => $"{m.Model} · {Fmt(m.Totals.NonCache)}").ToList(),
    };

    /// <summary>Compact token count: 1.2k / 59.8M.</summary>
    private static string Fmt(long n) => n >= 1_000_000
        ? $"{n / 1_000_000.0:0.#}M"
        : n >= 1_000 ? $"{n / 1_000.0:0.#}k" : n.ToString();

    /// <summary>
    /// Display name for the project. Codex reports a real cwd path; Claude reports a
    /// folder name with separators replaced by '-'. Either way, show the trailing part.
    /// </summary>
    private static string ShortProject(string project)
    {
        if (project.IndexOfAny(['\\', '/']) >= 0)
        {
            var seg = project.Split('\\', '/').LastOrDefault(s => s.Length > 0);
            return seg ?? project;
        }
        var idx = project.LastIndexOf("--", StringComparison.Ordinal);
        return idx >= 0 && idx + 2 < project.Length ? project[(idx + 2)..] : project;
    }
}

/// <summary>
/// Builds the section list for a tab. Port of the Rust <c>sections_for</c> +
/// the per-vendor <c>*_sections</c> helpers.
/// </summary>
public static class SectionBuilder
{
    public static List<SectionVm> Build(TabResult result, DateTimeOffset now, int paceTolerance,
        bool geminiGroupByVariant = false)
    {
        switch (result)
        {
            case TabResult.Error e:
                return
                [
                    new TextSectionVm { Label = "Error", Value = e.Message },
                    new TextSectionVm { Value = "Press Refresh to retry." },
                ];

            case TabResult.Ready r:
                var sections = r.Outcome.Snapshot switch
                {
                    AnthropicSnapshot s => Anthropic(s, now, paceTolerance),
                    OpenAiSnapshot s => OpenAi(s, now, paceTolerance),
                    CopilotSnapshot s => Copilot(s, now),
                    GeminiSnapshot s => Gemini(s, now, geminiGroupByVariant),
                    AntigravitySnapshot s => Antigravity(s, now, paceTolerance),
                    _ => [],
                };

                if (sections.Count > 0 && sections[0] is TitleSectionVm title)
                {
                    title.Right = r.FetchedAt is { } at
                        ? $"Updated {at.ToLocalTime():HH:mm:ss}"
                        : "Updated —";
                }

                if (r.Outcome.Stale)
                    sections.Add(new TextSectionVm { Value = "⏸ showing cached data" });

                if (r.Outcome.LastError is { Code: var code, Message: var msg } && code != 0)
                    sections.Add(new TextSectionVm { Label = $"HTTP {code}", Value = msg });

                return sections;

            default:
                return [new TextSectionVm { Value = "Loading…" }];
        }
    }

    private static List<SectionVm> Anthropic(AnthropicSnapshot s, DateTimeOffset now, int tol)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = $"Claude {s.Plan}" } };
        PushWindow(v, "Session (5h)", s.Session, now, tol, true);
        PushWindow(v, "Weekly (7d)", s.Weekly, now, tol, true);
        if (s.Sonnet is { } sonnet) PushWindow(v, "Sonnet only", sonnet, now, tol, false);
        if (s.Extra is { } e)
        {
            var pct = Math.Clamp(e.Percent(), 0, 100);
            // A null limit means an uncapped plan (e.g. Claude Pro) reported no
            // spending cap — the spend is still real and stays visible, but
            // there is no denominator to compute a percentage against.
            var limitLabel = e.FmtLimit();
            v.Add(new MetricSectionVm
            {
                Label = "Extra usage",
                Pct = pct,
                Severity = SeverityRules.SeverityFor(pct),
                ValueLabel = limitLabel is null ? e.FmtSpent() : $"{e.FmtSpent()} of {limitLabel}",
                Footnote = limitLabel is null ? "no monthly limit reported" : $"{pct}% of monthly limit consumed",
            });
        }
        return v;
    }

    private static List<SectionVm> OpenAi(OpenAiSnapshot s, DateTimeOffset now, int tol)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = s.Plan } };
        PushWindow(v, "Codex 5h", s.Session, now, tol, true);
        PushWindow(v, "Codex weekly", s.Weekly, now, tol, true);
        if (s.CodeReview is { } cr) PushWindow(v, "Code review", cr, now, tol, false);
        if (s.Credits is { } c)
        {
            var body = new List<string>();
            // Only surface the balance when there's actually a value (or it's unlimited).
            if (c.Unlimited) body.Add("balance: unlimited");
            else if (c.HasCredits) body.Add($"balance: {c.Balance}");
            if (c.ApproxLocalMessages is { } lm) body.Add($"≈ {lm.Lo}-{lm.Hi} local messages");
            if (c.ApproxCloudMessages is { } cm) body.Add($"≈ {cm.Lo}-{cm.Hi} cloud messages");
            if (body.Count > 0) v.Add(new BlockSectionVm { Label = "Credits", Body = body });
        }
        return v;
    }

    private static List<SectionVm> Copilot(CopilotSnapshot s, DateTimeOffset now)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = s.Plan } };
        if (s.Quotas.Count == 0)
        {
            v.Add(new TextSectionVm { Value = "no quota information reported" });
            return v;
        }
        foreach (var q in s.Quotas)
        {
            if (q.Unlimited)
            {
                v.Add(new BlockSectionVm { Label = q.Name, Body = ["unlimited"] });
                continue;
            }
            var pct = Math.Clamp(q.UtilizationPct, 0, 100);
            var left = $"{Num(q.Remaining)} of {Num(q.Entitlement)} left";
            var footnote = s.ResetsAt is { } r ? $"Resets in {Countdown.Format(r, now)} · {left}" : left;
            if (q.OverageCount > 0) footnote += $" · {q.OverageCount} overage";
            v.Add(new MetricSectionVm
            {
                Label = q.Name,
                Pct = pct,
                Severity = SeverityRules.SeverityFor(pct),
                ValueLabel = $"{pct}%",
                Footnote = footnote,
            });
        }
        return v;
    }

    private static List<SectionVm> Gemini(GeminiSnapshot s, DateTimeOffset now, bool groupByVariant)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = s.Plan } };
        var quotas = groupByVariant ? s.GroupedByVariant() : s.Quotas;
        if (quotas.Count == 0)
        {
            v.Add(new TextSectionVm { Value = "no model usage reported" });
            return v;
        }
        foreach (var q in quotas)
        {
            var pct = Math.Clamp(q.UtilizationPct, 0, 100);
            var footnote = q.ResetsAt is { } r ? $"Resets in {Countdown.Format(r, now)}" : "";
            if (q.Remaining is { } rem) footnote = footnote.Length > 0
                ? $"{footnote} · {Num(rem)} left"
                : $"{Num(rem)} left";
            v.Add(new MetricSectionVm
            {
                Label = q.Model,
                Pct = pct,
                Severity = SeverityRules.SeverityFor(pct),
                ValueLabel = $"{pct}%",
                Footnote = footnote,
            });
        }
        return v;
    }

    private static List<SectionVm> Antigravity(AntigravitySnapshot s, DateTimeOffset now, int tol)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = s.Plan } };

        // Session
        v.Add(new TextSectionVm { Label = "Session" });
        PushWindow(v, "Gemini", s.Session, now, tol, false);
        if (s.ThirdPartySession is { } tpSession)
            PushWindow(v, "Claude & GPT OSS", tpSession, now, tol, false);

        // Weekly
        v.Add(new TextSectionVm { Label = "Weekly" });
        PushWindow(v, "Gemini", s.Weekly, now, tol, false);
        if (s.ThirdPartyWeekly is { } tpWeekly)
            PushWindow(v, "Claude & GPT OSS", tpWeekly, now, tol, false);

        return v;
    }

    private static void PushWindow(List<SectionVm> v, string label, UsageWindow w,
        DateTimeOffset now, int tol, bool showPacing)
    {
        var pct = Math.Clamp(w.UtilizationPct, 0, 100);
        var reset = Countdown.Format(w.ResetsAt, now);
        string footnote;
        if (showPacing)
        {
            var p = PacingMath.Calc(w.UtilizationPct, w.ResetsAt, now, w.WindowDuration, tol);
            footnote = $"Resets in {reset} · {p.ElapsedPct}% elapsed · {p.PointPace.Glyph()} {p.PointLabel}";
        }
        else
        {
            footnote = $"Resets in {reset}";
        }
        v.Add(new MetricSectionVm
        {
            Label = label,
            Pct = pct,
            Severity = SeverityRules.SeverityFor(pct),
            ValueLabel = $"{pct}%",
            Footnote = footnote,
        });
    }

    /// <summary>Compact number (drops trailing zeros) for Copilot quota counts.</summary>
    private static string Num(double v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
