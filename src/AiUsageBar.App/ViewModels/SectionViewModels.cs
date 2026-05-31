using AiUsageBar.Core;
using AiUsageBar.Core.Models;
using AiUsageBar.Core.Pacing;
using AiUsageBar.Core.TokenTracking;

namespace AiUsageBar.App.ViewModels;

/// <summary>Base for a panel row. Port of the Rust <c>panels.rs Section</c> enum.</summary>
public abstract class SectionVm;

/// <summary>Plan/vendor title with an optional right-aligned "Updated …" annotation.</summary>
public sealed class TitleSectionVm : SectionVm
{
    public required string Left { get; init; }
    public string? Right { get; set; }
}

/// <summary>Label + gauge + value + dim footnote.</summary>
public sealed class MetricSectionVm : SectionVm
{
    public required string Label { get; init; }
    public required int Pct { get; init; }
    public required PaceSeverity Severity { get; init; }
    public required string ValueLabel { get; init; }
    public required string Footnote { get; init; }
}

/// <summary>A label followed by one or more dim body lines.</summary>
public sealed class BlockSectionVm : SectionVm
{
    public required string Label { get; init; }
    public required IReadOnlyList<string> Body { get; init; }
}

/// <summary>Free-form key/value (or message) line.</summary>
public sealed class TextSectionVm : SectionVm
{
    public string Label { get; init; } = "";
    public required string Value { get; init; }
}

/// <summary>
/// Local Claude Code token usage for the active project (read from the session
/// transcripts, not the vendor API). Shows the current session total + breakdown,
/// the project-wide total, and the top models.
/// </summary>
public sealed class TokenSectionVm : SectionVm
{
    public required string Title { get; init; }
    public required string Project { get; init; }
    public required string SessionTotal { get; init; }
    public required string SessionBreakdown { get; init; }
    public required string ProjectTotal { get; init; }
    public required IReadOnlyList<string> Models { get; init; }

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
    public static List<SectionVm> Build(TabResult result, DateTimeOffset now, int paceTolerance)
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
                    ZaiSnapshot s => Zai(s, now),
                    OpenRouterSnapshot s => OpenRouter(s),
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
            v.Add(new MetricSectionVm
            {
                Label = "Extra usage",
                Pct = pct,
                Severity = SeverityRules.SeverityFor(pct),
                ValueLabel = $"{e.Spent.FormatDollars()} of {e.Limit.FormatDollars()}",
                Footnote = $"{pct}% of monthly limit consumed",
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

    private static List<SectionVm> Zai(ZaiSnapshot s, DateTimeOffset now)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = s.Plan } };
        if (s.Session is { } se) PushWindow(v, "Session (5h)", se, now, 5, false);
        if (s.Weekly is { } w) PushWindow(v, "Weekly", w, now, 5, false);
        if (s.Mcp is { } m) PushWindow(v, "MCP tools (monthly)", m, now, 5, false);
        if (s.Session is null && s.Weekly is null && s.Mcp is null)
            v.Add(new TextSectionVm { Value = "no usage windows reported" });
        return v;
    }

    private static List<SectionVm> OpenRouter(OpenRouterSnapshot s)
    {
        var v = new List<SectionVm> { new TitleSectionVm { Left = s.Label } };
        var pct = Math.Clamp(s.ConsumedPct(), 0, 100);
        v.Add(new MetricSectionVm
        {
            Label = "Credit balance",
            Pct = pct,
            Severity = SeverityRules.SeverityFor(pct),
            ValueLabel = Money(s.Balance()),
            Footnote = $"{Money(s.TotalUsage)} of {Money(s.TotalCredits)} used ({pct}%)",
        });
        v.Add(new BlockSectionVm
        {
            Label = "Usage by period",
            Body = [$"today {Money(s.UsageDaily)} · week {Money(s.UsageWeekly)} · month {Money(s.UsageMonthly)}"],
        });
        if (s.Limit is { } limit && s.LimitRemaining is { } rem)
            v.Add(new BlockSectionVm { Label = "Per-key limit", Body = [$"{Money(rem)} of {Money(limit)} remaining"] });
        v.Add(new BlockSectionVm { Label = "Tier", Body = [s.IsFreeTier ? "free tier" : "paid tier"] });
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

    private static string Money(double v) =>
        "$" + v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Compact number (drops trailing zeros) for Copilot quota counts.</summary>
    private static string Num(double v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
