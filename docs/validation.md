# Validation matrix

Claims below distinguish automated fixtures, a rendered live desktop, and unverified integration coverage. Open-source release packages are preview quality until the full volume/provider matrix is exercised.

| Area | Evidence | Remaining coverage |
| --- | --- | --- |
| Parser | Fixups, sequence numbers, fragmented/signed/sparse runlists, attribute-list bounds, resident allocation, root filename, 5,000 deterministic malformed record mutations | Large fragmented physical MFT, additional NTFS 3.0 images, deleted/reused records under concurrent changes, broader fuzz corpus |
| Engine parity | Disposable NTFS VHD, 10,220 matching owned entries; 100 aliases inducing extension records, ADS, resident/compressed/sparse data, Unicode/long paths, junction | Physical fragmented-MFT reference fixture; concurrent changes |
| Directory/index | Metadata round-trip, exclusions, overlapping roots, root-scoped totals/query/export, snapshots, indexed primary view, canonical alias identity checks | ReFS, FAT/exFAT/USB and mounted-volume provider matrix |
| Worker | Authenticated named pipe, launched PID checks, metadata request, rejection of unknown operation, actual local hash-worker exit within two seconds | Standard-UI/elevated-worker cancellation timings, secure-desktop UAC cancellation, deliberately stalled SMB/provider read, crash injection |
| Desktop | Visible WPF smoke in light and dark themes: scan, SHA-256 duplicate pair, no errors. With `FILEVIZ_SMOKE_SECTIONS=1`: first-run layout, every section, age coloring, duplicate selection, review-window precheck, and a two-snapshot comparison, each reviewed as rendered screenshots | Manual checklist below; captures are offscreen, so Mica and Snap Layouts need a live check |
| Index composition | Category and age totals equal file sums, two-level space map shape, legacy database migration, compare totals and folder rollup agreeing with the file diff, diagnostic kinds and legacy classification, settings and profiles round-trip | Real-volume finish times with composition totals |
| Duplicates | Name candidates have no savings; samples never establish equivalence; staged full hashes, overlapping history, hard-link alias handling, changed metadata/identity rejection | Full multi-drive provider matrix and hash cache performance |
| Cleanup precheck | Matching copies pass all four checks and agree with the move; differing bytes, changed metadata, and differing named streams fail only their own check; manual selections skip content checks | Precheck of very large selections (sequential) |
| Cleanup | Fresh byte mismatches, changed metadata, differing ADS, protected paths, quarantine and no-overwrite restore, journal reconciliation | Recycle Bin unavailable/oversized-file paths, interrupted OS recycle request, network and cross-user UAC provider cases |
| Performance | One- and ten-million-entry synthetic index memory/query targets passed | Combined UI/worker real-volume budgets; raw 2x target currently unmet |
| Packages | Self-contained ZIP and per-user installer: 303 fixture files, 2 duplicate rows, zero errors with SDK paths removed; install/uninstall exit 0; checksums verified | Authenticode signing; ARM64; other Windows versions |

## Manual UI checklist

Run before a release on a real window, not the smoke captures:

- Light, dark, and a High Contrast theme: text, badges, treemap outlines, and usage bar stay legible; switching in Settings applies immediately.
- Mica visible behind the navigation pane and title bar; hovering the maximize button shows Snap Layouts; the title bar drags and double-click maximizes.
- Keyboard only: Tab reaches every control in each section; F6 and Shift+F6 cycle navigation, content, and search; Ctrl+1 to Ctrl+6 switch sections; arrow keys and Enter work in the space map; Esc cancels a scan.
- Narrator: navigation items, icon-only buttons, the space map selection, and review-window check states are announced.
- 100%, 150%, and 200% display scaling, and a window moved between monitors with different scaling.

Only disposable fixtures are used for cleanup tests. User files are never automatically selected or modified during validation. Structural raw failures discard raw entries and restart directory enumeration; incomplete coverage remains visible. Quarantine and Recycle Bin requests retain or dispose data separately from potential-savings estimates.
Machine-readable reports live in [evidence](evidence/). The [final package report](evidence/packages-release.json) validates the exact downloaded `v0.1.0` release assets, including the root/treemap assertion. Package validation is reproducible with scripts/validate-package.ps1; it uses only workspace-owned disposable files and refuses to overwrite an existing user installation.

The [0.1.1 package report](evidence/packages-0.1.1.json) verifies the downloaded installer and ZIP, executable version/source commit, and the refreshed packaged README. Both visible smokes passed with 303 fixture files, two duplicate rows, and no diagnostics; installation and uninstallation exited successfully.

The [0.2.0 local package report](evidence/packages-0.2.0-local.json) covers the locally built installer and ZIP of the redesign: checksums verified, both visible smokes passed with 303 fixture files, two duplicate rows, and no diagnostics, and installation and uninstallation exited successfully.

The [0.2.0 release report](evidence/packages-0.2.0.json) verifies the downloaded release installer and ZIP: checksums match the attached `SHA256SUMS.txt`, the executable reports version 0.2.0 from the tagged commit, both visible smokes passed with 303 fixture files, two duplicate rows, and no diagnostics, and installation and uninstallation exited successfully.

The [0.2.1 release report](evidence/packages-0.2.1.json) verifies both downloaded asset checksums, the portable executable's tagged source version, and a self-contained desktop smoke with SDK paths removed: 304 files including two hard-link aliases, two duplicate rows, zero diagnostics, and the hard-link/finalization progress assertions. All 53 regression tests passed locally and in Windows CI. The installer was downloaded and checksum verified but not installed or uninstalled during this investigation, to preserve the existing installation and its running scan.

The [0.2.3 release report](evidence/packages-0.2.3.json) verifies the exact downloaded ZIP and installer checksums and tagged source version. The visible portable smoke passed with SDK paths removed: 304 disposable files, two duplicate rows, zero diagnostics, and a saved-inventory rebuild that preserves interrupted coverage and excludes files created after scanning. All 59 regression tests passed in Windows release CI. The per-user installer upgrade exited successfully and its installed version matched the tag; uninstall was not tested in this run. Real-drive recovery reports remain local and are not included in public evidence.
