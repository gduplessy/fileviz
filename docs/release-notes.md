# FileViz 0.3.0 preview

Review low-resolution photos by drive and move explicitly selected originals to reversible quarantine.

## Changes

- New **Photos** page (Ctrl+7) with a saved drive/folder selector, name/path filtering, and 100-row pages.
- Short-edge threshold defaults to 720 pixels. Enable a megapixel threshold as well, or use zero to disable either criterion. Portrait and landscape receive the same treatment. Resolution does not measure blur or compression quality.
- Read dimensions on demand in a dedicated worker with progress, elapsed time, current file, cancellation, and retained metadata. Normal drive scanning remains metadata-only.
- Windows codecs support common raster formats; optional WebP/HEIC/AVIF support depends on installed codecs. Unsupported, corrupt, inaccessible, changing, or timed-out images appear separately and cannot be selected for removal.
- Inspect a bounded preview or show the original in Explorer. Select individual photos or the visible page, review, and quarantine only files that pass fresh identity/timestamp checks. Restore originals from Cleanup without overwriting.
- Codec workers have a Windows-enforced 384 MiB committed-memory limit and ten-second read deadlines. Cloud placeholders and reparse paths are skipped. No automatic removal selections, hydration requests, or permanent-deletion fallback.

## Packages and validation

Use `FileViz-0.3.0-win-x64-setup.exe` or extract the complete `FileViz-0.3.0-win-x64-portable.zip`. Verify the attached `SHA256SUMS.txt`.

Regression coverage includes threshold boundaries and orientation, bounded worker previews, changed-file/cache rejection, root isolation, pagination, and original-byte preservation through disposable photo quarantine/restore. The rendered fixture exercises analysis, preview, explicit review, cancellation with retained results, error separation, and restore.

Close the previous process before upgrading. Saved inventories, duplicate results, and journals are preserved. Preview quality; no telemetry, automatic updates, or automatic deletion.
