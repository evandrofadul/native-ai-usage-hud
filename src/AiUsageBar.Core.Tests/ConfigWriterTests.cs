using AiUsageBar.Core.Config;
using AiUsageBar.Core.Models;
using Xunit;

namespace AiUsageBar.Core.Tests;

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
        ConfigWriter.Save(_path, VendorId.Zai, ThemeId.OneDark, "zk", "ok");
        var raw = File.ReadAllText(_path);
        Assert.Contains("primary = \"zai\"", raw);
        Assert.Contains("theme = \"one-dark\"", raw);
        Assert.Contains("[zai]", raw);
        Assert.Contains("api_key = \"zk\"", raw);
        Assert.Contains("[openrouter]", raw);
        Assert.Contains("api_key = \"ok\"", raw);
    }

    [Fact]
    public void PreservesExistingCommentsAndUnrelatedFields()
    {
        File.WriteAllText(_path, """
            # my comment
            [ui]
            # pre-existing comment
            primary = "anthropic"

            [zai]
            enabled = true
            api_key_env = "ZAI_API_KEY"
            # tier comment
            plan_tier = "pro"

            [openrouter]
            enabled = true
            api_key_env = "OPENROUTER_API_KEY"
            """);

        ConfigWriter.Save(_path, VendorId.Openrouter, ThemeId.Nord, "zk2", "ok2");
        var raw = File.ReadAllText(_path);

        Assert.Contains("# my comment", raw);
        Assert.Contains("# pre-existing comment", raw);
        Assert.Contains("# tier comment", raw);
        Assert.Contains("api_key_env = \"ZAI_API_KEY\"", raw);
        Assert.Contains("plan_tier = \"pro\"", raw);
        Assert.Contains("primary = \"openrouter\"", raw);
        Assert.Contains("theme = \"nord\"", raw);
        Assert.Contains("api_key = \"zk2\"", raw);
        Assert.Contains("api_key = \"ok2\"", raw);

        // And it round-trips through the parser.
        var cfg = AppConfig.LoadFrom(_path);
        Assert.Equal(VendorId.Openrouter, cfg.Ui.Primary);
        Assert.Equal(ThemeId.Nord, cfg.Ui.Theme);
        Assert.Equal("zk2", cfg.Zai.ApiKey);
        Assert.Equal("ok2", cfg.Openrouter.ApiKey);
    }

    [Fact]
    public void DoesNotWriteEmptyKeys()
    {
        ConfigWriter.Save(_path, VendorId.Anthropic, ThemeId.OneDark, "", "");
        var raw = File.ReadAllText(_path);
        Assert.DoesNotContain("api_key =", raw);
        Assert.Contains("primary = \"anthropic\"", raw);
    }
}
