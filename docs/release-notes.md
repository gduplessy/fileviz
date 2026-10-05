# FileViz 0.1.0 preview

Windows 11 x64 disk analysis with native directory enumeration, elevated read-only NTFS MFT scanning, SQLite snapshots, treemap navigation, filters, reports, and content duplicate review.

- Per-drive views, selected folder/share roots, exclusions, and saved scan profiles.
- Size filtering, sampling, then full SHA-256 hashes; optional SHA-1, MD5, or name candidates.
- Explicitly reviewed same-volume quarantine, fresh byte and alternate-stream verification, journaled restore, and Windows Recycle Bin requests.
- Self-contained portable ZIP and per-user installer. No runtime installation required.
- Local-only storage; no telemetry, automatic deletion, automatic updates, or drivers.

This is a preview release. Review the published performance report and validation matrix for measured results and remaining coverage. Filesystems can change while scanning; snapshots are observations rather than atomic backups. Raw scanner failures discard their entries and visibly fall back to directory enumeration.

Packages are currently unsigned. Verify SHA256SUMS.txt before use. SmartScreen may display an unsigned-publisher prompt.