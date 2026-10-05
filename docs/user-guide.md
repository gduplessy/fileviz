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

The default scope is the current root; **Compare across selected roots** spans drives and snapshots. **Analyze content** filters by size, compares samples, then hashes entire main streams. SHA-256 is the default; SHA-1 and MD5 are compatibility options. The pipeline card shows how many files passed each stage and the reclaimable size of verified groups only. **Find name matches** is fast, but name matches never imply savings and cannot be selected for removal. Known hard links are collapsed as aliases, not independent copies, and counted separately.

Each group card shows its evidence, size, and copies. Choose one copy to **Keep** and tick **Remove** on the others; a keeper can never be removed, and choices are kept as you page through groups. A preferred keeper folder influences suggested keepers on the next analysis; suggestions never select removals.

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

Keyboard: F5 scan, Esc cancel, Alt+Up up one level, Ctrl+E export, Ctrl+F search, Ctrl+1 to Ctrl+6 switch sections, F6 and Shift+F6 move between the navigation pane, the section, and search. Controls have accessible names, the space map announces the selected tile, and High Contrast themes are followed. Per-monitor DPI scaling is enabled.

## Privacy and storage

Snapshots, settings, and journals live in `%LOCALAPPDATA%/FileViz/index.db`. Reports contain paths and metadata: review them before sharing. No telemetry, drivers, automatic deletion, automatic updates, or remote storage are used. Removing a snapshot database does not restore quarantine files; retain the journal while cleanup actions remain outstanding.
