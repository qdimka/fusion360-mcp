# Project status

Updated 2026-11-04. No stable release has been published; the project remains
beta. The current release candidate is version `1.0.0` in `Directory.Build.props`.
The C# MCP server exposes 92 tools. A pinned Python
Fusion add-in and installers for Windows and macOS are committed in `1750af1` as
a release candidate. The Python add-in is the only Fusion add-in in the source
tree. Windows live Fusion E2E is scheduled after release; Windows installer CI
remains required. See the [release plan](plans/stable-release.md),
[implementation task](tasks/python-addin-stable-release.md), and
[compatibility inventory](status/python-addin-compatibility.md).

Historical local evidence before C# retirement: 120 Core tests, 84 Server
tests, and 186 legacy AddIn tests passed. Current verification: 121 Core tests,
84 Server tests, and 25 Python headless tests pass. Catalog/dispatch parity and
documentation guards passed. The macOS installer passed a repeat install from the release
zip in a temporary directory, and rejected an invalid manifest without
replacing the previous installation. All four server archives and the Python
add-in zip were produced with valid SHA-256 checksums. These checks used the working
tree before commit `1750af1`, not a tagged release.

Windows Fusion E2E evidence is unavailable and is deferred until after
release. macOS must pass the exact release candidate before publication.
[Live E2E evidence](status/e2e-stable-release.md) records the candidate and
the Windows follow-up.
