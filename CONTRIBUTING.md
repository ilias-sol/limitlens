# Contributing

Thanks for helping improve Limit Lens. Please keep changes privacy-first and
Windows x64 compatible.

1. Install the .NET 10 SDK on Windows 10 or Windows 11.
2. Run `dotnet restore LimitLens.slnx --locked-mode`.
3. Run `dotnet build LimitLens.slnx -c Debug --no-restore`.
4. Run `dotnet test LimitLens.slnx -c Debug --no-build --no-restore`.

Do not add code that reads `auth.json`, Codex SQLite databases, prompts,
responses, commands, arguments, or workspace file contents. New locally stored
fields require a matching privacy test and an update to `PRIVACY.md`.

By contributing, you agree that your contribution is licensed under the MIT
License in this repository.
