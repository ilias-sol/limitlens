# Changelog

All notable changes to Limit Lens are documented here.

## Unreleased

### Fixed

- Backfill the current usage trajectory from privacy-safe rate-limit snapshots
  already present in local Codex session metadata.
- Preserve the full reset-window curve by compacting unchanged snapshots and
  tolerating small reset-timestamp drift.

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
