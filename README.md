# FileViz

Native Windows 11 disk analysis, raw NTFS metadata scanning, and duplicate review.

## Development

Install the .NET 10 SDK version specified by `global.json`, then run:

```powershell
dotnet build FileViz.slnx -c Release
dotnet test FileViz.slnx -c Release
dotnet run --project src/FileViz.App
```

## Documentation

- [User guide](docs/user-guide.md)
- [Architecture](docs/architecture.md)
- [Performance and validation](docs/performance.md)
- [Contributing](CONTRIBUTING.md)
- [Security reports](SECURITY.md)

FileViz stores analysis locally. It does not send telemetry or automatically remove files.

## License

GPL-3.0-only. See [LICENSE](LICENSE).
