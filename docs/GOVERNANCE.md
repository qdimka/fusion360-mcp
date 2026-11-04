# Release and technical-debt governance

## Version and release

`Directory.Build.props` holds the shared server version. The Python add-in
manifest and release notes must identify the same release. A push to `main`
builds and tests but does not create a GitHub Release. A `v<version>` tag
matching the shared version starts the release and versioned Docker workflows.
Those workflows require passing macOS candidate evidence in
[e2e-stable-release.md](status/e2e-stable-release.md); the release attaches
server archives, the Python add-in zip, and SHA-256 checksums. Release notes
state supported Fusion versions, known limits, and upgrade steps.

The C# add-in has been retired from the source tree, build, and distribution.
Windows Fusion E2E is deferred until after release because no Windows Fusion
host is currently available; Windows installer CI remains a release check.
Record the limitation and schedule the live Windows candidate run as the first
post-release compatibility task. The release remains blocked by a failed
macOS candidate run or any other release-blocking defect.

## Technical debt

Track open items in [TECH_DEBT.md](status/TECH_DEBT.md). After each release,
resolve an item, close it with a reason, or retain it with a concrete return
condition. Avoid increasing the open count during a release cycle. An item
that affects the published contract, data safety, installability, or supported
platforms is a release blocker rather than ordinary debt.
