using AiUsageBar.Core.Config;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Models;
using Xunit;

namespace AiUsageBar.Core.Tests;

[Collection("env")]
public class ConfigTests
{
    [Fact]
    public void DefaultsEnableVisibleVendors()
    {
        var c = new AppConfig();
        Assert.True(c.IsEnabled(VendorId.Anthropic));
        Assert.True(c.IsEnabled(VendorId.Openai));
        Assert.True(c.IsEnabled(VendorId.Copilot));
        // Z.AI / OpenRouter stay enabled in config but are excluded from the UI list.
        Assert.Equal(3, c.EnabledVendors().Count);
        Assert.DoesNotContain(VendorId.Zai, c.EnabledVendors());
        Assert.DoesNotContain(VendorId.Openrouter, c.EnabledVendors());
    }

    [Fact]
    public void MissingFileUsesDefaults()
    {
        var c = AppConfig.LoadFrom(Path.Combine(Path.GetTempPath(), "nope-ai-usagebar.toml"));
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

            [zai]
            enabled = true
            api_key_env = "MY_ZAI"
            plan_tier = "pro"

            [openrouter]
            enabled = false
            """);
        Assert.True(c.IsEnabled(VendorId.Anthropic));
        Assert.False(c.IsEnabled(VendorId.Openai));
        Assert.True(c.IsEnabled(VendorId.Zai));
        Assert.False(c.IsEnabled(VendorId.Openrouter));
        Assert.Equal("MY_ADMIN_KEY", c.Openai.AdminKeyEnv);
        Assert.Equal("MY_ZAI", c.Zai.ApiKeyEnv);
        Assert.Equal("pro", c.Zai.PlanTier);
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
    public void ParsesInlineKeyAndPrimary()
    {
        var c = AppConfig.Parse("""
            [ui]
            primary = "openrouter"

            [zai]
            api_key = "sk-zai-inline"

            [openrouter]
            api_key = "sk-or-inline"
            """);
        Assert.Equal(VendorId.Openrouter, c.Ui.Primary);
        Assert.Equal("sk-zai-inline", c.Zai.ApiKey);
        Assert.Equal("sk-or-inline", c.Openrouter.ApiKey);
    }

    [Fact]
    public void EnabledVendorsPreservesCanonicalOrder()
    {
        var c = new AppConfig();
        Assert.Equal(
            new[] { VendorId.Anthropic, VendorId.Openai, VendorId.Copilot },
            c.EnabledVendors());
    }

    [Fact]
    public void ResolveApiKeyPrefersEnvOverInline()
    {
        const string var = "AI_USAGEBAR_TEST_ENV_WINS";
        Environment.SetEnvironmentVariable(var, "from-env");
        try
        {
            Assert.Equal("from-env", ApiKeyResolver.Resolve("Zai", var, "from-inline"));
        }
        finally { Environment.SetEnvironmentVariable(var, null); }
    }

    [Fact]
    public void ResolveApiKeyFallsBackToInline()
    {
        const string var = "AI_USAGEBAR_TEST_INLINE_FALLBACK";
        Environment.SetEnvironmentVariable(var, null);
        Assert.Equal("inline-key", ApiKeyResolver.Resolve("Zai", var, "inline-key"));
    }

    [Fact]
    public void ResolveApiKeyErrorsWhenBothMissing()
    {
        const string var = "AI_USAGEBAR_TEST_BOTH_MISSING";
        Environment.SetEnvironmentVariable(var, null);
        var ex = Assert.Throws<CredentialsException>(() => ApiKeyResolver.Resolve("Zai", var, null));
        Assert.Contains(var, ex.Message);
        Assert.Contains("api_key", ex.Message);
    }

    [Fact]
    public void ResolveApiKeyTreatsEmptyEnvAsUnset()
    {
        const string var = "AI_USAGEBAR_TEST_EMPTY_ENV";
        Environment.SetEnvironmentVariable(var, "");
        try
        {
            Assert.Equal("inline", ApiKeyResolver.Resolve("OpenRouter", var, "inline"));
        }
        finally { Environment.SetEnvironmentVariable(var, null); }
    }
}

/// <summary>Serialize env-var-mutating tests so they don't race.</summary>
[CollectionDefinition("env", DisableParallelization = true)]
public class EnvCollection;
