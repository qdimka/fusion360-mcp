# Stable release E2E evidence

Status: pending
Windows: pending
macOS: pending
Candidate version: pending
Add-in SHA256: pending

Windows Fusion E2E has not been run: a Windows Fusion host is currently
unavailable. Windows remains pending; installer CI is not a substitute for
live Fusion testing.

Record the candidate commit, server and add-in artifact SHA-256 values, OS and
Fusion versions, named saved design, each command and observed response from
the matrix in [the release task](../tasks/python-addin-stable-release.md),
defects, and affected-case reruns. The add-in hash must match the reproducible
release zip built from the tagged source. Change a platform line to `pass` only after
its run is complete. Set `Status: pass` only when both platform runs and all
release-blocking cases pass on the candidate artifacts.

## Preliminary artifacts (not E2E evidence)

Built locally from source commit `7a8436fb923602115b1b7ba2876a6a75e73c47bd`;
all entries in `SHA256SUMS` verified locally. These archives have not been
installed or tested in Fusion on either host and are not final release evidence.

| Artifact | SHA-256 |
| --- | --- |
| Python add-in | `76a29dd19aadc0622a7b1d601fb883b01deb9356ebdf1f86bc0ed1cd8fbdb001` |
| Server win-x64 | `601527885033ca89986130b461f0301d34092b5b82f70af9f917b1b6cb1e6a3c` |
| Server osx-arm64 | `2431a8e37a5cec3da2f81fe43e64a5c2a29e7bb8670f49fdc575b10f55638065` |
| Server osx-x64 | `6aa73f6a116c2b244ccef6b6621ad3a7959952a5d82d9c663a00db527c4a25c1` |
| Server linux-x64 | `a6f4046b6e39ef8c35bc4fa0652feef46f29e5ec6818b5c5744880e08156f748` |

Preliminary macOS Fusion: pending (no active connection at preparation time).
Preliminary Windows Fusion: pending (host unavailable).

Local journal gate: 28 filtered `IdempotentToolBackendTests` and
`OperationJournalTests` passed on 2026-11-04. This does not replace either
live Fusion platform run.
