# Performance and validation

Measured on 2026-10-04: Windows 11 build 26100, Intel Core i7-5820K (6 cores / 12 logical processors), 32 GiB RAM, Samsung 870 EVO 2 TB SATA SSD hosting the workspace and disposable VHD. SDK 10.0.401; runtime 10.0.12. These are single-machine preview measurements, not universal throughput claims.

## SQLite indexing

The synthetic generator creates 1,000 directories and files with deterministic sizes, identities, and timestamps. It does not create millions of physical files. Transactions contain at most 256 entries. SQLite uses a 32 MiB connection cache, file-backed temporaries, and deferred per-snapshot query indexes. Folder, extension, and root totals are materialized after ingestion. Memory is sampled every 50 ms.

| Dataset | Ordering | Ingest | Finalize | Peak process working set | Cached file query median / max |
| --- | --- | --- | --- | --- | --- |
| 1,000,000 files | Interleaved across 1,000 folders | 505.11 s | 124.20 s | 180.60 MiB | 1.12 / 14.70 ms |
| 10,000,000 files | Directory-clustered | 246.14 s | 1,155.75 s | 192.59 MiB | 5.76 / 86.67 ms |

The million-entry index used 834,646,016 database bytes; the ten-million-entry index used 8,377,925,632 bytes. The ten-million run completed in 23.37 minutes, with most time spent building indexes and summaries. Interleaved insertion is materially slower than directory-clustered insertion because it scatters primary-index writes. The directory engine performs depth-first traversal. Dataset ordering is stated explicitly; timings across different orders are not direct scaling comparisons.

**With composition totals (redesign, same machine, 2026-10-04).** Finishing a snapshot now also builds per-folder file-type and age totals. The one-million interleaved run finalized in 130.79 s, against 124.20 s before (+6.6 s, +5.3%). Peak working set was 187.16 MiB, the cached file query median/max 1.36 / 12.31 ms, and the database 835,932,160 bytes (+1.3 MB). Ingest took 187.12 s against 505.11 s earlier; ingestion code did not change, so that gap reflects run conditions and is not claimed as an improvement. The ten-million directory-clustered run finalized in 1,174.84 s against 1,155.75 s before (+19.1 s, +1.7%), with ingest 244.63 s (246.14 s before), peak working set 189.15 MiB, cached query median/max 2.50 / 52.98 ms, and 8,390,836,224 database bytes. Evidence: [one million](evidence/index-million-composition.json), [ten million](evidence/index-ten-million-composition.json).

Both index processes met their component memory budgets (512 MiB / 1 GiB) and the 500 ms primary-query budget. This is not a combined UI/worker measurement. A separate visible desktop smoke run indexed 303 files / 40 MiB and found the expected SHA-256 duplicate pair, with zero diagnostics; its UI process peaked at 169.18 MiB. Combined working sets on real million/ten-million-file volumes remain unverified.

## Hard-link metadata refresh (0.2.1)

On 2026-10-05, a synthetic database with 5,000 independent hard-link groups (10,000 entries) measured the metadata-write phase separately. Before the fix, each refreshed identity rewrote all its aliases in its own transaction, even when metadata already matched. The revised pipeline collects up to 64 worker results before writing, commits them together, and updates only changed rows. No write transaction is held while waiting for filesystem metadata.

| Metadata | Before | After |
| --- | --- | --- |
| Already matches | 1.1878 s | 0.6002 s |
| Changed length and change timestamp | 1.3990 s | 0.5048 s |

These are sequential single runs on the machine above, while another FileViz process was finalizing a real-volume scan. They exclude filesystem reads and do not establish full-drive speedups. Final query indexes and folder/type/age aggregates are unchanged and can take many minutes on large inventories. The UI now reports these stages explicitly, clears stale MFT progress after fallback, and keeps elapsed time accurate beyond an hour. Cancellation during finalization still waits for partial views to be saved. [Evidence](evidence/alias-refresh-0.2.1.json).

Reproduce the revised pipeline with `dotnet run --project tools/FileViz.Benchmarks -c Release -- --aliases 5000 artifacts/benchmarks/new-aliases`. `--unbatched` measures individual transactions using the loaded Data assembly; reproducing the original unconditional updates requires the pre-fix `FileViz.Data.dll` from commit `9491f5e`. The regression suite verifies zero writes for unchanged metadata, all-alias canonicalization, null allocation handling, identity rejection, and rollback of a failed batch.

## Disposable NTFS fixture

A dedicated dynamically expanding 1 GiB VHD was formatted as NTFS after verifying its file-backed disk identity. Its `data` tree contains 10,000 small files, 100 hard-link aliases, alternate streams, resident data, compressed zeros, a sparse file, Unicode names, long paths, and a junction. The image is detached after each run; existing physical disks are never formatted.

Parity passed for 10,220 entries: paths, directory flags, logical/reported allocation, identities, attributes, modified timestamps, and change timestamps. Known hard-link identities are freshly queried once and their metadata is canonicalized, because Windows directory-index timestamps can lag writes through another alias. This is identity-checked normalization, not an ignored comparison.

Separate fresh-process metadata scans on the warm fixture:

| Engine | Entries including filesystem metadata | Time | Peak process working set | Diagnostics |
| --- | --- | --- | --- | --- |
| Native directory | 10,222 | 0.1127 s | 37.59 MiB | 1 protected system-directory diagnostic |
| Raw MFT | 10,251 | 0.2670 s | 41.41 MiB | 0 |

The raw engine includes internal NTFS metadata that ordinary directory enumeration does not expose. Parity compares the owned `data` tree. The raw 2x throughput target was **not met**: its ratio here was 0.422x directory speed. The pipeline parity timings also include SQLite writes and must not be presented as pure engine timings. A large physical fragmented-MFT reference fixture is still needed before claiming raw throughput superiority.

## Reproduce

```powershell
dotnet run --project tools/FileViz.Benchmarks -c Release -- --index 1000000 artifacts/benchmarks/new-million
dotnet run --project tools/FileViz.Benchmarks -c Release -- --index 10000000 artifacts/benchmarks/new-ten-million --clustered
# Administrator: creates and formats a new dedicated VHD only, under workspace artifacts.
./scripts/validate-ntfs.ps1 -FileCount 10000 -ReservedLetters BCDEG
# Recheck an existing dedicated FileViz fixture, without formatting.
./scripts/recheck-ntfs.ps1 -ImagePath artifacts/ntfs-validation-TIMESTAMP/fixture.vhdx -Benchmark tools/FileViz.Benchmarks/bin/Release/net10.0-windows/FileViz.Benchmarks.dll
```

Use a new output directory. Reports are JSON; databases and test images are ignored by Git. The test suite verifies actual local worker termination within two seconds. Disconnected SMB/provider stalls, elevated cancellation timing, cloud placeholders, concurrent live changes, and the complete reference-volume matrix remain unverified; see [validation](validation.md).

## Branching directory finalization (0.2.2)

A real-volume CPU trace exposed repeated scans in folder rollup and file-type/age aggregation. Correlated lookups scanned an unindexed grouped temporary result for each parent, producing quadratic work on wide nested trees. Direct folder totals also repeated inventory queries. Version 0.2.2 builds indexed temporary aggregates, groups direct children once, and uses keyed lookups for each depth. The original million-entry fixture placed only 1,000 folders directly beneath one root and did not expose this shape.

Measured on the machine above on 2026-10-05. The branching fixture creates a root, a branch and leaf directory per branch, and one 17-byte file per leaf, entirely as SQLite metadata.

| Dataset | Version | Finalize | Folder sizes | Type/age totals | Peak working set |
| --- | --- | --- | --- | --- | --- |
| 8,000 files / 16,000 folders | Original | 55.19 s | 31.39 s | 20.04 s | 90.93 MiB |
| 8,000 files / 16,000 folders | Patched | 5.66 s | 1.38 s | 1.49 s | 91.30 MiB |
| 100,000 files / 200,000 folders | Patched | 33.29 s | 11.09 s | 7.88 s | 123.43 MiB |

These are single runs under shared load, not full-drive scan speed guarantees. The small branching fixture improved 9.7x overall; the larger fixture validates a directory count that the old shallow benchmark missed. Index construction and filesystem access still depend on inventory size, storage, permissions, and provider behavior. [Machine-readable evidence](evidence/folder-rollup-0.2.2.json).

Reproduce with `dotnet run --project tools/FileViz.Benchmarks -c Release -- --tree 100000 artifacts/benchmarks/new-tree`. To reproduce the original behavior, use the original 0.2.1 Data assembly with the same generator. Finalization cancellation now interrupts an executing SQLite statement instead of waiting to finish partial views; interrupted metadata can be rebuilt later without rescanning.