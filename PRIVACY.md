# Privacy

Limit Lens is a local dashboard. Limit Lens does not operate a telemetry
service and does not upload your Codex history.

## Inputs

The application uses two read-only sources:

1. `codex app-server` for supported account usage and rate-limit snapshots.
2. JSONL records under the selected Codex home `sessions` and
   `archived_sessions` folders.

The JSONL reader necessarily reads each completed record into memory. The
parser extracts only the metadata described below and discards the record. It
does not extract, index, log, or persist prompt text, response text, command
text, tool arguments, or workspace file contents.

Session files are opened with shared read/delete access. Limit Lens tolerates
partial lines and never modifies the Codex home directory.

## Retained locally

The app's SQLite database retains:

- session and turn identifiers;
- token counts by type;
- timestamps and daily buckets;
- model names;
- task outcome, duration, and time to first token;
- project basename and a salted pseudonymous path identifier;
- broad tool categories such as shell, patch, web, browser, or connector; and
- filename-only scan checkpoints needed for incremental indexing.

Project basenames, timestamps, and usage patterns can still be sensitive even
though prompts and full project paths are excluded. Do not publish or share the
database unless you intend to disclose that metadata.

The JSON settings file can retain:

- user-selected Codex home and executable paths;
- window and monitor placement;
- appearance (including taskbar position and text/bar colour choices), startup,
  card, and alert preferences;
- rate-limit usage history (timestamp, remaining percentage, reset timestamp,
  quota-window duration, and metered bucket identifier) and alert deduplication
  state; and
- the random salt used for pseudonymous project identifiers.

Installed mode stores the database and settings under
`%LOCALAPPDATA%\LimitLens`. Portable mode uses a neighboring `Data` folder.
Settings are written with atomic replacement.

Showcase mode uses synthetic account values, an in-memory settings store, and
a no-op local indexer. It does not read local Codex sessions or create a local
Limit Lens data directory.

## Explicit exclusions

Limit Lens does not extract or retain:

- prompt or response text;
- command text;
- tool arguments;
- workspace file contents;
- full project paths from session records;
- `auth.json`; or
- Codex-owned SQLite databases.

The app-server child process is started directly without a shell. Limit Lens
does not open or copy Codex credentials. Its stderr is drained without being
stored or displayed because diagnostics can contain sensitive context.

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
