using AiUsageHud.Core.Config;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Tests;

public class ConfigWriterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "ai-ub-cfg-" + Guid.NewGuid().ToString("N") + ".toml");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* ignore */ }
    }

    [Fact]
    public void WritesMinimalTomlWhenStartingEmpty()
    {
        ConfigWriter.Save(_path, VendorId.Gemini, ThemeId.OneDark);
        var raw = File.ReadAllText(_path);
        Assert.Contains("primary = \"gemini\"", raw);
        Assert.Contains("theme = \"one-dark\"", raw);
    }

    [Fact]
    public void WritesOpacityAndStartupAsUnquotedScalars()
    {
        ConfigWriter.Save(_path, VendorId.Anthropic, ThemeId.Nord, opacity: 80,
            opacityAffectsTray: true, launchAtStartup: false);
        var raw = File.ReadAllText(_path);
        Assert.Contains("opacity = 80", raw);
        Assert.Contains("opacity_affects_tray = true", raw);
        Assert.Contains("launch_at_startup = false", raw);

        var cfg = AppConfig.LoadFrom(_path);
        Assert.Equal(80, cfg.Ui.Opacity);
        Assert.True(cfg.Ui.OpacityAffectsTray);
        Assert.False(cfg.Ui.LaunchAtStartup);
    }

    [Fact]
    public void PreservesExistingCommentsAndUnrelatedFields()
    {
        File.WriteAllText(_path, """
            # my comment
            [ui]
            # pre-existing comment
            primary = "anthropic"

            [gemini]
            enabled = true
            # project comment
            project_id = "my-proj-42"
            """);

        ConfigWriter.Save(_path, VendorId.Gemini, ThemeId.Nord);
        var raw = File.ReadAllText(_path);

        Assert.Contains("# my comment", raw);
        Assert.Contains("# pre-existing comment", raw);
        Assert.Contains("# project comment", raw);
        Assert.Contains("project_id = \"my-proj-42\"", raw);
        Assert.Contains("primary = \"gemini\"", raw);
        Assert.Contains("theme = \"nord\"", raw);

        // And it round-trips through the parser.
        var cfg = AppConfig.LoadFrom(_path);
        Assert.Equal(VendorId.Gemini, cfg.Ui.Primary);
        Assert.Equal(ThemeId.Nord, cfg.Ui.Theme);
        Assert.Equal("my-proj-42", cfg.Gemini.ProjectId);
    }
}
