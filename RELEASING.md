# Releasing Limit Lens

## Before building

1. Choose a SemVer value without a leading `v` and update `CHANGELOG.md`.
2. Work from a clean clone or clean worktree. Review every untracked file with
   `git status --short`.
3. Confirm GitHub private vulnerability reporting, secret scanning, and push
   protection are enabled for the repository.
4. Run the privacy, credential, history, and Git identity audit:

   ```powershell
   ./scripts/audit-release.ps1 -IncludeGitMetadata
   ```

## Build and verify

The normal release contains only the installer, portable ZIP, and checksum
file:

```powershell
./scripts/build-release.ps1 -Version 0.1.1
```

Use `-SkipInstaller` when Inno Setup 6 is unavailable. This produces a verified
portable ZIP and matching checksum file.

The build uses temporary package staging, rejects `Data` directories and
development files, and inspects the compressed .NET bundle for private build
paths. Do not upload locally built files if any audit or verification step
fails.

Showcase packages are internal screenshot/test artifacts only:

```powershell
./scripts/build-release.ps1 -Version 0.1.1 -SkipInstaller -IncludeShowcase
```

Never attach a showcase package, extracted staging directory, `Data` folder,
database, or settings file to a public release.

## Release checklist

1. Install on a clean Windows account and verify startup, uninstall, taskbar
   recovery, light/dark appearance, alerts, and portable data placement.
2. Confirm the release directory contains only the expected installer and/or
   portable ZIP plus `SHA256SUMS.txt`.
3. Verify every checksum and confirm the ZIP contains no `Data`, PDB, user, or
   development files.
4. Confirm the README screenshot uses synthetic data and contains no unwanted
   system or account details.
5. Tag the reviewed commit as `v0.1.1` and push the tag. The workflow builds in
   a clean runner and creates a draft GitHub release.
6. Review the draft's files, generated notes, checksums, and unsigned-binary
   warning before publishing.

Version 0.1.1 is unsigned and may trigger Microsoft Defender SmartScreen.
