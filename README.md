# AI Usage HUD

Cross-platform (Avalonia) port of `ai-usagebar` — monitors AI plan usage for
**Anthropic Claude**, **OpenAI Codex/ChatGPT**, **GitHub Copilot** and
**Gemini CLI** from the system tray. Runs on **Windows** and **Linux**.

It reuses the credentials the official CLIs already wrote to disk (`claude`,
`codex`, `copilot`/`gh`, `gemini`) and the same undocumented usage endpoints, so
there is no separate login.

Supported vendors:

- Anthropic Claude
- OpenAI Codex / ChatGPT
- GitHub Copilot
- Z.AI (GLM)
- OpenRouter

The app reuses credentials that the official CLIs already wrote to disk
(`claude`, `codex`, `copilot` / `gh`) and calls the same undocumented usage
endpoints, so there is no separate in-app login.

## Form Factor

- The app starts hidden in the system tray.
- Left-click the tray icon to open or focus the main window.
- The window shows one tab per enabled vendor with usage gauges, reset
  countdowns, pacing, credits, and related stats.
- Anthropic and OpenAI tabs also show local Claude Code / Codex token usage for
  the active project or workspace when those local logs are available.
- Closing the window hides it back to the tray.
- Use **Quit** from the tray menu to exit the app.
- The tray icon shows the active vendor's short id, colored by severity, plus a
  tooltip with compact usage information.

## Install

### Run from source

1. Install the .NET 10 SDK with the Windows Desktop workload.
2. Authenticate the vendors you want to use.
3. Optionally create `%APPDATA%\ai-usage-hud\config.toml`.
4. Run the app.

Authentication prerequisites:

- Anthropic: run `claude` and sign in.
- OpenAI: run `codex login`.
- Copilot: sign in with the GitHub Copilot CLI or `gh auth login`.
- Z.AI: set `ZAI_API_KEY` or add `[zai].api_key` in `config.toml`.
- OpenRouter: set `OPENROUTER_API_KEY` or add `[openrouter].api_key` in `config.toml`.

```powershell
dotnet build
dotnet test
dotnet run --project src/AiUsageHud.App
```

### Build a distributable executable

```powershell
dotnet publish src/AiUsageHud.App/AiUsageHud.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

The published output lands under `src/AiUsageHud.App/bin/Release/net10.0-windows/win-x64/publish/`.

This publish mode is framework-dependent, so the target machine needs the .NET
10 Windows Desktop Runtime installed.

## Layout

The Avalonia head sits on top of a platform-neutral core and shared view models:

```
src/
  AiUsageHud.Core/         # platform-neutral core (no UI framework) — all logic + networking
  AiUsageHud.Presentation/ # shared view models + abstractions (IUiDispatcher, IThemeService)
  AiUsageHud.App/  # Avalonia head — UI + tray
  AiUsageHud.Core.Tests/   # xUnit suite (ported from the Rust tests)
```

`AiUsageHud.Core` mirrors the Rust crate module-for-module: `Models`, `Pacing`
(pacing/countdown/severity), `Config` (+ `AppPaths`, `ConfigWriter`), `Caching`
(atomic write + TTL + lock + last-error), and one folder per vendor
(`Vendors/Anthropic`, `OpenAi`, `Copilot`, `Gemini`) with `Types` / `Creds` /
`OAuth` / `Fetcher`. `UsageService` is the orchestrator.
`Core/Theming/PaletteColors.cs` is the single source of truth for the 58 theme
palettes.

`AiUsageHud.Presentation` holds every view model (`MainViewModel`,
`VendorTabViewModel`, `SettingsViewModel`, the section/dashboard VMs,
`ThemeOption`) plus `OpacityManager` and the framework-agnostic abstractions the
head implements: `IUiDispatcher` (timer) and `IThemeService` (palette swap). The
head is thin — only XAML + framework interop (window chrome, animations, tray,
icon rendering).

### Hand-rolled theming

`Themes/Controls.axaml` deliberately does **not** use a prebuilt Avalonia theme —
it re-templates every visible control by hand (borderless window, tabs, gauges,
dashboard/heatmap, settings overlay and palettes). `Avalonia.Themes.Simple` is
included only as structural plumbing for controls that aren't drawn directly
(`ScrollViewer`/`ItemsControl`), and is fully overridden.

### Theme palettes (single source of truth)

The 58 palettes live in `tools/palettes.json`. Run the generator to (re)emit the
C# table — never hand-edit the generated files. The Avalonia head builds every
palette at runtime from `PaletteColors.cs`, so it needs only `OneDark.axaml` (the
compile-time default in `App.axaml`), which the generator also emits:

```powershell
pwsh tools/Generate-Palettes.ps1            # PaletteColors.cs + Avalonia OneDark.axaml
```

## Paths

| Purpose            | Windows                                       | Linux                                  |
|--------------------|-----------------------------------------------|----------------------------------------|
| Config             | `%APPDATA%\ai-usage-hud\config.toml`           | `~/.config/ai-usage-hud/config.toml`    |
| Cache (per vendor) | `%LOCALAPPDATA%\ai-usage-hud\<vendor>\`        | `~/.local/share/ai-usage-hud/<vendor>/` |
| Anthropic creds    | `%USERPROFILE%\.claude\.credentials.json`     | `~/.claude/.credentials.json`          |
| OpenAI creds       | `%USERPROFILE%\.codex\auth.json`              | `~/.codex/auth.json`                   |
| Copilot creds      | Windows Credential Manager (`copilot-cli` / `gh`) | `~/.config/gh/hosts.yml` (gh CLI)  |
| Gemini creds       | `%USERPROFILE%\.gemini\oauth_creds.json`      | `~/.gemini/oauth_creds.json`           |

## Configuration

Optional `config.toml` (defaults enable all vendors). Same shape as the Rust
project:

```toml
[ui]
# primary = "anthropic"   # anthropic | openai | copilot | gemini

[anthropic]
enabled = true
# credentials_path = "C:/Users/name/.claude/.credentials.json"

[openai]
enabled = true
# codex_auth_path = "C:/Users/name/.codex/auth.json"

[copilot]
enabled = true
# oauth_token = "gho_..."   # explicit override; otherwise read from the OS (Credential
#                           # Manager on Windows, gh hosts.yml on Linux)

[gemini]
enabled = true
# credentials_path = "..."  # override ~/.gemini/oauth_creds.json
# project_id = "..."        # Code Assist project; otherwise resolved via loadCodeAssist
group_by_variant = true     # collapse models into variants (Flash/Pro…), each showing its
#                           # highest usage; false lists every model
```

The in-app **Settings** dialog edits `[ui]` (primary, theme, opacity, launch-at-login),
preserving the file's comments and unrelated fields. `[gemini].group_by_variant` is
file-only for now.

## Build, Test, And Publish

```powershell
dotnet build
dotnet test                                      # 108 tests
dotnet run --project src/AiUsageHud.App  # Avalonia head
```

Requires the .NET 10 SDK. The app builds the tray glyph at runtime and starts
hidden to the tray.

### Tray per platform

The tray is split behind `ITrayController`:

- **Windows** (`WindowsTrayController`) — a Win32 `Shell_NotifyIcon` with a fully
  Avalonia-styled context menu and a rich hover card.
- **Linux** (`LinuxTrayController`) — Avalonia's cross-platform `TrayIcon` +
  `NativeMenu`. The StatusNotifierItem model (GNOME/KDE/waybar, over DBus) gives no
  per-icon hover/move events and lets the desktop shell own the menu, so the hover
  card and hand-styled menu are Windows-only; Linux gets the native menu (Open /
  Refresh all / vendor switch / Quit). The "ai" glyph is rendered with Skia
  (`TrayGlyphRenderer`), so no `System.Drawing` is needed there.

### Native AOT publish

The Avalonia head sets `PublishAot=true`; a self-contained, AOT-compiled binary
comes out of:

```powershell
dotnet publish src/AiUsageHud.App -c Release -r win-x64    # on Windows
dotnet publish src/AiUsageHud.App -c Release -r linux-x64  # on Linux
```

> Native AOT does **not** cross-compile between operating systems — each target
> must be published **on that OS**. The Linux publish needs `clang` and `zlib`
> installed (e.g. `apt install clang zlib1g-dev`).

## Differences from the original (Rust / Waybar)

- Tray icon + window instead of the Waybar widget and terminal TUI.
- No Omarchy theme integration.
- The default theme is One Dark, but the Windows app ships multiple built-in palettes.
- No `SIGRTMIN` signaling; the app auto-refreshes on a 60-second `DispatcherTimer`.
- File locking uses `FileShare.None` retries instead of `flock`.
- Config and cache storage use per-user Windows profile directories.
