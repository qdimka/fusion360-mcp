Superseded: the legacy C# add-in and its compile-time adsk stubs were removed on 2026-11-04. This ADR is retained as historical implementation context only.

# ADR-0003 — Committed `adsknet.dll` compile-time stub for CI (D3)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D3 (2026-10-27), implemented in T1.
Environment: macOS arm64 development host without Fusion; Windows Fusion 2706.1.14 provides the runtime `adsknet` assembly.

## Decision

The repository contains a compile-time `adsknet.dll` stub under `thirdparty/adsk/`. It exposes the API surface needed to compile (`adsk.core`, `adsk.fusion.*`) but has no runtime implementation. The add-in references the stub with `CopyLocal=false` (see the project file), so:

- AddIn tests can compile and run on CI hosts without Fusion.
- The stub is not copied into build output or the installed add-in; Fusion resolves its real `adsknet` assembly at runtime.
- The published add-in archive excludes `adsknet.dll`.

Build the stub into a clean output directory; never overwrite the committed file in `thirdparty/adsk/`.

## Rationale

CI must compile and test the net48 add-in without requiring an installed Fusion instance. The vendor assembly is available only with Fusion, so a compile-only stub separates the build-time reference from the runtime implementation.

## Evidence recorded at implementation

- The solution built and the recorded 142 AddIn tests passed on macOS without Fusion; the stub supplied the compile-time API.
- The published add-in archive was checked to ensure it did not contain `adsknet.dll`.
- `TreatWarningsAsErrors=true` passed without warnings.

## Alternatives rejected

- **Commit the vendor `adsknet.dll`:** adds a licensed vendor binary and ties CI to a Fusion installation.
- **Use reflection or late binding:** loses a typed API surface and makes handler logic harder to unit-test.
- **Download adsk assemblies in CI:** adds a network and version dependency and may not cover preview APIs.
