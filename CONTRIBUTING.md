# Contributing

Thanks for helping improve Limit Lens. Keep changes privacy-first and Windows
x64 compatible.

## Development setup

1. Install the .NET 10 SDK on Windows 10 or Windows 11.
2. Run `dotnet restore LimitLens.slnx --locked-mode`.
3. Run `dotnet build LimitLens.slnx -c Debug --no-restore`.
4. Run `dotnet test LimitLens.slnx -c Debug --no-build --no-restore`.
5. Run `./scripts/audit-release.ps1 -IncludeGitMetadata` before submitting a
   release-related change.

## Privacy requirements

Do not add code that extracts, indexes, logs, or persists `auth.json`, Codex
SQLite databases, prompts, responses, command text, tool arguments, or
workspace file contents. New locally stored fields require a privacy-focused
test and an update to `PRIVACY.md`.

Never commit or attach local `Data` directories, SQLite databases, `.env`
files, credentials, build output, test results, or locally produced release
artifacts. Use synthetic fixtures in tests and screenshots.

## Pull requests

Keep changes focused, describe user-visible and privacy effects, and include
tests for behavior changes. Update `README.md`, `CHANGELOG.md`, and release
documentation when the public behavior or packaging changes.

By contributing, you agree that your contribution is licensed under the MIT
License in this repository.
