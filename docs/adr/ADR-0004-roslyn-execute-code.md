# ADR-0004 — Arbitrary `execute_code` tool

Status: superseded. The original C#/Roslyn execution proposal is not part of the current product surface.

## Original proposal

An early design proposed an `execute_code` tool that accepted C# and executed it through Roslyn on Fusion's main thread. The proposal was recorded as D4 and was implemented in an intermediate project state.

## Current decision

The C# port's catalog intentionally excludes arbitrary `execute_code`; the server exposes typed tools from `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`. This decision applies to the C# port only: the upstream reference Python implementation still exposes `execute_code` and can run supplied Python code inside Fusion 360. Do not add a C# scripting engine without a new ADR and a separate decision about the boundaries of that API.

## Evidence and consequences

- The [README](../../README.md) distinguishes the C# catalog from the reference Python implementation, where `execute_code` remains available.
- The current catalog has 92 MCP tools. The AddIn project does not reference `Microsoft.CodeAnalysis.CSharp.Scripting`, `CSharpScriptEngine`, or `IFusionScriptEngine`.
- Previous statements about Roslyn, code limits, `execute_code` tests, and a scripting guide described an intermediate implementation and are not current project facts.
