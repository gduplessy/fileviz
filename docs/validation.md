# Validation matrix

Claims below distinguish automated fixtures, a rendered live desktop, and unverified integration coverage. Open-source release packages are preview quality until the full volume/provider matrix is exercised.

| Area | Evidence | Remaining coverage |
| --- | --- | --- |
| Parser | Fixups, sequence numbers, fragmented/signed/sparse runlists, attribute-list bounds, resident allocation, root filename, 5,000 deterministic malformed record mutations | Large fragmented physical MFT, additional NTFS 3.0 images, deleted/reused records under concurrent changes, broader fuzz corpus |
| Engine parity | Disposable NTFS VHD, 10,220 matching owned entries; 100 aliases inducing extension records, ADS, resident/compressed/sparse data, Unicode/long paths, junction | Physical fragmented-MFT reference fixture; concurrent changes |
| Directory/index | Metadata round-trip, exclusions, overlapping roots, root-scoped totals/query/export, snapshots, indexed primary view, canonical alias identity checks | ReFS, FAT/exFAT/USB and mounted-volume provider matrix |
| Worker | Authenticated named pipe, launched PID checks, metadata request, rejection of unknown operation, actual local hash-worker exit within two seconds | Standard-UI/elevated-worker cancellation timings, secure-desktop UAC cancellation, deliberately stalled SMB/provider read, crash injection |
| Desktop | Visible WPF scan and SHA-256 duplicate smoke: 303 files, 40 MiB, 2 duplicate rows, no errors; reviewed rendered screenshot | Keyboard/screen-reader acceptance, DPI and dark-theme checks across multiple displays |
| Duplicates | Name candidates have no savings; samples never establish equivalence; staged full hashes, overlapping history, hard-link alias handling, changed metadata/identity rejection | Full multi-drive provider matrix and hash cache performance |
| Cleanup | Fresh byte mismatches, changed metadata, differing ADS, protected paths, quarantine and no-overwrite restore, journal reconciliation | Recycle Bin unavailable/oversized-file paths, interrupted OS recycle request, network and cross-user UAC provider cases |
| Performance | One- and ten-million-entry synthetic index memory/query targets passed | Combined UI/worker real-volume budgets; raw 2x target currently unmet |
| Packages | Self-contained ZIP and per-user installer: 303 fixture files, 2 duplicate rows, zero errors with SDK paths removed; install/uninstall exit 0; checksums verified | Authenticode signing; ARM64; other Windows versions |

Only disposable fixtures are used for cleanup tests. User files are never automatically selected or modified during validation. Structural raw failures discard raw entries and restart directory enumeration; incomplete coverage remains visible. Quarantine and Recycle Bin requests retain or dispose data separately from potential-savings estimates.
Machine-readable reports live in [evidence](evidence/). Package validation is reproducible with scripts/validate-package.ps1; it uses only workspace-owned disposable files and refuses to overwrite an existing user installation.
