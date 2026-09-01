using System.Text;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Config;

/// <summary>
/// Write settings back to <c>config.toml</c> while preserving comments and
/// unrelated fields. Port of the Rust <c>tui/settings.rs</c> save logic.
///
/// Implemented as a line-based editor (rather than a syntax-tree rewrite) because
/// our config is a flat <c>key = value</c> layout and a textual edit preserves all
/// surrounding comments/whitespace exactly, with no trivia bookkeeping.
/// </summary>
public static class ConfigWriter
{
    /// <summary>
    /// Persist <c>[ui].primary</c>, <c>[ui].theme</c>, vendor enabled states and the window/opacity/startup
    /// preferences, preserving all surrounding comments and unrelated fields.
    /// </summary>
    public static void Save(string path, VendorId primary, ThemeId theme,
        int? opacity = null, bool? opacityAffectsTray = null, bool? launchAtStartup = null,
        IReadOnlyDictionary<VendorId, bool>? vendorEnabled = null)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var lines = text.Length == 0
            ? new List<string>()
            : text.Replace("\r\n", "\n").Split('\n').ToList();

        SetKey(lines, "ui", "primary", primary.Slug());
        SetKey(lines, "ui", "theme", theme.Slug());
        // Opacity + its tray flag are TOML scalars (number / bool), so write them unquoted.
        if (opacity is { } op) SetKey(lines, "ui", "opacity", op.ToString(), quote: false);
        if (opacityAffectsTray is { } tray) SetKey(lines, "ui", "opacity_affects_tray", tray ? "true" : "false", quote: false);
        if (launchAtStartup is { } launch) SetKey(lines, "ui", "launch_at_startup", launch ? "true" : "false", quote: false);

        if (vendorEnabled is not null)
        {
            foreach (var (vendor, isEnabled) in vendorEnabled)
            {
                SetKey(lines, vendor.Slug(), "enabled", isEnabled ? "true" : "false", quote: false);
            }
        }

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Count - 1) sb.Append('\n');
        }
        if (sb.Length == 0 || sb[^1] != '\n') sb.Append('\n');

        Cache.AtomicWrite(path, Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static void SetKey(List<string> lines, string section, string key, string value, bool quote = true)
    {
        var rendered = quote ? $"{key} = \"{value}\"" : $"{key} = {value}";
        var sectionStart = FindSection(lines, section);

        if (sectionStart < 0)
        {
            // Append a new section block, separated by a blank line when needed.
            if (lines.Count > 0 && lines[^1].Trim().Length != 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add(rendered);
            return;
        }

        // Scan the section body for an existing (non-commented) key.
        for (var i = sectionStart + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('[')) break; // next section
            if (IsKeyLine(trimmed, key))
            {
                lines[i] = rendered;
                return;
            }
        }

        // Not present — insert right after the section header.
        lines.Insert(sectionStart + 1, rendered);
    }

    private static int FindSection(List<string> lines, string section)
    {
        var header = $"[{section}]";
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Trim() == header)
                return i;
        return -1;
    }

    private static bool IsKeyLine(string trimmed, string key)
    {
        if (trimmed.StartsWith('#')) return false;
        if (!trimmed.StartsWith(key, StringComparison.Ordinal)) return false;
        var rest = trimmed[key.Length..].TrimStart();
        return rest.StartsWith('=');
    }
}
