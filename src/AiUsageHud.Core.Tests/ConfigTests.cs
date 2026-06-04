using AiUsageHud.Core.Config;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void DefaultsEnableVisibleVendors()
    {
        var c = new AppConfig();
        Assert.True(c.IsEnabled(VendorId.Anthropic));
        Assert.True(c.IsEnabled(VendorId.Openai));
        Assert.True(c.IsEnabled(VendorId.Copilot));
        Assert.True(c.IsEnabled(VendorId.Gemini));
        Assert.Equal(4, c.EnabledVendors().Count);
    }

    [Fact]
    public void MissingFileUsesDefaults()
    {
        var c = AppConfig.LoadFrom(Path.Combine(Path.GetTempPath(), "nope-ai-usage-hud.toml"));
        Assert.True(c.IsEnabled(VendorId.Anthropic));
    }

    [Fact]
    public void ParsesFullConfig()
    {
        var c = AppConfig.Parse("""
            [anthropic]
            enabled = true

            [openai]
            enabled = false
            admin_key_env = "MY_ADMIN_KEY"

            [copilot]
            enabled = false
            """);
        Assert.True(c.IsEnabled(VendorId.Anthropic));
        Assert.False(c.IsEnabled(VendorId.Openai));
        Assert.False(c.IsEnabled(VendorId.Copilot));
        Assert.Equal("MY_ADMIN_KEY", c.Openai.AdminKeyEnv);
    }

    [Fact]
    public void ParsesGeminiSection()
    {
        var c = AppConfig.Parse("""
            [gemini]
            enabled = false
            credentials_path = "C:\\creds\\gemini.json"
            project_id = "my-proj-42"
            group_by_variant = false
            """);
        Assert.False(c.IsEnabled(VendorId.Gemini));
        Assert.Equal("C:\\creds\\gemini.json", c.Gemini.CredentialsPath);
        Assert.Equal("my-proj-42", c.Gemini.ProjectId);
        Assert.False(c.Gemini.GroupByVariant);
    }

    [Fact]
    public void GeminiGroupByVariantDefaultsOn()
    {
        Assert.True(new AppConfig().Gemini.GroupByVariant);
        // Absent from the file → still defaults on.
        Assert.True(AppConfig.Parse("[gemini]\nenabled = true\n").Gemini.GroupByVariant);
    }

    [Fact]
    public void PartialConfigFallsBackToDefaults()
    {
        var c = AppConfig.Parse("[openai]\nenabled = false\n");
        Assert.False(c.IsEnabled(VendorId.Openai));
        Assert.True(c.IsEnabled(VendorId.Anthropic));
        Assert.Equal("OPENAI_ADMIN_KEY", c.Openai.AdminKeyEnv);
    }

    [Fact]
    public void MalformedTomlThrows() =>
        Assert.ThrowsAny<Exception>(() => AppConfig.Parse("this is not = = valid"));

    [Fact]
    public void ParsesUiPrimaryVendor()
    {
        var c = AppConfig.Parse("""
            [ui]
            primary = "gemini"
            """);
        Assert.Equal(VendorId.Gemini, c.Ui.Primary);
    }

    [Fact]
    public void UnknownUiPrimaryIsNull() =>
        Assert.Null(AppConfig.Parse("[ui]\nprimary = \"nope\"\n").Ui.Primary);

    [Fact]
    public void EnabledVendorsPreservesCanonicalOrder()
    {
        var c = new AppConfig();
        Assert.Equal(
            new[] { VendorId.Anthropic, VendorId.Openai, VendorId.Copilot, VendorId.Gemini },
            c.EnabledVendors());
    }
}
