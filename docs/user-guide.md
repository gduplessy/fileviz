# Using FileViz

FileViz has six sections in the left pane (Home, Explorer, Duplicates, Compare, Cleanup, Diagnostics) plus Settings. Sections that need a snapshot are disabled until your first scan.

## Scan (Home)

On first launch, Home shows the drives on this PC. Select one or more, or choose **Folder or share**, then **Scan**. Later, Home shows drive cards with used space, file system, the engine that applies, and the state of each drive's latest snapshot, plus your recent snapshots (double-click or Enter opens one in Explorer).

Overlapping roots are collapsed. Hidden and system entries are included. Exclusions accept an absolute folder prefix or a filename wildcard; profiles save roots, exclusions, and the preferred engine. Default exclusions and engine choices live in **Settings**.

FileViz runs unelevated by default. Turn on **Administrator scan** to launch a read-only worker through UAC. Raw MFT scanning is used for complete local NTFS volumes; selected subfolders, unsupported formats, and shares use directory enumeration. A raw failure discards its entries and restarts through the directory engine, recorded under **Diagnostics** as an engine fallback. Worker elevation must use the same Windows identity. If UAC requires another administrator account, run the entire application as that account instead. Mapped shares are resolved to UNC paths before elevation. Existing Windows credentials are used; authentication errors do not change network configuration.

While a scan runs, Home shows the target, engine, worker, files, logical bytes, and elapsed time. Raw MFT scans also show records read across both passes. **Pause** stops consumption at a metadata batch boundary. **Cancel** (Esc) closes the pipe and terminates the read-only worker. Partial, failed, cancelled, interrupted, and stale snapshots remain clearly labelled. Reparse directories and cloud placeholders are not traversed or hydrated.

## Explore

**Explorer** opens the selected snapshot and root. The breadcrumb shows where you are; Up (Alt+Up) or a breadcrumb step goes back.

- **Volume card:** used and free space for drive roots, a usage bar by file type or age with a legend, and totals for files, logical size, allocated size, and diagnostics. A hatched **Not attributed** segment is drive usage that no scanned file accounts for (file system metadata, named streams, sharing, and coverage gaps).
- **Space map:** the largest folders and files at this level. Large folders show their own largest children inside them; space in items too small to list stays as background, so tiles keep true proportions. **Color: Type** colors by file category (by extension); **Age** colors by last-modified time relative to the scan, with older shown darker. **Size: Logical / Allocated** switches the measure. Click selects; double-click or Enter opens a folder; arrow keys move between tiles. Your coloring choice is remembered.
- **Inspector:** details for the selected tile or file: size and share of the root, allocation notes for sparse, compressed, and placeholder files, folder, attributes, and file ID, with Explorer, Copy path, Properties, and **Review for cleanup**. With nothing selected it describes the folder being shown.
- **Lists:** **Files** (largest files, with filters for name or path, extension, minimum size, modified date, and hidden), **Folders**, and **Types**. Lists use bounded pages or top-100 rankings rather than loading the whole inventory.

Logical and reported allocated sizes differ for sparse, compressed, resident, and hard-linked files. Unknown allocation is labelled; allocation is counted once per known identity in totals. Snapshots taken before type and age totals existed show a note asking for a rescan before the map and bar can be colored.

**Export** (Ctrl+E) writes the complete snapshot as CSV or JSON to a new file. The title bar search (Ctrl+F) filters the selected snapshot by name or path and opens the results in Explorer.

## Duplicates

Choose **Drive or folder** directly on the Duplicates page. The selector lists saved roots and scan IDs, opens the matching inventory, and keeps you on Duplicates. It follows Explorer and explicitly reopened historical scans; new choices normally use the latest saved scan for that root. Scan an additional drive in Home to add its inventory here. **Compare across selected drives** reveals checkboxes beside the selector and uses the latest saved scan for each checked root. Finish or cancel an active analysis before changing scope. Existing matches are the last completed analysis until you analyze the new selection.

**Analyze content** filters by size, compares samples, then hashes entire main streams. SHA-256 is the default; SHA-1 and MD5 are compatibility options. The pipeline card shows how many files passed each stage and the reclaimable size of verified groups only. **Find name matches** is fast, but name matches never imply savings and cannot be selected for removal. Known hard links are collapsed as aliases, not independent copies, and counted separately.

Each group card shows its evidence, size, and copies. Choose one copy to **Keep** and tick **Remove** on the others; a keeper can never be removed, and choices are kept as you page through groups. A preferred keeper folder influences suggested keepers on the next analysis; suggestions never select removals.

While analysis runs, the activity card shows preparation, sampling, full hashing, and result grouping. File counts and percentages apply to the current stage; database preparation is indeterminate until its candidate count is known. Bytes show actual reads, including sampled blocks; cached hashes count separately. The current file, elapsed time, and age of the last work update distinguish active reads from waiting for a provider or database operation. **Cancel analysis** stops the worker and interrupts SQLite; the last completed result remains available. Completion, cancellation, and errors appear explicitly on the card and status bar.

## Photos

Open **Photos** (Ctrl+7), choose a saved **Drive or folder**, and click **Analyze photos**. Image dimensions are read on demand in an isolated worker; ordinary drive scans stay metadata-only. JPEG, PNG, GIF, BMP, and TIFF use Windows imaging codecs; WebP, HEIC/HEIF, AVIF, and other listed formats require a compatible installed codec. Unsupported, corrupt, inaccessible, changing, and timed-out images appear under **Show unreadable / unsupported images** and cannot be selected for removal. Reparse points and cloud placeholders are skipped without requesting hydration.

Set **Minimum short edge** (720 pixels by default) and/or **Minimum megapixels**. A photo matches if it falls below either enabled threshold; zero disables a threshold. Short-edge filtering treats portrait and landscape equally. **Apply filters** updates the cached results; name/path filtering narrows them further. GIF/multipage dimensions use the maximum width and height across frames, conservatively avoiding thumbnail-sized first pages. These thresholds measure resolution, not blur, compression artifacts, or whether an image is valuable.

Select a row and **Load preview** or **Show in Explorer** to inspect it. Previews show the first frame and are limited to 512 pixels on the long edge, images up to 25 megapixels, and files up to 64 MiB. Larger originals remain analyzable but should be reviewed in Explorer. EXIF orientation is not applied by the preview; resolution filtering remains orientation-independent.

Tick **Remove** individually or **Select this page**, then **Review removals**. Nothing is selected automatically. Choices persist across 100-row pages, with a limit of 5,000 selections per review; changing scope or filters clears them. Photo cleanup is an explicit manual selection, with fresh identity/timestamp checks rather than duplicate keeper comparisons. Changed files and protected locations are blocked. Same-volume quarantine and journaled restore preserve the originals; the selected byte total is logical size, not guaranteed reclaimed allocation, especially for hard-link aliases.

The activity card shows phase, counts, current file, elapsed time, and the age of the last update. Cancel retains completed metadata and leaves originals untouched. Re-analysis revalidates identity/change metadata before cache reuse. Reopened cached results may be stale or partial; cleanup always checks again.

## Review and quarantine

**Review removals** (or **Review for cleanup** in Explorer) opens the review window. FileViz prechecks every selected file without moving anything: identity, size and timestamps, and for duplicates the bytes and every named stream against the keeper. Files that fail are listed as blocked with the reason and stay in place. **Quarantine** moves only the files that passed, and each move revalidates under read locks. Protected system and application locations, reparse points, and placeholders are blocked.

Files move into a hidden `.FileViz-Quarantine` directory beside their original parent on the same volume. A durable journal records intent, completion, and individual failures. Restore never overwrites an existing path. Restart reconciles interrupted moves conservatively.

## Cleanup

**Cleanup** lists what quarantine holds (files and bytes), with restored, Recycle Bin requested, and failed counts. Select an entry to see its original and quarantine paths and file ID, then **Restore**, **Show in Explorer**, or **Send to Recycle Bin**.

Quarantine retains disk space. The Windows Recycle Bin request displays Windows dialogs; unavailable or oversize recycling is never silently changed to permanent deletion. Cancelling retains the quarantined file. A completed Windows disposal request is logged without claiming that the OS used a particular disposal method. Emptying the bin is a separate Windows action.

## Compare

**Compare** needs two snapshots of the same roots. It totals every added, removed, grown, and shrunk file, shows the folders one and two levels below the root whose size changed most, and lists the first 250 file changes by size, filterable by kind. A caution appears when either snapshot is not complete, because unread folders can make files look added or removed.

## Diagnostics

**Diagnostics** summarizes coverage for the selected snapshot. Each entry has a kind: **Access denied** and **Path unavailable** are coverage gaps (those folders are missing from totals, duplicates, and comparisons; FileViz never estimates their size); **Interrupted** and **Engine fallback** are warnings; **Not traversed** marks items listed by design but not followed. **Copy report** copies the list.

## Settings and keyboard

Settings holds the app theme (follows Windows by default), the default map coloring, scan defaults (prefer raw MFT, administrator scan, default exclusions), saved profiles, the default hash algorithm and preferred keeper folder, and storage details. Settings are saved in the index database. The accent color follows your Windows accent color.

Keyboard: F5 scan, Esc cancel, Alt+Up up one level, Ctrl+E export, Ctrl+F search, Ctrl+1 to Ctrl+7 switch sections, F6 and Shift+F6 move between the navigation pane, the section, and search. Controls have accessible names, the space map announces the selected tile, and High Contrast themes are followed. Per-monitor DPI scaling is enabled.

## Privacy and storage

Snapshots, settings, and journals live in `%LOCALAPPDATA%/FileViz/index.db`. Reports contain paths and metadata: review them before sharing. No telemetry, drivers, automatic deletion, automatic updates, or remote storage are used. Removing a snapshot database does not restore quarantine files; retain the journal while cleanup actions remain outstanding.

## Recover an interrupted snapshot

Select an interrupted, cancelled, or failed snapshot in Home's history and choose **Rebuild saved views**. This computes indexes and totals from its saved entries; it does not enumerate folders, refresh file metadata, or establish complete coverage. The snapshot retains its previous coverage state. Cancel can interrupt an executing finalization query; a later rebuild starts the views again without duplicating entries.

For recovery from a local database copy, launch `FileViz.exe --database "C:\Recovery\index.db" --rebuild-snapshot 1`. Keep the original database untouched and copy its matching `index.db-wal` and `index.db-shm` together while FileViz is closed; an online database requires SQLite's backup API instead. Close other FileViz instances before launching. The override writes only to the selected database; it does not migrate it to the default location. File actions always refer to the original scanned paths, so retain the incomplete/stale coverage labels when reviewing results.

The diagnostic benchmark tool also supports `--rebuild DATABASE OUTPUT` for the newest saved interrupted snapshot. It modifies the selected database, records stage timings, and performs no filesystem scan. Use a recovery copy and an empty report directory.
Measure saved file, folder ranking, and two-level map queries with the diagnostic tool's "--views DATABASE OUTPUT" mode. It prepares missing folder navigation indexes in the selected database and reports five iterations; it reads no file content or live directory metadata.
