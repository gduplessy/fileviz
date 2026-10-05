<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/readme-banner-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="docs/images/readme-banner-light.svg">
    <img src="docs/images/readme-banner-light.svg" width="1200" alt="FileViz: see where your space goes. Native Windows disk analysis and duplicate review.">
  </picture>
</p>

<p align="center">
  <a href="https://github.com/gduplessy/fileviz/releases"><img src="https://img.shields.io/github/v/release/gduplessy/fileviz?include_prereleases&amp;style=flat-square&amp;logo=github&amp;label=release" alt="Latest preview release"></a>
  <a href="https://github.com/gduplessy/fileviz/actions/workflows/ci.yml"><img src="https://github.com/gduplessy/fileviz/actions/workflows/ci.yml/badge.svg?branch=main" alt="Windows build and tests"></a>
  <a href="docs/setup.md"><img src="https://img.shields.io/badge/Windows-11%20x64-0078D4?style=flat-square" alt="Windows 11 x64"></a>
  <a href="global.json"><img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&amp;logo=dotnet" alt=".NET 10"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--only-2b8a7a?style=flat-square" alt="GPL-3.0-only license"></a>
</p>

<p align="center">
  <strong>Find large files. Understand each drive. Review duplicates before cleanup.</strong><br>
  A native Windows desktop app built with C#, WPF, and SQLite. Your scan data stays on your PC.
</p>

<p align="center">
  <a href="#download">Download</a> ·
  <a href="#see-it-in-action">Screenshots</a> ·
  <a href="docs/user-guide.md">User guide</a> ·
  <a href="docs/performance.md">Benchmarks</a> ·
  <a href="CONTRIBUTING.md">Contribute</a>
</p>

> [!IMPORTANT]
> **Preview software.** Raw NTFS fixture parity passes, but the 2× MFT speed target is not yet met. Combined UI/worker memory on real million-file volumes and the full provider matrix remain unverified. Read the [performance report](docs/performance.md) and [validation matrix](docs/validation.md).

## Download

<p>
  <a href="https://github.com/gduplessy/fileviz/releases/download/v0.1.1/FileViz-0.1.1-win-x64-setup.exe"><img src="https://img.shields.io/badge/Download-Windows%20installer-1f6f8b?style=for-the-badge&amp;logo=github" alt="Download Windows installer"></a>
  <a href="https://github.com/gduplessy/fileviz/releases/download/v0.1.1/FileViz-0.1.1-win-x64-portable.zip"><img src="https://img.shields.io/badge/Download-Portable%20ZIP-2b8a7a?style=for-the-badge&amp;logo=github" alt="Download portable ZIP"></a>
</p>

| Package | Choose it when | How to run |
| --- | --- | --- |
| **Per-user installer** | You want Start menu integration and an uninstaller. | Run the setup executable. No administrator access is needed to install. |
| **Portable ZIP** | You want to extract and run without installing. | Extract the **entire** archive, then launch `FileViz.exe`. Keep its `worker` folder beside it. |

Both packages bundle the Windows x64 runtime; you do not need to install .NET. [All releases](https://github.com/gduplessy/fileviz/releases) · [v0.1.1 SHA-256 checksums](https://github.com/gduplessy/fileviz/releases/download/v0.1.1/SHA256SUMS.txt)

> [!NOTE]
> Packages are currently unsigned. Windows SmartScreen may prompt before launch. Verify the checksum against the file attached to the same release.

## See it in action

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/desktop-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/desktop.png">
  <img src="docs/images/desktop.png" alt="FileViz Explorer: Windows 11 window with navigation pane, usage bar by file type, nested space map, inspector, and largest-file list" width="1400">
</picture>

*Actual desktop, disposable sample data. The screenshot follows your GitHub color theme.*

> [!NOTE]
> These screenshots show the Windows 11 redesign on `main`, which ships in the next preview. The v0.1.1 packages above use the earlier single-window layout.

| Color by age | Duplicate groups |
| --- | --- |
| <picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/explorer-age-dark.png"><img src="docs/images/explorer-age.png" alt="Space map and usage bar colored by last-modified age, older shown darker"></picture> | <img src="docs/images/duplicates.png" alt="Duplicate pipeline counts and a verified group with Keep and Remove choices"> |
| **Review before quarantine** | **First run** |
| <picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/review-dark.png"><img src="docs/images/review.png" alt="Review window with identity, metadata, byte, and stream checks per file"></picture> | <picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/first-run-dark.png"><img src="docs/images/first-run.png" alt="First-run screen with drive selection and a Scan button"></picture> |

## What you can do

| Explore your storage | Find duplicates | Review cleanup |
| --- | --- | --- |
| Drive capacity, used/free space, and space not attributed to files | Size → sample → full-hash pipeline with counts at each stage | Precheck of identity, metadata, bytes, and named streams for every file |
| Nested space map colored by file type or by age | SHA-256 by default; SHA-1 or MD5 options | Only files that pass are quarantined; each move revalidates |
| Inspector with allocation notes for sparse, compressed, and placeholder files | Explicit Keep and Remove per copy; a keeper is never removed | Same-volume quarantine with an action journal |
| Largest files, folders, and file types; logical or allocated size | Hard links collapsed as aliases; name matches never count as savings | Restore without overwriting destinations |
| Filters, exclusions, saved profiles, title-bar search | Preferred-folder keeper suggestions | Explicit Windows Recycle Bin requests |

- **Scan your scope:** one drive, selected folders, multiple roots, or UNC shares. Hidden/system entries are included; reparse children and cloud placeholders are skipped.
- **Elevate when needed:** request UAC for a read-only scan worker, or restart the whole application as administrator. Complete supported local NTFS volumes can use the raw MFT engine; structural failures discard raw results and visibly fall back to directory enumeration.
- **Keep useful history:** reopen SQLite snapshots, compare totals and the folders that changed most, export CSV/JSON, and see coverage gaps by kind. Partial, cancelled, and stale results remain labelled.
- **Feel at home on Windows 11:** Fluent controls, Mica, your Windows accent color, light/dark/High Contrast themes, Snap Layouts, and full keyboard use (Ctrl+F, Ctrl+1 to 6, F6, Alt+Up).
- **Stay in control:** pause at safe batch boundaries, cancel workers, or open selected entries in File Explorer and inspect properties.

> [!TIP]
> Start with a drive or folder scan, inspect the largest files, then run content duplicate analysis only where needed. Hashing reads file contents and adds I/O.

> [!WARNING]
> Name matches and sampled content never establish duplicate equivalence. Quarantine retains disk space; Recycle Bin operations do not immediately reclaim it. Cleanup requires explicit review, and duplicate removal requires fresh byte/stream verification with a surviving keeper.

## Measured performance

| Synthetic SQLite dataset | Peak index-process working set | Cached file query median / max |
| --- | --- | --- |
| 1 million files, interleaved (with type and age totals) | 187.16 MiB | 1.36 / 12.31 ms |
| 10 million files, directory-clustered | 192.59 MiB | 5.76 / 86.67 ms |

These measure the **index component**, not live filesystem throughput or combined UI/worker memory. Dataset orders differ and are not direct scaling comparisons. The ten-million figures predate the type and age totals. The raw engine was slower on the small warm NTFS fixture. [Hardware, timings, unmet targets, and reproduction commands](docs/performance.md) · [Machine-readable evidence](docs/evidence/)

## Build from source

<details>
<summary><strong>Windows development setup</strong></summary>

Use the .NET SDK pinned in `global.json`. The bootstrap installs it under ignored `.tools/dotnet`; the wrapper selects it even when the global command resolves to .NET 6.

```powershell
./scripts/bootstrap.ps1
./scripts/dotnet.ps1 restore FileViz.slnx --locked-mode
./scripts/dotnet.ps1 build FileViz.slnx -c Release --no-restore
./scripts/dotnet.ps1 test FileViz.slnx -c Release --no-build
./scripts/run.ps1
```

See [setup](docs/setup.md), [architecture](docs/architecture.md), and the [release process](docs/release.md). Parser and cleanup changes require disposable fixtures and relevant correctness tests.

</details>

## Documentation and community

| Use FileViz | Work on FileViz |
| --- | --- |
| [User guide](docs/user-guide.md) | [Development setup](docs/setup.md) |
| [Performance report](docs/performance.md) | [Architecture](docs/architecture.md) |
| [Validation coverage](docs/validation.md) | [Contributing](CONTRIBUTING.md) |
| [Changelog](CHANGELOG.md) | [Design system](docs/design.md) |
| [Release downloads](https://github.com/gduplessy/fileviz/releases) | [Report a bug](https://github.com/gduplessy/fileviz/issues/new) |
| | [Report a security issue privately](SECURITY.md) |

**Local by default:** snapshots, settings, and journals live in `%LOCALAPPDATA%/FileViz`. No telemetry, automatic deletion, hard-link replacement, automatic updates, drivers, or remote storage. Scanning and hashing do not modify files.

**GPL-3.0-only:** see [LICENSE](LICENSE). Full dependency licenses and [third-party notices](THIRD-PARTY-NOTICES.md) ship with both packages. The README banners are original SVG artwork included in this repository.
