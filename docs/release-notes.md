# FileViz 0.2.1 preview

A scan progress and hard-link indexing fix for Windows 11 x64.

## Changes

- Large scans now show what happens after enumeration: hard-link discovery/refresh, each query index, folder totals, file-type and age totals, and snapshot saving. Finalization can still take many minutes on multi-million-entry volumes.
- Hard-link metadata writes use transactions of at most 64 entries and skip unchanged metadata. Identity checks, canonicalization across aliases, and unknown allocation are preserved.
- Elapsed time uses a monotonic timer and no longer wraps after an hour. File counts exclude directories; directory fallback resets discarded raw-scan counters and progress.
- Pause applies at metadata batches and finalization step boundaries. Cancellation can still wait while partial snapshot views are saved; this preview does not promise immediate cancellation during finalization.

## Download

- **Installer:** `FileViz-0.2.1-win-x64-setup.exe`, per-user installation.
- **Portable:** `FileViz-0.2.1-win-x64-portable.zip`, extract the whole archive and launch `FileViz.exe`.
- **Verify:** compare the package with the attached `SHA256SUMS.txt`.

Allow a running scan to finish before upgrading. Upgrades preserve local snapshots; installing this patch does not change an already-running process.

## Verification and limits

- Release build: zero warnings/errors; all 53 tests passed.
- Desktop fixture: 304 files including two real hard-link aliases, expected duplicate pair, zero diagnostics. Smoke assertions verify that hard-link discovery and snapshot finalization reach the UI.
- Synthetic 5,000-group hard-link database benchmark: unchanged refresh 1.19 → 0.60 seconds; changed refresh 1.40 → 0.50 seconds. These are single runs under shared load, exclude filesystem reads, and are not end-to-end scan speed claims. [Evidence](https://github.com/gduplessy/fileviz/blob/main/docs/evidence/alias-refresh-0.2.1.json)
- Query-index construction and aggregate generation are unchanged. Raw MFT can still fall back to directory enumeration for unsupported or inconsistent metadata; see Diagnostics after completion.

Packages remain unsigned previews. The raw 2× throughput target and full provider matrix remain unverified or unmet; see [performance](https://github.com/gduplessy/fileviz/blob/main/docs/performance.md) and [validation](https://github.com/gduplessy/fileviz/blob/main/docs/validation.md). No telemetry, automatic updates, or automatic deletion.
