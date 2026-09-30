# Changelog

All notable changes to Limit Lens are documented here.

## Unreleased

### Added

- Separate 5-hour and weekly indicators for Plus in the taskbar and flyout;
  compact weekly-only layouts for Pro and higher-tier plans.
- Reset countdowns and data freshness in the taskbar tooltip.
- Saved taskbar position slider with immediate movement and a reset button;
  relative placement adapts to taskbar width, indicator width, and display scaling.
- Extend positioning past the tray to the taskbar's far-right edge while preserving
  existing saved positions and reset-to-tray behavior.
- Separate taskbar text and bar colour settings, with automatic appearance,
  white/black/gray presets, and validated custom hex colours. Keep settings
  scrollable with the navigation footer visible.

### Fixed

- Select the Codex usage bucket explicitly and identify windows by their duration.
- Display unavailable usage as “—” rather than 100% remaining.
- Prefer weekly forecast history and keep window durations and bucket identities
  separate, including when reset timestamps coincide. Discard ambiguous legacy
  saved history on migration; local session metadata rebuilds the curve.
- Synchronize taskbar content size with host resizing and DPI changes, resume
  colour updates after reload, and handle theme changes on the UI thread.
- Mute alerts until the earliest upcoming reset.

## 0.1.1 - 2026-08-15

### Fixed

- Backfill the current usage trajectory from privacy-safe rate-limit snapshots
  already present in local Codex session metadata.
- Preserve the full reset-window curve by compacting unchanged snapshots and
  tolerating small reset-timestamp drift.
- Reset local trajectory history cleanly when a session log is rewritten,
  truncated, or removed.
- Repair missing or stale Windows startup registration from the saved
  preference, including after reinstalling or moving the executable.

## 0.1.0 - 2026-08-09

Initial public release.

### Added

- Native Windows taskbar usage indicator
- Compact light and dark usage flyout
- Smooth snapshot-based usage history and pace forecast
- Reset countdown, available credits, and reset credits
- Configurable usage alerts and Windows startup
- Portable and per-user installer distributions
- Privacy-minimized local indexing with no prompt or response persistence

### Security

- Full-tree and reachable-history credential audit
- Private build-path detection inside .NET single-file bundles
- Temporary release staging that cannot retain local portable data
- Isolated showcase mode that does not read local Codex sessions
