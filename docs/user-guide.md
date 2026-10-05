# Using FileViz

## Scan

Select one or more drives, add a folder/share, or enter additional roots. Overlapping roots are collapsed. Hidden/system entries are included. Exclusions accept an absolute folder prefix or filename wildcard; profiles save roots, exclusions, and the preferred engine.

FileViz runs unelevated by default. Check **Administrator scan** to launch a read-only worker through UAC. Raw MFT scanning is used for complete local NTFS volumes; selected subfolders, unsupported formats, and shares use directory enumeration. A raw failure discards its entries and restarts through the directory engine, with an explanation under **Diagnostics**. Worker elevation must use the same Windows identity. If UAC requires another administrator account, run the entire application as that account instead. Mapped shares are resolved to UNC paths before elevation. Existing Windows credentials are used; authentication errors do not change network configuration.

Pause stops consumption at a metadata batch boundary. Cancel closes the pipe and terminates the read-only worker. Partial, failed, cancelled, interrupted, and stale snapshots remain clearly labelled. Reparse directories and cloud placeholders are not traversed or hydrated.

## Explore

Choose a snapshot and its drive/root in the sidebar. The treemap shows the largest children; click a folder to navigate. **Largest files** returns to the current root ranking. File/folder views use bounded pages or top-100 rankings, rather than loading the entire inventory. Logical and reported allocated sizes differ for sparse, compressed, resident, and hard-linked files. Unknown allocation is labelled; allocation is counted once per known identity in totals.

Apply literal name/path, extension, minimum MiB, modified-date, and hidden-attribute filters. Explorer, Copy path, and Properties operate on selected entries. Export writes the complete snapshot as CSV or JSON to a new file. Snapshot comparisons require matching roots and show the first 250 added, removed, or grown files; partial coverage affects comparisons.

Keyboard: F5 scan, Escape cancel, Alt+Up navigate, Ctrl+E export. Theme toggles light/dark. Controls support keyboard focus, accessible names, and per-monitor DPI scaling.

## Duplicates and cleanup

The default duplicate scope is the current root. Enable selected roots to compare across drives/snapshots. Name candidates never imply savings. Content analysis filters by size, samples content, and then hashes entire main streams. SHA-256 is the default; MD5 and SHA-1 are compatibility options. Sampling alone never forms a verified group. Known hard links are collapsed as aliases, not independent copies.

A preferred folder influences suggested keepers; suggestions do not select removals. Review exact selected paths before quarantine. Duplicate cleanup freshly validates identities, metadata, bytes, and every named stream, holding read locks through the move. Missing/unverifiable streams, changed files, differing content, and a missing keeper block that removal. Protected system/application locations, reparse points, and placeholders are blocked.

Manual file cleanup is also reviewed and freshly revalidated. Files move into a hidden `.FileViz-Quarantine` directory beside their original parent on the same volume. A durable journal records intent, completion, and individual failures. Restore never overwrites an existing path. Restart reconciles interrupted moves conservatively. Keep quarantined files in place until restored or explicitly disposed.

Quarantine retains disk space. The Windows Recycle Bin request displays Windows dialogs; unavailable/oversize recycling is never silently changed to permanent deletion. Cancelling retains the quarantined file. A completed Windows disposal request is logged without claiming that the OS used a particular disposal method. Review Windows prompts. Emptying the bin is a separate Windows action.

## Privacy and storage

Snapshots and journals live in `%LOCALAPPDATA%/FileViz/index.db`. Reports contain paths and metadata: review them before sharing. No telemetry, drivers, automatic deletion, automatic updates, or remote storage are used. Removing a snapshot database does not restore quarantine files; retain the journal while cleanup actions remain outstanding.