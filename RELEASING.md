# Releasing Limit Lens

1. Confirm `CHANGELOG.md` describes the release.
2. Run the privacy and credential audit:

   ```powershell
   ./scripts/audit-release.ps1 -IncludeGitMetadata
   ```

3. Build and test all release formats:

   ```powershell
   ./scripts/build-release.ps1 -Version 0.1.0
   ```

4. Install on a clean Windows account and verify startup, uninstall, taskbar
   recovery, light/dark appearance, and alerts.
5. Confirm `SHA256SUMS.txt` matches every installer and ZIP.
6. Tag the commit as `v0.1.0` and push the tag. The release workflow creates a
   draft GitHub release containing the installer, portable ZIPs, and checksums.
7. Add final screenshots to the README before publishing the draft.

Version 0.1.0 is unsigned and may trigger Microsoft Defender SmartScreen.
