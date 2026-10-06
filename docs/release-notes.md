# FileViz 0.2.5 preview

Select the drive for duplicate analysis directly on the Duplicates page.

## Changes

- A Drive or folder selector lists saved roots with their scan IDs. Choosing a root selects its matching snapshot and keeps the Duplicates page open.
- Coverage and inventory requirements are explicit: scan an additional drive in Home before analyzing its saved inventory.
- Cross-drive comparison and drive checkboxes now sit beside the selector, rather than in the removal tray. Cross-drive mode uses the latest saved scan for each checked root.
- The selector reflects Explorer and scan-history changes, including explicitly reopened older snapshots. Selecting a single scope replaces the cross-drive defaults with that scope.
- Scope changes are locked during analysis; finish or cancel before choosing another drive. Existing matches are labelled as the last completed analysis until a new run replaces them.

## Packages and validation

Use the per-user `FileViz-0.2.5-win-x64-setup.exe` or extract the complete `FileViz-0.2.5-win-x64-portable.zip`. Verify the attached `SHA256SUMS.txt`.

The visible disposable UI fixture verifies switching between two inventories without leaving Duplicates, single-scope result isolation, cross-drive results from both scopes, busy lockout, return to a single scope, and historical snapshot selection. Duplicate progress, cancellation, and cache reuse checks remain enabled. The regression suite contains 63 tests.

Close the previous process before upgrading. Saved inventories and completed duplicate results are preserved; a running analysis must be cancelled or restarted to apply the new UI. Preview quality; no telemetry, automatic updates, or automatic deletion.
