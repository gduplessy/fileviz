# Release process

1. Restore locked packages; run the Release build and tests on Windows.
2. Run the visible desktop fixture smoke test and disposable NTFS parity tooling.
3. Run the index benchmarks; record hardware, results, and unmet targets in `performance.md`.
4. Update the changelog and preview limitations. Review package licenses and notices.
5. Run `scripts/build-release.ps1` into an empty output directory. Verify the ZIP and per-user installer with no developer runtime in PATH.
6. Commit the version and evidence; tag `vVERSION`. The package workflow attaches versioned packages and checksums to a preview GitHub release.

The GPL license and upstream license texts ship with both package formats. The `worker` directory is required. Installer upgrades preserve local snapshots under `%LOCALAPPDATA%/FileViz`; uninstall removes application binaries, not analysis history or quarantined files.

Code signing is not configured. Packages must not claim a verified publisher. CI-generated binaries may have different hashes from a local build; always use the checksum file attached to the matching release assets.