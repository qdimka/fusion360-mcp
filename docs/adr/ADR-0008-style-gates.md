# ADR-0008 — C# language version, nullable checks, warnings, and xUnit (D8)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D8 (2026-10-27).
Environment: `TreatWarningsAsErrors=true` across the solution; xUnit 2.9.3 in Core, Server, and AddIn tests.

## Decision

- Use the latest C# language version available to each target framework.
- Enable nullable reference types across the solution.
- Set `TreatWarningsAsErrors=true` so compiler warnings fail the build.
- Use xUnit (`xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, and `Microsoft.NET.Test.Sdk`) in all test projects. Do not add Shouldly or FluentAssertions; use standard xUnit assertions.

## Rationale

Treating warnings as errors catches nullable and unused-code issues during compilation without a separate lint pass. xUnit is already used in the reference and related C# projects; its built-in assertions cover the test needs without another dependency.

## Evidence recorded at implementation

- The release build completed with zero warnings.
- The recorded suite had 290 passing tests: Core 100, Server 48, and AddIn 142.
- The implementation checkpoint recorded a clean commit gate: zero warnings, 290 passing tests, and parity passing.

## Alternatives rejected

- **Shouldly or FluentAssertions:** standard xUnit assertions meet the need without extra dependencies.
- **Disable `TreatWarningsAsErrors`:** would allow nullable and unused-code warnings to accumulate.
- **Pin one older C# version for every target:** would unnecessarily limit newer targets; use the latest supported version per target instead.
