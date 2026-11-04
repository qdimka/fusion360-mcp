# Stable release E2E evidence

Status: pending
Windows: pending
macOS: pending
Candidate version: pending
Add-in SHA256: pending

Record the candidate commit, server and add-in artifact SHA-256 values, OS and
Fusion versions, named saved design, each command and observed response from
the matrix in [the release task](../tasks/python-addin-stable-release.md),
defects, and affected-case reruns. The add-in hash must match the reproducible
release zip built from the tagged source. Change a platform line to `pass` only after
its run is complete. Set `Status: pass` only when both platform runs and all
release-blocking cases pass on the candidate artifacts.
