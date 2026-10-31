# FileViz 0.3.3 preview

Photo review now loads previews automatically and can select matching photos across every result page.

## Changes

- Selecting a photo starts a bounded preview automatically. Fast navigation cancels superseded requests; only the current selection can update the image. The separate Load preview button is removed.
- Select all matches (all pages) selects eligible photos throughout the current applied filter and scope. Partial selections show an indeterminate checkbox; unchecking a complete selection clears every page. Blocked files remain unselected. More than 5,000 matches requires narrower filters and leaves existing choices unchanged.
- Empty photo results explicitly distinguish images not yet analysed from completed analysis. Partial coverage reports the remaining image count.
- Photo preparation no longer builds a new index across historical inventories. Focused folder review is available with `FileViz.exe --photo-folder "C:\Photos"`, using a dedicated local database and directory enumeration.

## Validation and packages

All 72 regressions pass locally. A disposable desktop fixture verifies automatic preview, superseded-request cancellation, cross-page checkbox state, individual deselection, applied filters, protected-file exclusion, and original-byte preservation through fixture-only quarantine/restore. Windows CI runs these desktop checks for pushes and releases.

Use `FileViz-0.3.3-win-x64-setup.exe` or extract the complete `FileViz-0.3.3-win-x64-portable.zip`. Verify `SHA256SUMS.txt`. Saved inventories and cleanup journals are preserved. Close the previous application before upgrading.

Selection is not deletion: removals still require explicit review and fresh verification. Resolution thresholds do not measure blur or compression quality.
