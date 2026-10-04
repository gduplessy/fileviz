# Architecture

The WPF shell owns SQLite writes and paged views. Isolated workers enumerate or hash files through authenticated local named pipes. Scan engines emit bounded batches and diagnostics. The raw NTFS engine is read-only and falls back to native directory enumeration when unsupported or inconsistent.

File identities and path entries are separate concepts: hard links are multiple names for one allocation. Scan snapshots are observations of a changing filesystem, not atomic backups. Cleanup performs fresh verification independently of cached analysis.
