# FileViz

A native Windows 11 x64 disk visualizer and duplicate-review application. C# / .NET 10, WPF, SQLite, GPL-3.0-only.

## Download and run

Get the portable ZIP or per-user installer from [GitHub releases](https://github.com/gduplessy/fileviz/releases). Both bundle the Windows x64 runtime. For portable use, extract the entire ZIP and launch `FileViz.exe`; keep the `worker` folder beside it. Verify the matching `SHA256SUMS.txt`.

FileViz runs normally by default. Administrator scans request UAC for a read-only worker; the whole application can also restart as administrator. No drivers or filesystem locks are installed.

![FileViz desktop showing a disposable sample scan](docs/images/desktop.png)

## Features

- Per-drive usage, selected folders and UNC shares, native batched directory enumeration, elevated raw NTFS 3.0/3.1 MFT scanning with visible fallback.
- SQLite scan history, bounded file pages, interactive treemap, large folders, file types, logical/reported allocated sizes, filters, exclusions, saved profiles, and snapshot comparisons.
- Name candidates or SHA-256 / SHA-1 / MD5 content groups, with size/sample/full-hash staging and identity-checked caching. Hard links are aliases, not extra reclaimable copies.
- Explorer, path copy, properties, CSV/JSON exports, reviewed same-volume quarantine, fresh byte/stream verification, no-overwrite restore, and action history.
- Keyboard controls, light/dark themes, DPI awareness, safe batch pause, and worker cancellation.

This is a preview. Raw fixture parity passes, but the 2x MFT speed target is currently unmet. [Performance evidence](docs/performance.md) and the [validation matrix](docs/validation.md) list measured results and outstanding coverage. Quarantine and Recycle Bin operations do not immediately reclaim space.

## Develop

Install the SDK pinned in `global.json`, or use the workspace bootstrap below. The wrapper selects the local SDK even when the machine still has .NET 6:

```powershell
./scripts/bootstrap.ps1
./scripts/dotnet.ps1 restore FileViz.slnx --locked-mode
./scripts/dotnet.ps1 build FileViz.slnx -c Release --no-restore
./scripts/dotnet.ps1 test FileViz.slnx -c Release --no-build
./scripts/run.ps1
```

## Documentation

- [Setup](docs/setup.md) and [user guide](docs/user-guide.md)
- [Architecture](docs/architecture.md)
- [Performance](docs/performance.md) and [validation](docs/validation.md)
- [Release process](docs/release.md)
- [Contributing](CONTRIBUTING.md) and [security reports](SECURITY.md)

Analysis stays in `%LOCALAPPDATA%/FileViz`. No telemetry, automatic deletion, hard-link replacement, automatic updates, or remote storage. FileViz does not modify files while scanning or hashing.

## License

GPL-3.0-only; see [LICENSE](LICENSE). [Third-party notices](THIRD-PARTY-NOTICES.md) and full dependency license texts ship in the packages.