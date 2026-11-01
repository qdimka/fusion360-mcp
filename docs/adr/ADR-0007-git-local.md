# ADR-0007 — Local Git history; do not create a remote (D7)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D7 (2026-10-27).
Environment: `fusion360-mcp` repository; the reference checkout lives under `reference/` and is excluded from CI artifacts.

## Decision

Use Git locally and commit completed phases. Do not create or push to a remote unless the user explicitly requests it. Keep commit messages short and include the phase tag where applicable (for example, `P0: ...` or `T8: ...`).

## Rationale

The repository is a working C# port; public hosting and distribution were not requested. Local phase commits provide rollback points and a record of build, test, and parity gates without publishing externally.

## Evidence recorded at adoption

- The repository history contains phase-tagged commits.
- The policy is recorded in the ADR registry.

## Alternatives rejected

- **Create a GitHub/GitLab remote immediately:** publication and external CI were not requested.
- **Use one large commit for the port:** would lose phase-level rollback points.
