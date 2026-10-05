# Performance and validation

Measured on 2026-10-04: Windows 11 build 26100, Intel Core i7-5820K (6 cores / 12 logical processors), 32 GiB RAM, Samsung 870 EVO 2 TB SATA SSD hosting the workspace and disposable VHD. SDK 10.0.401; runtime 10.0.12. These are single-machine preview measurements, not universal throughput claims.

## SQLite indexing

The synthetic generator creates 1,000 directories and files with deterministic sizes, identities, and timestamps. It does not create millions of physical files. Transactions contain at most 256 entries. SQLite uses a 32 MiB connection cache, file-backed temporaries, and deferred per-snapshot query indexes. Folder, extension, and root totals are materialized after ingestion. Memory is sampled every 50 ms.

| Dataset | Ordering | Ingest | Finalize | Peak process working set | Cached file query median / max |
| --- | --- | --- | --- | --- | --- |
| 1,000,000 files | Interleaved across 1,000 folders | 505.11 s | 124.20 s | 180.60 MiB | 1.12 / 14.70 ms |
| 10,000,000 files | Directory-clustered | 246.14 s | 1,155.75 s | 192.59 MiB | 5.76 / 86.67 ms |

The million-entry index used 834,646,016 database bytes; the ten-million-entry index used 8,377,925,632 bytes. The ten-million run completed in 23.37 minutes, with most time spent building indexes and summaries. Interleaved insertion is materially slower than directory-clustered insertion because it scatters primary-index writes. The directory engine performs depth-first traversal. Dataset ordering is stated explicitly; timings across different orders are not direct scaling comparisons.

Both index processes met their component memory budgets (512 MiB / 1 GiB) and the 500 ms primary-query budget. This is not a combined UI/worker measurement. A separate visible desktop smoke run indexed 303 files / 40 MiB and found the expected SHA-256 duplicate pair, with zero diagnostics; its UI process peaked at 169.18 MiB. Combined working sets on real million/ten-million-file volumes remain unverified.

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