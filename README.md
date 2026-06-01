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

Two UI heads (WPF and Avalonia) share the same core and view models:

```
src/
  AiUsageBar.Core/         # platform-neutral core (no UI framework) — all logic + networking
  AiUsageBar.Presentation/ # shared view models + abstractions (IUiDispatcher, IThemeService)
  AiUsageBar.App/          # WPF head — UI + tray
  AiUsageBar.AvaloniaApp/  # Avalonia head — UI + tray
  AiUsageBar.Core.Tests/   # xUnit suite (ported from the Rust tests)
```

`AiUsageBar.Core` mirrors the Rust crate module-for-module: `Models`, `Pacing`
(pacing/countdown/severity), `Config` (+ `AppPaths`, `ApiKeyResolver`,
`ConfigWriter`), `Caching` (atomic write + TTL + lock + last-error), and one
folder per vendor (`Vendors/Anthropic`, `OpenAi`, `Copilot`, `Zai`,
`OpenRouter`) with `Types` / `Creds` / `OAuth` / `Fetcher`. `UsageService` is
the orchestrator. `Core/Theming/PaletteColors.cs` is the single source of truth
for the 42 theme palettes.

`AiUsageBar.Presentation` holds every view model (`MainViewModel`,
`VendorTabViewModel`, `SettingsViewModel`, the section/dashboard VMs,
`ThemeOption`) plus `OpacityManager` and the framework-agnostic abstractions each
head implements: `IUiDispatcher` (timer) and `IThemeService` (palette swap). Both
heads are thin — only XAML + framework interop (window chrome, animations, tray,
icon rendering).

### Two heads, same look

The Avalonia head reproduces the WPF UI exactly: the same borderless window,
tabs, gauges, dashboard/heatmap, settings overlay and palettes. It deliberately
does **not** use a prebuilt Avalonia theme — `Themes/Controls.axaml` re-templates
every visible control by hand to match WPF. `Avalonia.Themes.Simple` is included
only as structural plumbing for controls that aren't drawn directly
(`ScrollViewer`/`ItemsControl`), and is fully overridden.

### Theme palettes (single source of truth)

The 42 palettes live in `tools/palettes.json`. Run the generator to (re)emit the
C# table and the per-framework dictionaries — never hand-edit the generated files:

```powershell
pwsh tools/Generate-Palettes.ps1            # PaletteColors.cs + Avalonia *.axaml
pwsh tools/Generate-Palettes.ps1 -IncludeWpf  # also regenerate the WPF *.xaml
```

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
dotnet test                              # 108 tests
dotnet run --project src/AiUsageBar.App          # WPF head
dotnet run --project src/AiUsageBar.AvaloniaApp  # Avalonia head
```

Requires the .NET 10 SDK (the WPF head also needs the Windows Desktop workload).
Both heads build the tray glyph at runtime and start hidden to the tray.

## Differences from the Linux original

- Tray icon + window instead of the Waybar widget and the terminal TUI.
- No Omarchy theme integration — ships the One Dark palette.
- No `SIGRTMIN` signaling — the app auto-refreshes on a 60s `DispatcherTimer`.
- File locking uses `FileShare.None` retries instead of `flock`.
- Config protection relies on the per-user profile directory.
