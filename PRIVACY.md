# Privacy

Limit Lens is a local dashboard. It has no telemetry service and
does not upload your Codex history.

## Inputs

The application uses two read-only sources:

1. `codex app-server` for supported account usage and rate-limit snapshots.
2. JSONL metadata under the selected Codex home `sessions` and
   `archived_sessions` folders.

It opens session files with shared read/delete access, tolerates partial lines,
and never modifies the Codex home directory.

## Retained locally

The app's own SQLite database retains only:

- token counts by type;
- timestamps and daily buckets;
- model names;
- task outcome, duration, and time to first token;
- project basename and a salted, non-reversible path identifier;
- broad tool categories such as shell, patch, web, browser, or connector; and
- relative scan checkpoints needed for incremental indexing.

Installed mode stores this database and versioned JSON settings under
`%LOCALAPPDATA%\LimitLens`. Portable mode uses a neighboring `Data` folder.
Settings are written with atomic replacement.

## Explicit exclusions

Limit Lens deliberately does not read or retain:

- prompt or response text;
- command text;
- tool arguments;
- workspace file contents;
- full project paths;
- `auth.json`; or
- Codex-owned SQLite databases.

The app-server child process is started directly without a shell. Its stderr is
drained without being stored or displayed because diagnostics can contain
sensitive context.

## Account and device separation

Account totals may include another machine or cloud activity. This-device
analytics come only from locally available sessions. Limit Lens labels these
sources separately and never adds the totals together.

## Control and deletion

Settings lets you reindex or delete the app's local aggregate database. Delete
pauses local indexing until Reindex is selected. Neither action deletes or
changes original Codex session files.

Uninstalling the installed application does not silently delete the separate
`%LOCALAPPDATA%\LimitLens` data directory. Delete it in Settings first if you
want the aggregate data removed before uninstalling.
