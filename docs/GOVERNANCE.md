# Release and technical-debt governance

## Version and release

`Directory.Build.props` holds the shared server version. The Python add-in
manifest and release notes must identify the same release. A push to `main`
builds and tests but does not create a GitHub Release. A `v<version>` tag
matching the shared version starts the release and versioned Docker workflows.
Those workflows require passing Windows and macOS evidence in
[e2e-stable-release.md](status/e2e-stable-release.md); the release attaches
server archives, the Python add-in zip, and SHA-256 checksums. Release notes
state supported Fusion versions, known limits, and upgrade steps.

The stable release remains blocked while a supported platform lacks a live
candidate run or a release-blocking defect is open. The C# add-in is retired
only after the Python replacement passes both platform runs.

## Technical debt

Track open items in [TECH_DEBT.md](status/TECH_DEBT.md). After each release,
resolve an item, close it with a reason, or retain it with a concrete return
condition. Avoid increasing the open count during a release cycle. An item
that affects the published contract, data safety, installability, or supported
platforms is a release blocker rather than ordinary debt.
