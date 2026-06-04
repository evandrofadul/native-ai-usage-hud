namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// A bucket of token counts as reported by Claude Code's session logs
/// (<c>message.usage</c>). Cache tokens are tracked separately because they
/// dominate the totals and are billed differently.
/// </summary>
public readonly record struct TokenTotals(long Input, long Output, long CacheCreate, long CacheRead)
{
    /// <summary>Sum of every bucket.</summary>
    public long Total => Input + Output + CacheCreate + CacheRead;

    /// <summary>The totalizer shown in the UI: input + output, excluding cache tokens.</summary>
    public long NonCache => Input + Output;

    /// <summary>Cache-creation + cache-read tokens.</summary>
    public long Cache => CacheCreate + CacheRead;

    public static TokenTotals operator +(TokenTotals a, TokenTotals b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheCreate + b.CacheCreate, a.CacheRead + b.CacheRead);
}

/// <summary>Per-model token totals.</summary>
public sealed record ModelTokens(string Model, TokenTotals Totals);

/// <summary>
/// Token usage for the active Claude Code project: the current session (latest
/// session file) plus the project-wide total, broken down by model.
/// </summary>
public sealed record ProjectTokenUsage(
    string Project,
    TokenTotals Session,
    TokenTotals ProjectTotal,
    IReadOnlyList<ModelTokens> ByModel);
