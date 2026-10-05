# Design

FileViz is a native Windows 11 utility. The interface should feel like part of the OS, keep numbers honest, and make every destructive step explicit. This document is the reference for the visual system, information architecture, and screen behavior, plus the implementation plan for moving the current single-window layout to it.

Reference mockups (light/dark, 1440×900): the private design canvas, [FileViz Windows Design](https://claude.ai/artifact/XNLmFwDjgKTFiWo9tRjWCF). Where the canvas and this document disagree, this document wins.

## Principles

- **Native first.** Use the Windows 11 Fluent theme, Mica, the system accent, Segoe UI Variable, and Segoe Fluent Icons. Custom drawing is limited to the treemap and usage bars.
- **Evidence before action.** Show how a claim was established (verified hash, sample, name, hard link) next to the claim. Only verified results carry a savings figure.
- **Honest numbers.** Logical and allocated size are separate. Partial, cancelled, stale, and interrupted snapshots are labelled everywhere they appear. Space that cannot be attributed to files is shown as "Not attributed", never folded into a category. Missing folders are never estimated.
- **Calm density.** Data-dense screens, one accent button per view, status color reserved for state and evidence.

## Information architecture

Left navigation (NavigationView pattern), six sections plus Settings:

| Section | Purpose | Replaces |
| --- | --- | --- |
| Home | Choose roots, scan options, scan progress, recent snapshots. First-run empty state when no snapshot exists. | Sidebar "Drives and roots", scan controls in the summary card |
| Explorer | Breadcrumb, volume summary, treemap, selection inspector, largest files/folders/types. | Space explorer, Folders, File types tabs |
| Duplicates | Hash pipeline, verified and name-only groups, keep/remove selection, review. | Duplicates tab |
| Compare | Before/after snapshot change by folder and by file. | Compare tab |
| Cleanup | Quarantine journal, restore, Recycle Bin requests. | Cleanup history tab |
| Diagnostics | Coverage gaps, interruptions, items not traversed, engine fallbacks. | Diagnostics tab |
| Settings | Theme, map coloring, scan defaults, profiles, duplicates, storage, about. | Theme button, profile controls in the expander |

Folders and File types become a **Files / Folders / Types** switch inside Explorer. The sections other than Home and Settings are disabled until a snapshot exists.

## Window shell

- **Title bar (48 px):** app mark, "FileViz", Preview tag, centered search box (Ctrl+F, searches the selected snapshot and opens Explorer with the filter applied), elevation state ("Standard user", "Worker elevated", "Administrator"), caption buttons. Built with `WindowChrome`; the maximize button must answer `HTMAXBUTTON` so Snap Layouts work.
- **Navigation pane (232 px)** on the Mica ground; selection uses the Fluent pill indicator. Counts appear right-aligned (duplicate groups, files held in quarantine). Collapses to icons below 1200 px window width.
- **Content layer:** rounded top-left corner, 1 px stroke, Layer fill. Page padding 24 px horizontal, 16 to 20 px vertical.
- **Status bar (30 px):** worker state dot, snapshot state, coverage, right-aligned reminders ("Data stays on this PC").

Minimum window size stays 1050×700.

## Tokens

Theme-dependent colors come from the Fluent theme's own brush keys (`Application.ThemeMode`), so light, dark, and High Contrast switch without FileViz code. Always reference them with `DynamicResource`. FileViz-only resources (fonts, file-type and age palettes, brand mark, radii) live in `src/FileViz.App/Themes/Tokens.xaml`; styles live in `Themes/Controls.xaml`. In High Contrast the Fluent keys map to system colors; the treemap should draw outlines and labels instead of fills (phase 8).

| Role | Fluent key |
| --- | --- |
| Window ground | Mica backdrop (`WindowBackground` fallback) |
| Content layer | `LayerFillColorDefaultBrush` |
| Cards, lists | `CardBackgroundFillColorDefaultBrush` / `CardStrokeColorDefaultBrush` |
| Card footers | `CardBackgroundFillColorSecondaryBrush` |
| Dividers | `DividerStrokeColorDefaultBrush` |
| Hover / pressed (subtle) | `SubtleFillColorSecondaryBrush` / `SubtleFillColorTertiaryBrush` |
| Text | `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `TextFillColorTertiaryBrush` |
| Accent | `AccentFillColorDefaultBrush`, `AccentTextFillColorPrimaryBrush`, `TextOnAccentFillColorPrimaryBrush` |
| Status | `SystemFillColorSuccessBrush`, `SystemFillColorCautionBrush`, `SystemFillColorCriticalBrush` and their `...BackgroundBrush` variants |
| Focus | `FocusStrokeColorOuterBrush` |

Reference values used in the mockups, for comparison when reviewing renders:

| Token | Use | Light | Dark |
| --- | --- | --- | --- |
| `MicaFallbackBrush` | Window ground when Mica is unavailable | `#ECEEF1` | `#1C1D20` |
| `LayerBrush` | Content layer | `#F7F8FA` | `#242629` |
| `CardBrush` | Panels, lists | `#FFFFFF` | `#2C2E32` |
| `CardSecondaryBrush` | Card footers, group headers | `#F9FAFB` | `#292B2F` |
| `SunkenBrush` | Bar tracks, segmented control track | `#EEF0F3` | `#202225` |
| `StrokeBrush` | Dividers, card borders | `#E1E4E8` | `#383B40` |
| `StrokeStrongBrush` | Control borders | `#CDD1D7` | `#4A4E55` |
| `InkBrush` | Primary text | `#1A1C1F` | `#F1F2F4` |
| `InkSecondaryBrush` | Secondary text | `#4E535B` | `#C4C8CE` |
| `InkTertiaryBrush` | Captions | `#62676F` | `#A5AAB2` |
| `SuccessBrush` / soft | Verified, complete | `#0E6F35` / `#E2F2E7` | `#74CF94` / 14% |
| `CautionBrush` / soft | Partial, stale, volume over 85% | `#7D4B00` / `#FBEFD9` | `#F0B75E` / 14% |
| `CriticalBrush` / soft | Blocked, failed, coverage gap | `#AE2116` / `#FBE6E4` | `#FF948A` / 14% |

**Accent:** always the Windows accent (`SystemColors.AccentColor*BrushKey`). Mockups show the default blue (`#005FB8` light, `#60CDFF` dark). Never hard-code an accent value. The teal/green/orange app mark is brand only and is not used as UI color.

### File-type palette

Theme-independent. Light fills carry dark labels. Adjacent hues also differ in lightness so the map reads in grayscale.

| Category | Fill | Label | Extensions |
| --- | --- | --- | --- |
| Disk images | `#46546D` | white | vhd vhdx vmdk iso img wim esd qcow2 |
| Video | `#2F5DA8` | white | mp4 mkv mov avi wmv webm m4v ts |
| Code and SDKs | `#B04D66` | white | cs js ts py java go rs c cpp h json xml jar class pdb obj o lib nupkg whl |
| Images | `#23877B` | white | jpg jpeg png gif heic webp tif tiff bmp dng cr2 cr3 nef arw psd |
| Audio | `#7656A6` | white | mp3 flac wav aac m4a ogg opus wma |
| Documents | `#5E8F33` | white | pdf doc docx xls xlsx ppt pptx odt rtf txt md epub |
| Archives and installers | `#D68B2A` | `#1A1C1F` | zip 7z rar tar gz bz2 xz zst cab msi msix appx |
| Caches and temp | `#A3ABB5` | `#1A1C1F` | tmp log etl dmp cache |
| System and other | `#CBD0D7` | `#1A1C1F` | everything else, including exe and dll |

Classification is by extension only, defined once in `FileViz.Core` (`FileCategory`) and reused for SQL aggregation. Path heuristics (for example `node_modules`) are out of scope so the result stays predictable.

### Age ramp

Bucketed by last-modified time relative to the snapshot's start time, so a snapshot's colors do not drift as it ages.

| Bucket | Fill | Label |
| --- | --- | --- |
| Under 30 days | `#EDE6D8` | dark |
| 1 to 6 months | `#DCC7A0` | dark |
| 6 to 12 months | `#C49A55` | dark |
| 1 to 3 years | `#9C6A22` | white |
| Over 3 years | `#6B4210` | white |

Older is darker so stale data stands out. A folder takes the byte-weighted mean bucket of its contents.

### Typography

| Style | Font | Size / line | Weight |
| --- | --- | --- | --- |
| Title | Segoe UI Variable Display | 28 / 36 | Semibold |
| Subtitle | Segoe UI Variable Display | 20 / 28 | Semibold |
| Body strong | Segoe UI Variable Text | 14 / 20 | Semibold |
| Body | Segoe UI Variable Text | 14 / 20 | Regular |
| Caption | Segoe UI Variable Text | 12 / 16 | Regular |
| Mono | Cascadia Mono, fallback Consolas | 12 / 18 | Regular |

Paths, hashes, and file IDs use Mono. Every number uses tabular figures (`Typography.NumeralAlignment="Tabular"`).

### Shape, spacing, elevation

- Radius: controls 4 px, cards and dialogs 8 px, treemap tiles 3 px.
- 4 px grid. Gaps: 8 within a control group, 12 to 16 between cards, 24 page gutters.
- Elevation: cards are flat with a stroke. Only dialogs and flyouts cast shadows.

### Icons

Segoe Fluent Icons glyphs, 16 px in controls and navigation, 20 px in settings rows. Never emoji.

| Use | Glyph | Use | Glyph |
| --- | --- | --- | --- |
| Home | `E80F` | Explorer (treemap) | `F0E2` |
| Add | `E710` | Export | `EDE1` |
| Previous / next page | `E76B` / `E76C` | Theme | `E790` |
| Accent color | `E771` | Index database | `E8B7` |
| Privacy | `E72E` | Info | `E946` |
| Duplicates | `E8C8` | Compare | `E8AB` |
| Cleanup (quarantine) | `E7B8` | Diagnostics | `E7BA` |
| Settings | `E713` | Search | `E721` |
| Up | `E74A` | Rescan | `E72C` |
| Pause | `E769` | Stop | `E71A` |
| Open in Explorer | `E838` | Copy path | `E8C8` |
| Properties | `E946` | Shield (elevation) | `EA18` |
| Restore | `E777` | Recycle Bin | `E74D` |

Glyphs in use were verified in rendered screenshots (phase 2). Verify any new glyph the same way; replace any that render as boxes.

## Components

Implemented as styles over the Fluent theme's controls, not new control types, unless noted.

Style keys in `Themes/Controls.xaml`: `TitleText`, `SubtitleText`, `BodyStrongText`, `BodyText`, `CaptionText`, `MonoText`, `FieldLabel`, `Icon`, `MetricValue`, `Card`, `CardHeader`, `Callout`, `CautionCallout`, `Badge`, `SubtleButton`, `IconButton`, `NavItem`, `SegmentTrack` with `Segment`, `Switch`, `ListGrid`. Accent buttons use Fluent's `AccentButtonStyle`.

- **Buttons:** Accent (one per view, never on an unreviewed destructive step), Standard, Subtle, Icon-only (requires `AutomationProperties.Name`).
- **Filter chip:** `ToggleButton` style, 28 px pill. Active chips show a remove glyph.
- **Segmented control:** `ListBox` or grouped `RadioButton` style with a sunken track. Used for Files/Folders/Types, Type/Age, Logical/Allocated, hash algorithm, list filters.
- **Badge:** small rounded label. Snapshot state: Complete (success), Scanning (accent), Partial and Stale (caution), Cancelled (neutral), Failed (critical). Evidence: Verified (success), Sample match, Name only, Hard link (neutral), Suggested keeper (accent).
- **Callout:** soft fill with an icon. Info (accent), caution, critical, success. Never a colored left border.
- **Usage bar:** 6 px track. A stacked 10 px variant shows a volume by category or age, with a hatched "Not attributed" segment and the free-space remainder.
- **Settings card:** icon, title, description, trailing control, 64 px minimum height. Grouped cards share one border.
- **Treemap (custom `FrameworkElement`):** squarified, two levels. Top-level folders are containers with a 20 px header (name and size). Children are colored by category or age; the selected tile gets a 2 px accent ring. Labels appear when a tile is at least 70×36 px. Hover tooltip: name, size, category or age. Click selects; double-click or Enter opens a folder. Arrow keys move between tiles and Alt+Up goes up a level.
- **Dialog:** ContentDialog-style owned window: title, short explanation, body, footer with Accent confirm on the right.

## Screens

Each item lists what the screen shows and where the data comes from. "Data" notes name the queries and records behind each screen.

### First run

The empty state when the index has no snapshots: headline, privacy line, drive cards (radio), Folder or share, Administrator scan toggle, Scan button with the F5 hint, three short steps (Scan, Explore, Review). Data: drive enumeration that already exists.

### Home

- Scan-in-progress card: target, engine badge (Raw MFT or Directory), worker badge (read-only, elevated or not), progress, files, logical bytes, elapsed time, diagnostics, Pause, and Cancel (Esc). The progress denominator is the MFT record count across both raw passes (`ScanProgress`); the directory engine shows an indeterminate bar.
- Drive and root cards: capacity bar (caution over 85%), file system, available engine, last snapshot state. Add folder or share.
- Options: Administrator scan, Prefer raw MFT, exclusions, profile picker. Scan button labelled with the number of selected roots.
- Recent snapshots table: id, roots, state badge, time, files, logical size. Clicking a row opens it in Explorer.

### Explorer

- Header: breadcrumb (current root to parent), size and file count, snapshot picker with state badge, Export, Rescan (accent).
- Volume summary card: volume name, used/free/total, stacked bar by category or age with legend, files, logical, allocated, engine and duration.
- Space map: Up, Type/Age switch, Logical/Allocated switch, nested treemap.
- List card: Files/Folders/Types switch, filter field, chips (minimum size, extension, modified, hidden), top 100 recursive, columns Name (swatch), Size (inline bar), Allocated, Modified, Folder (mono).
- Inspector: icon tile, name, type, size and share of used space, allocation callout for sparse or compressed files, folder path, allocated size, modified time, attributes, file ID, Explorer / Copy path / Properties, Review for cleanup, duplicate status for the folder.
- **Data:** per-folder category and age byte totals, the extension-to-category mapping, the selected entry's attribute names, and the "Not attributed" figure (drive used space minus snapshot allocation).

### Duplicates

- Header: scope picker, hash algorithm (SHA-256, SHA-1, MD5), Analyze or Re-analyze.
- Pipeline card: Same size, Sample match, Full hash verified (files and groups), Reclaimable (verified only). Side facts: name-only matches, hard-link aliases collapsed. **Data:** counts from the last `DuplicateRun` recorded in `duplicate_runs`.
- Groups list: Verified/Name only switch, sort, paging. Each group card: swatch, name, copies and size, evidence badge, hash prefix, reclaimable bytes. Rows: Keep radio, Remove check box, path, modified time, note (Suggested keeper, Oldest copy, Alias of same file). Hard-link aliases cannot be selected.
- Review tray: selected bytes and file and group counts, preferred keeper folder, Review N removals (accent). Every group must keep one copy before review opens.

### Cleanup review dialog

Title "Move N files to quarantine?", Ready / Blocked / Disk space freed now (always 0 B until disposal), per-file precheck table (Identity, Metadata, Bytes, Streams, Status), blocked reasons in plain language, quarantine explanation, Back to selection / Cancel / Quarantine N files. **Data:** the read-only `CleanupService.PrecheckAsync`, which runs the same validation as the move without moving anything. The move still revalidates.

### Compare

- Before and After pickers with state badges, Swap, Compare. Caution callout when either snapshot is not Complete.
- Totals: net change, added, removed, grown (bytes and file counts). **Data:** `IndexStore.CompareTotals`.
- "Where it changed": diverging bars by folder (blue for shrink, orange for growth). **Data:** `IndexStore.FolderChanges`, which diffs the materialized `folders` totals of both snapshots one and two levels below the root.
- Changed files: All/Added/Removed/Grown switch, first 250 by size change. The existing `Compare` query, plus a change filter.

### Cleanup

Summary (held files and bytes, restored, Recycle Bin requested, failed), a reminder that held files still use disk space, a journal table with error lines, and a details panel (original path, quarantine path, keeper, journal steps, Restore, Show in Explorer). Journal steps show only states the journal actually recorded.

### Diagnostics

Snapshot picker, coverage summary (files indexed, folders missing, counts by kind), entries with severity and kind, and a panel explaining what missing data affects and the engine used. **Data:** `ScanError.Kind` (AccessDenied, Unavailable, Interrupted, NotTraversed, EngineFallback, Other), set where the error is raised and optional on the wire; rows without a kind are classified from their message.

### Settings

Appearance (theme: system/light/dark; accent note with a link to `ms-settings:colors`; default map coloring), Scanning (Prefer raw MFT, Administrator scan by default, default exclusions, profiles), Duplicates (algorithm, preferred keeper folder), Storage and privacy (index path and size, snapshot count, cleanup journal), About (version, license, no telemetry, no automatic updates). **Data:** a `settings(key, value)` table in the index database.

## Copy

- Plain verbs: Scan, Rescan, Pause, Cancel, Review, Quarantine, Restore.
- State space consequences: "Disk space is held until you dispose of them", "0 B until disposed".
- Never say "delete" for quarantine. Never show a savings figure for name or sample matches.
- Sentence case. No exclamation marks. Numbers use the current culture with binary units (KiB, MiB, GiB, TiB).

## Accessibility

- Every interactive element is a real control with an accessible name. Icon-only buttons set `AutomationProperties.Name`.
- Text contrast is at least 4.5:1 (3:1 at 24 px and above) in both themes. Category and age colors also differ in lightness.
- The treemap is focusable, supports the keyboard navigation described above, and announces the selected tile (name, size, category or age) through `AutomationProperties.ItemStatus`.
- F6 and Shift+F6 cycle the navigation pane, the section content, and title bar search. Shortcuts: F5 scan, Esc cancel, Alt+Up, Ctrl+E export, Ctrl+F search, Ctrl+1 to Ctrl+6 for sections.
- High Contrast: the Fluent theme handles controls; FileViz brushes map to `SystemColors`.
- Per-monitor DPI stays enabled in `app.manifest`.

## Implementation roadmap

Each phase builds, passes `FileViz.slnx` tests, keeps the `--smoke` run green, and is shippable on its own. Logic that can be unit tested lives in `FileViz.Core` or `FileViz.Data`; `FileViz.App` stays thin.

Status: all eight phases are implemented. The sections below record what was built and where it deviates from the plan above.

| Phase | Scope | Main changes | Tests |
| --- | --- | --- | --- |
| 1. Foundations | Fluent theme, tokens, styles, icons | `Application.ThemeMode="System"` (`WPF0001` suppressed in `FileViz.App.csproj` only); Fluent dictionary merged explicitly so styles can derive from it; `Themes/Tokens.xaml`, `Controls.xaml`; `App.SetTheme` | Smoke screenshots in both themes |
| 2. Shell | Title bar, navigation, sections, status bar | `WindowChrome` title bar with system caption buttons; nav pane plus one `DataTemplate` per section; `SessionViewModel` plus section view models; title bar search; Ctrl+F, Ctrl+1 to 6 | Smoke updated; optional per-section captures |
| 3. Home and first run | Drive cards, progress, first run | `ScanProgress` on raw MFT batches (optional on the wire); drive capacity and engine; state badges; first-run layout | Wire compatibility of `ScanProgress` |
| 4. Explorer | Space map, coloring, inspector | `FileCategories`, `AgeBuckets`, `Composition`, `MapNode`, `TreemapLayout` in Core; nine category and five age columns on `folders`, built in `BuildComposition` after `RebuildFolders`; `SpaceMap`; nested treemap; usage bar; breadcrumb; inspector | Classification, age buckets, layout proportionality and overlap, composition sums, map shape, legacy migration |
| 5. Duplicates and review | Pipeline, group cards, precheck | `DuplicateRun` in `duplicate_runs`; group cards with Keep/Remove persisted across pages; `CleanupService.PrecheckAsync`; `ReviewWindow` | Precheck outcomes and agreement with the move; run counts |
| 6. Compare, Cleanup, Diagnostics | Remaining sections | `CompareTotals`, `FolderChanges`, change filter; cleanup details; `ScanError.Kind` and `DiagnosticKinds` | Totals and rollup agree with the file diff; kinds and legacy classification |
| 7. Settings | Persistence and defaults | `settings` table; theme, map coloring, scan and duplicate defaults; profile deletion | Settings and profile round-trip |
| 8. Polish and docs | Keyboard, accessibility, docs | F6 and Shift+F6 pane cycling; High Contrast drawing in the treemap and usage bar; README screenshots refreshed from the smoke run; user guide, architecture, validation, and performance updated | Smoke in both themes with section captures; manual checklist in `validation.md` |

### Implementation notes

- **Theme:** `App.xaml` sets `ThemeMode="System"` and merges `PresentationFramework.Fluent;component/Themes/Fluent.xaml` first, then `Tokens.xaml` and `Controls.xaml`. Mica comes from the Fluent window style.
- **Title bar:** `WindowChrome` with `GlassFrameThickness="-1"` and `UseAeroCaptionButtons="True"`. Content draws into the 48 px caption over Mica and Windows still draws the caption buttons, so Snap Layouts and dark captions need no custom hit-testing. Only the search box opts into hit testing. Keep the right 150 px of the title bar free for the caption buttons.
- **View models:** `SessionViewModel` owns the index, snapshot history, selected snapshot and root, busy state, cancellation and pause, and status. Sections implement `ISnapshotSection` (`Reset`, `HistoryChanged`, `RefreshAsync`) and register with the session. `ShellViewModel` owns navigation (`NavItem`), title bar search, and shortcuts. `SettingsViewModel` loads saved preferences at startup and applies them to the sections.
- **Lists:** `ListGrid` replaces Fluent's `DataGridCell` template to center content vertically and re-applies Fluent's selection and focus visuals.
- **Space map:** two levels only. Unlisted bytes inside a nested folder stay as container background so listed children keep true proportions. Selection is announced through `AutomationProperties.ItemStatus` instead of a per-tile automation peer.
- **Deviations from the screen specs:** the Duplicates evidence switch (verified / name only) became a separate **Find name matches** action, because a run stores one kind of evidence; hash prefixes are not shown on group cards (the duplicates table does not store them); the Cleanup journal panel shows the recorded state and time, not a step timeline, because the journal stores only the latest state; Diagnostics shows general engine guidance, because the engine used is not stored per snapshot.
- **Smoke run:** forces light, then dark, and captures over a solid background because Mica does not appear in `RenderTargetBitmap`. `FILEVIZ_SMOKE_SECTIONS=1` also captures the first-run layout, every section in both themes, age coloring, a duplicate selection, the review window, and a two-snapshot comparison.

### Risks

- `ThemeMode` is marked experimental (`WPF0001`). The suppression is scoped to the app project; revisit it on each .NET release. Fallback: keep the token dictionaries and style controls by hand.
- Composition totals lengthen snapshot finish time. Measured on the 1M synthetic dataset: finalize 130.79 s against 124.20 s before (+5.3%), with memory and query targets still met. The 10M dataset has not been re-measured. See `performance.md`.
- The review window prechecks files one at a time; very large selections take proportionally longer before Quarantine is enabled.
