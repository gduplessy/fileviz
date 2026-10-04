# Contributing

Use the pinned .NET SDK. Keep changes focused, run the relevant tests, and document visible behavior changes. Report scan engine, elevation state, filesystem, and reproducible steps in issues; redact private paths.

Parser changes require malformed-input tests and directory-engine parity checks. Cleanup changes require disposable fixtures and tests for changed identities, keeper protection, and recovery. Never use personal files for destructive testing.

Commit messages should state the resulting change. Separate unrelated changes. Update documentation and the changelog in the same change as behavior.
