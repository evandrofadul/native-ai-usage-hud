# ai-usagebar-wpf

Windows (WPF) port of `ai-usagebar` — monitors AI plan usage for
**Anthropic Claude**, **OpenAI Codex/ChatGPT**, **GitHub Copilot**, **Z.AI (GLM)**
and **OpenRouter** from the system tray.

It reuses the credentials the official CLIs already wrote to disk (`claude`,
`codex`, `copilot`/`gh`) and the same undocumented usage endpoints, so there is
no separate login.

## Form factor

A single window reachable from a **system-tray icon**:

- Left-click the tray icon → open/focus the window.
- The window has one tab per enabled vendor, each with usage gauges, reset
  countdowns, pacing, credits, etc.
- Closing the window hides it back to the tray; **Quit** is on the tray menu.
- The tray icon shows the active vendor's 3-letter id colored by severity, and a
  tooltip with `session% · reset`.

## Layout

```
src/
  AiUsageBar.Core/        # platform-neutral core (no WPF) — all logic + networking
  AiUsageBar.App/         # WPF UI + tray
  AiUsageBar.Core.Tests/  # xUnit suite (ported from the Rust tests)
```

`AiUsageBar.Core` mirrors the Rust crate module-for-module: `Models`, `Pacing`
(pacing/countdown/severity), `Config` (+ `AppPaths`, `ApiKeyResolver`,
`ConfigWriter`), `Caching` (atomic write + TTL + lock + last-error), and one
folder per vendor (`Vendors/Anthropic`, `OpenAi`, `Copilot`, `Zai`,
`OpenRouter`) with `Types` / `Creds` / `OAuth` / `Fetcher`. `UsageService` is
the orchestrator.

## Windows paths

| Purpose            | Path                                                  |
|--------------------|-------------------------------------------------------|
| Config             | `%APPDATA%\ai-usagebar\config.toml`                   |
| Cache (per vendor) | `%LOCALAPPDATA%\ai-usagebar\<vendor>\`                |
| Anthropic creds    | `%USERPROFILE%\.claude\.credentials.json`             |
| OpenAI creds       | `%USERPROFILE%\.codex\auth.json`                      |
| Copilot creds      | Windows Credential Manager (`copilot-cli` / `gh` entry) |

## Configuration

Optional `config.toml` (defaults enable all five vendors). Same shape as the Rust
project:

```toml
[ui]
# primary = "anthropic"   # anthropic | openai | copilot | zai | openrouter

[anthropic]
enabled = true

[openai]
enabled = true

[copilot]
enabled = true
# oauth_token = "gho_..."   # explicit override; otherwise read from Credential Manager

[zai]
enabled = true
api_key_env = "ZAI_API_KEY"   # env wins; inline api_key is the fallback
# api_key = "..."

[openrouter]
enabled = true
api_key_env = "OPENROUTER_API_KEY"
# api_key = "sk-or-v1-..."
```

The in-app **Settings** dialog edits `[ui].primary` and the Z.AI / OpenRouter
inline keys, preserving the file's comments and unrelated fields.

## Build & run

```powershell
dotnet build
dotnet test                       # 108 tests
dotnet run --project src/AiUsageBar.App
```

Requires the .NET 10 SDK with the Windows Desktop workload (WPF).

## Differences from the Linux original

- Tray icon + window instead of the Waybar widget and the terminal TUI.
- No Omarchy theme integration — ships the One Dark palette.
- No `SIGRTMIN` signaling — the app auto-refreshes on a 60s `DispatcherTimer`.
- File locking uses `FileShare.None` retries instead of `flock`.
- Config protection relies on the per-user profile directory.
