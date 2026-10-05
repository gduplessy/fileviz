# Changelog

## 0.2.3 — preview

- Initialize folder views from saved roots and indexed directory rows; avoid reading and sorting every file to rediscover roots.
- Report folder initialization, allocation-owner discovery, direct totals, and individual rollup depths during recovery and finalization.
- Mark SQLite view failures as failed and retain the inventory for retry; surface the failure in the desktop UI.
- Include the 0.2.2 indexed aggregation and cancellation fixes, with 56 passing regressions.

## 0.2.2 — preview

- Replace repeated folder and composition scans with indexed temporary aggregates. Wide, nested directory trees no longer trigger quadratic finalization work.
- Interrupt executing SQLite finalization statements when cancelled; retain saved metadata for a later rebuild.
- Add **Rebuild saved views** for interrupted, cancelled, or failed snapshots. Rebuild from persisted inventory without traversing folders, while preserving the original coverage state.
- Add a local `--database PATH` override and `--rebuild-snapshot ID` for recovery, plus a reproducible branching-tree benchmark.
- Verify branching totals, hard-link allocation, unknown allocation, empty folders, repeat rebuilds, native SQL interruption, and desktop recovery without rescanning.

## 0.2.1 — preview

- Show hard-link discovery and refresh, all seven query-index steps, folder/type/age totals, and snapshot saving while finalizing large scans. Clear stale MFT progress when directory fallback begins.
- Commit hard-link metadata in batches of at most 64, skip unchanged updates, and preserve identity checks and unknown allocation.
- Keep elapsed time accurate beyond an hour or a day using a monotonic timer. Count files separately from directories and discard failed raw-scan totals when falling back.
- Pause hard-link batches and finalization step boundaries; explain that saving partial results can continue after cancellation.
- Add a reproducible hard-link metadata benchmark and regression coverage for no-op writes, transaction rollback, bounded batches, fallback counters, and elapsed time.

## 0.2.0 — preview

- Windows 11 Fluent theme with Mica, the Windows accent color, and system, light, or dark mode chosen in Settings.
- New window layout: title bar search (Ctrl+F), left navigation (Home, Explorer, Duplicates, Compare, Cleanup, Diagnostics, Settings; Ctrl+1 to Ctrl+6, F6 to move between panes), and a status bar. Folders and File types are now a switch inside Explorer.
- Home: first-run layout, drive cards with capacity and latest snapshot state, and live scan progress including MFT records read.
- Explorer: two-level space map colored by file type or by age, a usage bar with "not attributed" space, breadcrumb navigation, and an inspector with allocation notes and shell actions. Requires a rescan for snapshots taken by earlier versions.
- Duplicates: pipeline counts, group cards with explicit Keep and Remove choices, and a review window that prechecks identity, metadata, bytes, and named streams for every file before quarantine.
- Compare totals every change and shows the folders that changed most; Cleanup shows held space and per-file details; Diagnostics groups entries into coverage gaps, warnings, and items not traversed.
- Settings are saved: theme, map coloring, scan and duplicate defaults, and saved profiles.
- Design system and redesign notes in `docs/design.md`.

## 0.1.1 — preview

- Branded README with original light/dark SVG banners, release/build badges, direct download buttons, theme-aware screenshots, GitHub callouts, and collapsible setup details.
- Versioned Windows packages built from the latest committed source and documentation.
- Existing scan engines, duplicate analysis, and cleanup behavior are unchanged; preview validation limits still apply.

## 0.1.0 — preview

- Native WPF Windows 11 x64 desktop application with drive/folder/share selection and paged SQLite snapshots.
- Batched native directory scanner; elevated read-only NTFS MFT parser with extension/attribute-list support and fail-closed fallback.
- Treemap, root navigation, largest files/folders, type breakdown, logical/allocated views, filters, profiles, CSV/JSON export, and snapshot comparison.
- Staged SHA-256/SHA-1/MD5 duplicate analysis, name candidates, identity/metadata hash validation, canonical hard-link metadata, and suggested keepers.
- Explicit cleanup review, fresh byte/ADS checks, same-volume quarantine, durable journal, collision-safe restore, and Windows Recycle Bin requests.
- Authenticated worker IPC, disconnect self-termination, safe batch pause/cancel, keyboard navigation, light/dark themes, and DPI awareness.
- GPL-3.0-only repository, pinned SDK/dependencies, Windows CI, self-contained ZIP/installer packaging, checksums, and published preview validation limits.