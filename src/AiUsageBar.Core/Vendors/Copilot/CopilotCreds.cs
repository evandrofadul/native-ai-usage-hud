using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using AiUsageBar.Core.Config;
using AiUsageBar.Core.Errors;

namespace AiUsageBar.Core.Vendors.Copilot;

/// <summary>
/// Locates the GitHub OAuth token Copilot uses, mirroring how the other vendors find
/// their credentials. Resolution order:
/// <list type="number">
/// <item>an inline <c>oauth_token</c> from config (explicit override);</item>
/// <item>the Windows Credential Manager entry the GitHub Copilot CLI writes
///   (<c>copilot-cli/https://github.com:&lt;user&gt;</c>);</item>
/// <item>the Git Credential Manager entry for github.com as a last resort.</item>
/// </list>
/// The stored blob has no encoding hint, so we try UTF-8 and UTF-16 and pull out the
/// <c>gho_/ghu_/ghp_</c> token with a pattern match.
/// </summary>
public static partial class CopilotCreds
{
    private const string GitFallbackTarget = "git:https://github.com";

    public static string Resolve(CopilotConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.OauthToken))
            return config.OauthToken.Trim();

        if (OperatingSystem.IsWindows())
        {
            if (TokenFromCredentialStore(config.CredentialTarget) is { } t) return t;
            if (TokenFromTarget(GitFallbackTarget) is { } g) return g;
        }

        throw new CredentialsException(
            "Copilot: no GitHub token found. Sign in with the GitHub Copilot CLI " +
            "(`copilot`/`gh auth login`), or set `oauth_token` under [copilot] in " +
            "%APPDATA%\\ai-usagebar\\config.toml.");
    }

    [SupportedOSPlatform("windows")]
    private static string? TokenFromCredentialStore(string target)
    {
        // A "*" suffix (account part is unknown) means we enumerate and take the first hit.
        if (target.Contains('*'))
        {
            foreach (var blob in WindowsCredentialStore.Enumerate(target))
                if (ExtractToken(blob) is { } tok) return tok;
            return null;
        }
        return TokenFromTarget(target);
    }

    [SupportedOSPlatform("windows")]
    private static string? TokenFromTarget(string target)
    {
        var blob = WindowsCredentialStore.Read(target);
        return blob is null ? null : ExtractToken(blob);
    }

    /// <summary>Decode the blob (UTF-8 then UTF-16) and return the first GitHub token in it.</summary>
    public static string? ExtractToken(byte[] blob)
    {
        foreach (var text in new[] { Encoding.UTF8.GetString(blob), Encoding.Unicode.GetString(blob) })
        {
            var m = TokenPattern().Match(text);
            if (m.Success) return m.Value;
        }
        return null;
    }

    [GeneratedRegex(@"gh[oupsr]_[A-Za-z0-9]{20,}")]
    private static partial Regex TokenPattern();
}
