# Changelog

## 0.3.2 — preview

- Use the FileViz brand mark for the Windows executable, running taskbar icon, and title bar. Include nine transparent icon sizes from 16 to 256 pixels and retain SVG source with a reproducible Windows generator.

## 0.3.1 — preview

- Defer automatic SQLite checkpoints until startup schema changes finish. Upgrading a saved inventory with a large pending WAL can open the window without copying all pending pages first; normal checkpoint thresholds resume afterwards.
- Verify pending WAL preservation, schema creation, and saved snapshots with a disposable database regression.

## 0.3.0 — preview

- Add Photos with a saved drive/folder selector, orientation-independent short-edge and megapixel thresholds, name/path filters, 100-row pages, and unreadable-image diagnostics.
- Analyze image headers on demand using authenticated read-only workers; keep disk scans metadata-only. Revalidate cached identity/change metadata, retain completed batches on cancellation, and display counts, current path, elapsed time, and heartbeat.
- Bound codec workers with a 384 MiB Windows job limit and ten-second read deadlines. Skip reparse paths and placeholders without recall; optional formats depend on installed codecs.
- Load bounded first-frame previews and provide Explorer inspection. Explicit file/page selections use existing reviewed quarantine/restore with fresh identity/timestamp checks. No automatic removal selections.

## 0.2.5 — preview

- Add a drive/folder selector directly to Duplicates, showing saved scan IDs and coverage. Switching scopes selects the matching inventory without leaving the page.
- Move cross-drive controls beside the selector; show the checked roots explicitly and use their latest saved inventories.
- Keep historical snapshot selections synchronized and lock scope changes while analysis runs.
- Verify scope switching, result isolation, cross-drive inclusion, busy lockout, and historical selection with rendered disposable fixtures.

## 0.2.4 — preview

- Show duplicate-analysis activity immediately: database phases, stage counts, actual bytes read, current file, cache hits, errors, elapsed time, and the age of the last work update.
- Add Cancel analysis, native SQLite interruption, and transactional result publication that preserves previous groups on cancellation.
- Revalidate cached metadata in the isolated worker; avoid redundant latest-history lookup for single-snapshot preparation.
- Display explicit complete, cancelled, and failed states. Validate worker progress, database cancellation, cache reuse, and rendered desktop activity with 63 passing regressions and disposable UI fixtures.

## 0.2.3 — preview

- Defer folder ranking indexes until totals are computed, use bounded main/temporary SQLite caches suited to database size, and classify file types with constant extension sets. Verify SQL category and age totals against the core classifier for every known extension.
- Large MFTs support up to two million directory ancestors / an estimated 384 MiB, retaining fallback at the limit; smaller MFTs keep the previous limits.

- Index child-folder navigation and allocated folder rankings after aggregation; use the stored drive root for ranking queries instead of an inventory-wide path filter.

- Initialize folder views from saved roots and indexed directory rows; avoid reading and sorting every file to rediscover roots.
- Report folder initialization, allocation-owner discovery, direct totals, and individual rollup depths during recovery and finalization.
- Mark SQLite view failures as failed and retain the inventory for retry; surface the failure in the desktop UI.
- Include the 0.2.2 indexed aggregation and cancellation fixes, with 59 passing regressions.

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
