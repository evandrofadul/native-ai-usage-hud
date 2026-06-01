# ai-usagebar-wpf

Cross-platform (Avalonia) port of `ai-usagebar` — monitors AI plan usage for
**Anthropic Claude**, **OpenAI Codex/ChatGPT**, **GitHub Copilot**, **Z.AI (GLM)**
and **OpenRouter** from the system tray. Runs on **Windows** and **Linux**.

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

The Avalonia head sits on top of a platform-neutral core and shared view models:

```
src/
  AiUsageBar.Core/         # platform-neutral core (no UI framework) — all logic + networking
  AiUsageBar.Presentation/ # shared view models + abstractions (IUiDispatcher, IThemeService)
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

The 42 palettes live in `tools/palettes.json`. Run the generator to (re)emit the
C# table and the per-framework dictionaries — never hand-edit the generated files:

```powershell
pwsh tools/Generate-Palettes.ps1            # PaletteColors.cs + Avalonia *.axaml
```

## Paths

| Purpose            | Windows                                       | Linux                                  |
|--------------------|-----------------------------------------------|----------------------------------------|
| Config             | `%APPDATA%\ai-usagebar\config.toml`           | `~/.config/ai-usagebar/config.toml`    |
| Cache (per vendor) | `%LOCALAPPDATA%\ai-usagebar\<vendor>\`        | `~/.local/share/ai-usagebar/<vendor>/` |
| Anthropic creds    | `%USERPROFILE%\.claude\.credentials.json`     | `~/.claude/.credentials.json`          |
| OpenAI creds       | `%USERPROFILE%\.codex\auth.json`              | `~/.codex/auth.json`                   |
| Copilot creds      | Windows Credential Manager (`copilot-cli` / `gh`) | `~/.config/gh/hosts.yml` (gh CLI)  |

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
# oauth_token = "gho_..."   # explicit override; otherwise read from the OS (Credential
#                           # Manager on Windows, gh hosts.yml on Linux)

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
dotnet test                                      # 108 tests
dotnet run --project src/AiUsageBar.AvaloniaApp  # Avalonia head
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
dotnet publish src/AiUsageBar.AvaloniaApp -c Release -r win-x64    # on Windows
dotnet publish src/AiUsageBar.AvaloniaApp -c Release -r linux-x64  # on Linux
```

> Native AOT does **not** cross-compile between operating systems — each target
> must be published **on that OS**. The Linux publish needs `clang` and `zlib`
> installed (e.g. `apt install clang zlib1g-dev`).

## Differences from the original (Rust / Waybar)

- Tray icon + window instead of the Waybar widget and the terminal TUI.
- No Omarchy theme integration — ships the One Dark palette.
- No `SIGRTMIN` signaling — the app auto-refreshes on a 60s `DispatcherTimer`.
- File locking uses `FileShare.None` retries instead of `flock`.
- Config protection relies on the per-user profile directory.
