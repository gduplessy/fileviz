# FileViz 0.1.1 preview

Windows 11 x64 disk analysis and duplicate review, packaged from the latest committed source and documentation.

## Changes

- Redesigned GitHub README: original SVG banners, build/release badges, direct download buttons, theme-aware desktop screenshots, GitHub callouts, and collapsible setup instructions.
- Updated self-contained portable ZIP and per-user installer. No .NET installation is required.
- Scan engines, content duplicate analysis, and reviewed cleanup behavior remain as in 0.1.0.

## Download

- **Installer:** `FileViz-0.1.1-win-x64-setup.exe`, per-user installation and Start menu integration.
- **Portable:** `FileViz-0.1.1-win-x64-portable.zip`, extract the entire archive and launch `FileViz.exe`.
- **Verify:** use the SHA-256 values in the accompanying `SHA256SUMS.txt`.

## Preview status

Raw NTFS fixture parity passes, but the 2× MFT speed target is unmet on the small cached fixture. Combined UI/worker memory on real million-file volumes and the full provider matrix remain unverified. See the [performance report](https://github.com/gduplessy/fileviz/blob/main/docs/performance.md) and [validation matrix](https://github.com/gduplessy/fileviz/blob/main/docs/validation.md).

Packages are unsigned; Windows SmartScreen may prompt. Cleanup requires explicitly reviewed selections and fresh identity/content checks. Quarantine and Recycle Bin requests do not immediately reclaim disk space. Data stays local; no telemetry, automatic updates, or automatic deletion.

## Verification

- Release build: zero warnings/errors; all 27 tests passed. Windows build/test and packaging workflows passed.
- Exact published ZIP and installer downloaded and checksum-verified. Both ran without SDK paths in the environment: 303 disposable fixture files, expected duplicate pair, zero diagnostics, populated treemap.
- Per-user installation and uninstallation both exited successfully. Executable version and packaged README match the tagged source.
- [Machine-readable package validation](https://github.com/gduplessy/fileviz/blob/main/docs/evidence/packages-0.1.1.json)
