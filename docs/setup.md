# Development setup

Windows 11 x64, Git, and the SDK pinned in `global.json` are required. The project targets .NET 10 and cannot build with .NET 6. The runtime is bundled in release packages.

```powershell
./scripts/bootstrap.ps1
./scripts/dotnet.ps1 restore FileViz.slnx --locked-mode
./scripts/dotnet.ps1 build FileViz.slnx -c Release --no-restore
./scripts/dotnet.ps1 test FileViz.slnx -c Release --no-build
./scripts/run.ps1
```

`run.ps1` uses `.tools/dotnet` if present, otherwise the installed SDK. NuGet dependencies are pinned in committed lock files. SQLite uses Windows-serviced `winsqlite3.dll`.

For packages, install Inno Setup 6.7.3 and run:

```powershell
./scripts/build-release.ps1 -Version 0.1.0 -Iscc 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

Use a new output directory for each rebuild. The script never removes previous packages. Tagged CI builds generate a portable ZIP, per-user installer, and SHA-256 checksums. `docs/release.md` describes the release checklist.
The bootstrap installs the pinned SDK under ignored `.tools/dotnet`, without removing SDKs needed by other projects or changing global PATH. Use `scripts/dotnet.ps1` when the global `dotnet` command still resolves to .NET 6.