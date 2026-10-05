# Changelog

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