using System.Text.RegularExpressions;

namespace AiUsageBar.Core.TokenTracking;

/// <summary>
/// Turns a raw model id (e.g. <c>claude-opus-4-8</c>) into a short display name
/// (<c>Opus 4.8</c>) for the dashboard. Falls back to a lightly cleaned-up id for
/// anything that doesn't match the known Anthropic naming scheme.
/// </summary>
public static partial class ModelNames
{
    public static string Friendly(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return model;

        // claude-<family>-<major>-<minor>[-<date>] -> "<Family> <major>.<minor>"
        var m = ClaudePattern().Match(model);
        if (m.Success)
        {
            var family = char.ToUpperInvariant(m.Groups["family"].Value[0]) + m.Groups["family"].Value[1..];
            return $"{family} {m.Groups["major"].Value}.{m.Groups["minor"].Value}";
        }

        return model;
    }

    [GeneratedRegex(@"^claude-(?<family>[a-z]+)-(?<major>\d+)-(?<minor>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ClaudePattern();
}
