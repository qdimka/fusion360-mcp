# Project status

Updated 2026-11-04. The published product remains beta (`0.1.0` in
`Directory.Build.props`). The C# MCP server exposes 92 tools. A pinned Python
Fusion add-in and installers for Windows and macOS are in the working tree as
a release candidate; the legacy C# add-in remains until live platform checks
pass. See the [release plan](plans/stable-release.md),
[implementation task](tasks/python-addin-stable-release.md), and
[compatibility inventory](status/python-addin-compatibility.md).

Local evidence: 120 Core tests, 84 Server tests, 186 legacy AddIn tests, and
12 Python headless tests passed. Catalog/dispatch parity and documentation
guards passed. The macOS installer passed a repeat install from the release
zip in a temporary directory, and rejected an invalid manifest without
replacing the previous installation. All four server archives and the Python
add-in zip were produced with valid SHA-256 checksums. These checks used the working
tree, not a tagged release.

Windows Fusion E2E evidence is unavailable, and no stable release has been
validated. [Live E2E evidence](status/e2e-stable-release.md) remains pending.
The release workflow refuses versioned publication while that file records a
pending platform.
