<p align="center">
  <img src="src/LimitLens.App/Assets/LimitLens-256.png" width="112" alt="Limit Lens logo">
</p>

<h1 align="center">Limit Lens</h1>

<p align="center">
  Your Codex limit, always visible.<br>
  A polished Windows taskbar companion with a glanceable usage bar and a focused forecast.
</p>

<p align="center">
  Windows 10/11 · Self-contained · Privacy-first · MIT licensed
</p>

## See your runway without breaking focus

Limit Lens places a compact percentage bar beside the Windows notification
area. Click it for the details that matter: how much usage remains, when the
window resets, and whether your current pace is likely to last.

No browser tab. No account page. No noisy dashboard.

## What you get

- **Usage remaining** — a persistent taskbar bar with the exact percentage left
- **Reset timing** — a live countdown plus the full reset date and time
- **Usage trajectory** — a smooth curve built from real recorded snapshots
- **Forecasting** — a recent-pace prediction alongside the sustainable reset pace
- **Credits** — optional available-credit and reset-credit cards when supplied
- **Freshness** — automatic refresh, stale-data visibility, and manual refresh
- **Alerts** — deduplicated warnings at 25%, 10%, and 0% remaining
- **Native behavior** — startup support, Explorer-restart recovery, tray access,
  light mode, and dark mode

## Designed to stay out of the way

The taskbar indicator answers the everyday question immediately. The flyout
opens only when requested and closes when focus moves elsewhere. Settings are
kept separate from the usage view, and optional account details disappear when
they are unavailable.

New installations start with sensible defaults:

- dark appearance;
- start with Windows;
- show available credits; and
- usage alerts enabled.

## Privacy by construction

Limit Lens reads supported account-limit data from `codex app-server` and
privacy-safe metadata from local Codex session logs. It does not read or store
prompts, responses, command text, tool arguments, workspace file contents,
`auth.json`, or Codex-owned databases.

Account-wide values and this-device analytics stay separate. Read the complete
[privacy policy](PRIVACY.md).

## Download

### Installer

The recommended per-user installer requires no administrator access. It adds
Limit Lens to the Start Menu and provides a normal uninstall entry.

### Portable

The portable ZIP contains a self-contained `win-x64` build. Extract it and run
`LimitLens.exe`. The included `portable.flag` keeps settings and aggregates in
the neighboring `Data` directory.

The application does not require a separately installed .NET runtime.

## Requirements

- Windows 10 version 2004 or newer, or Windows 11
- x64 processor
- Codex installed for account-level limits

Local analytics remain available while account data is offline or unavailable.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet restore LimitLens.slnx --locked-mode
dotnet build LimitLens.slnx -c Debug --no-restore
dotnet test LimitLens.slnx -c Debug --no-build --no-restore
```

Create the installer and both portable variants with:

```powershell
./scripts/build-release.ps1 -Version 0.1.0
```

Pass `-SkipInstaller` when Inno Setup 6 is not installed.

## Data locations

| Mode | Location |
|---|---|
| Installed | `%LOCALAPPDATA%\LimitLens` |
| Portable | `Data` beside `LimitLens.exe` |
| Codex input | `%CODEX_HOME%` or `%USERPROFILE%\.codex` |

Limit Lens never modifies the Codex home directory.

## License

[MIT](LICENSE)
