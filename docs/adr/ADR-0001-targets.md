Superseded for the Fusion add-in: the net48 C# add-in was retired on 2026-11-04. The active Fusion implementation is the Python package in `addon/Fusion360MCP/`; the .NET server and shared Core remain.

# ADR-0001 — Target frameworks: net10 server / net48 add-in / netstandard2.0 core (D1)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D1 (2026-10-27), implemented in T1–T8.
Environment: macOS 15.7.4 (arm64) development host; Fusion 2706.1.14 E2E (2026-10-29; see [E2E status](../E2E-STATUS.md)). The C# add-in runs only in Fusion for Windows.

## Decision

The solution uses three target frameworks (PLAN.md, section 3, D1):

- **MCP server and command-line tools:** .NET 10 (`net10.0`), published as single-file, self-contained artifacts for four RIDs.
- **Add-in:** .NET Framework 4.8 (`net48`), the managed C# add-in target supported by Fusion on Windows.
- **Shared models:** `Fusion360.Mcp.Core` targets .NET Standard 2.0 so both the net48 add-in and net10 server can use the same envelopes, errors, units, catalog, and mock layer.

## Rationale

Fusion's managed API surface in Windows uses .NET Framework assemblies, so the add-in targets net48. The MCP server runs as a separate process and can use .NET 10 and the official MCP SDK. A shared netstandard2.0 core avoids duplicating protocol models across the two processes.

## Evidence recorded at implementation

- `dotnet build Fusion360Mcp.sln -c Release` built on macOS without Fusion, using the compile-time stub described in [ADR-0003](ADR-0003-adsk-stub.md); the recorded suite had 290 passing tests (Core 100, Server 48, AddIn 142).
- `scripts/publish.sh` produced self-contained server artifacts for `win-x64`, `osx-arm64`, `osx-x64`, and `linux-x64`, plus a net48 add-in archive.
- The platform constraint is documented in the installation guide: the C# add-in is Windows-only; macOS E2E uses the reference Python add-in over the same protocol.

## Alternatives rejected

- **Use net10 for every project:** Fusion's managed add-in surface requires the net48-compatible runtime.
- **Target the add-in at netstandard2.0:** net48 is the explicit Fusion target; netstandard2.0 remains the shared library target.
- **Duplicate shared models in each project:** duplication risks protocol drift, so the projects use one core library.
