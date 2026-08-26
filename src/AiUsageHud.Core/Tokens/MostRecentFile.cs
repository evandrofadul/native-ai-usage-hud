namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// Picks the most recently modified file from a set of candidates, but never
/// lets a future mtime (clock skew, DST, a stray file — the same defect fixed
/// for the on-disk vendor cache in <see cref="AiUsageHud.Core.Caching.Cache"/>)
/// look more recent than genuinely current activity. Without this, a bogus
/// future-dated session/subagent transcript would permanently pin the
/// "active project" pick — reopening the app would not help, since the file
/// stays the newest by wall-clock mtime until real time catches up to it.
/// Falls back to the unfiltered set if every candidate is somehow future-dated.
/// </summary>
internal static class MostRecentFile
{
    public static string? Pick(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return null;
        var now = DateTime.UtcNow;
        var plausible = paths.Where(p => SafeMtime(p) <= now).ToArray();
        return (plausible.Length > 0 ? plausible : paths).MaxBy(SafeMtime);
    }

    public static DateTime SafeMtime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
    }
}
