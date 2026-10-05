# Security reporting

Report a vulnerability privately through [GitHub private reporting](https://github.com/gduplessy/fileviz/security/advisories/new). Private reporting is enabled. Include version, scan engine, filesystem, elevation state, and a disposable reproduction; redact private paths and file contents.

Raw volume access is read-only. Elevation does not grant access to locked BitLocker volumes, EFS keys, or remote-share credentials. Elevated workers accept only authenticated scan, metadata-read, and hash requests. The UI owns SQLite and reviewed cleanup operations. Another-account UAC cannot authenticate to a current-user-only worker pipe; run the entire app as that administrator when needed.

Cleanup must remain explicitly reviewed, freshly revalidated, journaled, and recoverable. It must never silently change to permanent deletion. Treat NTFS records and IPC frames as untrusted input. Parser changes require bounded malformed-input tests; cleanup tests use disposable fixtures only.