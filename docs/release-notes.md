# FileViz 0.2.3 preview

Fixes excessive finalization time on drives with many nested directories.

## Changes

- Folder initialization reads saved roots and indexed directory rows rather than sorting the entire inventory. Progress now identifies initialization, allocation owners, direct totals, and every rollup depth.
- SQLite view failures preserve a failed, retryable snapshot and display the failure; a disposable trigger test verifies successful recovery after the failure is removed.

- Replace quadratic folder and type/age rollups with indexed temporary aggregates. A CPU trace of a six-hour real-volume operation identified repeated folder aggregation scans as the defect.
- Cancel now interrupts executing SQLite finalization statements and retains the saved inventory.
- **Rebuild saved views** recovers interrupted, cancelled, or failed snapshots without scanning their folders again. Original coverage labels remain visible.
- Recovery supports an explicit local database path and snapshot ID. Existing file inventory, hard-link accounting, and unknown allocation semantics are preserved.

## Download

- **Installer:** `FileViz-0.2.3-win-x64-setup.exe`, per-user installation.
- **Portable:** `FileViz-0.2.3-win-x64-portable.zip`, extract the whole archive and launch `FileViz.exe`.
- **Verify:** compare the package with the attached `SHA256SUMS.txt`.

Close an old FileViz process before upgrading: replacing its binaries cannot patch an already-running aggregation. Upgrades preserve snapshots. After interruption, use Home's **Rebuild saved views** instead of starting another drive scan. Preserve a closed database and its matching WAL before recovery.

## Verification and limits

- Release build: zero warnings/errors; all 56 regression tests passed.
- Branching metadata fixture, 8,000 files / 16,000 folders: finalization 55.19 → 5.66 seconds. Patched 100,000 files / 200,000 folders: 33.29 seconds, 123.43 MiB peak working set. Single runs under shared load; no end-to-end scan speed claim. [Evidence](https://github.com/gduplessy/fileviz/blob/main/docs/evidence/folder-rollup-0.2.2.json).
- Disposable tests verify hard-link allocation once per identity, unknown allocation, empty folders, repeat rebuilds, and actual executing-query cancellation within two seconds.
- A visible desktop fixture confirms recovery uses saved inventory even when new files exist on disk, preserves interrupted coverage, and displays usable file/treemap/duplicate views.

Packages remain unsigned previews. Large index construction can still take time, raw MFT can fall back to directory enumeration, and the raw 2x target and full provider matrix remain unmet or unverified. See [performance](https://github.com/gduplessy/fileviz/blob/main/docs/performance.md) and [validation](https://github.com/gduplessy/fileviz/blob/main/docs/validation.md). No telemetry, automatic updates, or automatic deletion.