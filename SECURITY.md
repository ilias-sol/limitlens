# Security policy

## Supported versions

Until Limit Lens reaches version 1.0, security fixes are provided for the most
recent release only. Users should update to the latest available version before
reporting a problem that may already be fixed.

## Reporting a vulnerability

Do not open a public issue for a vulnerability, especially one that could
expose local Codex data. Use GitHub's
[private vulnerability reporting](../../security/advisories/new) instead.

Include the affected version, impact, reproduction steps, and any suggested
mitigation. Do not include real credentials, prompts, session files, or local
databases; use redacted or synthetic examples.

Maintainers aim to acknowledge reports within three business days, provide an
initial assessment within seven business days, and coordinate disclosure after
a fix or mitigation is available. These are response targets rather than a
guarantee.

## Security boundaries

Limit Lens must never open `auth.json` or Codex-owned SQLite databases. It does
not request or persist Codex credentials. Changes that expand locally retained
data require tests and a matching update to `PRIVACY.md`.
