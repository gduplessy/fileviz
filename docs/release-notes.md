# FileViz 0.2.4 preview

Duplicate analysis now shows activity throughout candidate preparation, sampling, full hashing, and result publication.

## Changes

- A dedicated activity card appears immediately with the current phase, elapsed time, stage file counts, actual bytes read, cache hits, and errors. Large files report their current byte progress.
- SQLite execution sends activity updates during long candidate queries. Waiting for the next work update is shown explicitly; elapsed time alone is never presented as proof of progress.
- Cancel analysis interrupts executing database statements and disconnects the read-only worker. Interrupted publication rolls back and retains the last completed duplicate groups.
- Metadata revalidation runs in the isolated worker so blocked filesystem providers stay cancellable. Cache reuse still checks identity, size, modified time, and change time.
- Single-snapshot preparation avoids unnecessary latest-history lookups. Samples and name matches still never prove duplicate content or authorize cleanup.
- Completed, cancelled, and failed states replace the stale scan status. Progress percentages apply only to the current stage; preparation and grouping remain indeterminate.

## Packages and validation

Download the per-user `FileViz-0.2.4-win-x64-setup.exe` or extract the complete `FileViz-0.2.4-win-x64-portable.zip`. Both include the runtime and matching worker. Verify against `SHA256SUMS.txt`.

63 regression tests pass, including actual worker read-byte messages, executing-query cancellation, and rollback of cancelled result publication. The visible desktop smoke exercises immediate activity, cache reuse, cancellation, and retained results on disposable files. No throughput claim is made for large live drives or shares.

Close the previous process before upgrading. Existing snapshots and previously completed duplicate results are preserved; an unfinished analysis must be restarted with the new version. Preview quality; no telemetry, automatic updates, or automatic deletion.
