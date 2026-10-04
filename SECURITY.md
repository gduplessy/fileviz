# Security

Report vulnerabilities using GitHub private vulnerability reporting when enabled. Do not publish private paths or sensitive file contents in an issue.

Raw volume access is read-only. Elevation does not grant access to locked BitLocker volumes, EFS keys, or remote share credentials. Scanner workers must not expose arbitrary filesystem mutation or database-write operations through IPC.

Cleanup must remain explicitly reviewed, must revalidate files, and must never silently become permanent deletion. Treat NTFS records and worker messages as untrusted input.
