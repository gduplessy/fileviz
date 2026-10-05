# FileViz 0.2.0 preview

Windows 11 x64 disk analysis and duplicate review, redesigned as a native Windows 11 app.

## Changes

- **Windows 11 design:** Fluent controls, Mica, your Windows accent color, system/light/dark themes (and High Contrast), Snap Layouts, a title bar search (Ctrl+F), and a left navigation pane: Home, Explorer, Duplicates, Compare, Cleanup, Diagnostics, and Settings.
- **Home:** first-run screen, drive cards with capacity and the latest snapshot state, and live scan progress, including MFT records read on raw scans.
- **Explorer:** a two-level space map colored by file type or by age, a usage bar that shows space not attributed to files, breadcrumb navigation, and an inspector with allocation notes and shell actions. Snapshots taken by earlier versions need a rescan before they can be colored.
- **Duplicates:** counts at each stage of the size, sample, and full-hash pipeline; group cards with explicit Keep and Remove choices; and a review window that prechecks identity, metadata, bytes, and named streams for every file before quarantine. Only files that pass are moved, and each move revalidates.
- **Compare, Cleanup, Diagnostics:** totals for every change and the folders that changed most; held space and per-file details for quarantine; diagnostics grouped into coverage gaps, warnings, and items not traversed.
- **Settings** are saved: theme, map coloring, scan and duplicate defaults, and saved profiles.
- Existing index databases are upgraded in place. Scan engines and cleanup safety rules are unchanged.

## Download

- **Installer:** `FileViz-0.2.0-win-x64-setup.exe`, per-user installation and Start menu integration. Upgrades keep local snapshots.
- **Portable:** `FileViz-0.2.0-win-x64-portable.zip`, extract the entire archive and launch `FileViz.exe`.
- **Verify:** use the SHA-256 values in the accompanying `SHA256SUMS.txt`.

## Preview status

Raw NTFS fixture parity passes, but the 2× MFT speed target is unmet on the small cached fixture. Combined UI/worker memory on real million-file volumes and the full provider matrix remain unverified. See the [performance report](https://github.com/gduplessy/fileviz/blob/main/docs/performance.md) and [validation matrix](https://github.com/gduplessy/fileviz/blob/main/docs/validation.md).

Packages are unsigned; Windows SmartScreen may prompt. Cleanup requires explicitly reviewed selections and fresh identity/content checks. Quarantine and Recycle Bin requests do not immediately reclaim disk space. Data stays local; no telemetry, automatic updates, or automatic deletion.

## Verification

VERIFICATION
