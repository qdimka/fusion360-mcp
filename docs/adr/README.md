# Architecture Decision Records

This directory records architecture decisions using the ADR pattern, inspired by the [`SergeySklyarov/Kompas3D-MCP`](https://github.com/SergeySklyarov/Kompas3D-MCP) reference.

## Conventions

- Write an ADR before implementation when a decision will shape the architecture. Add evidence as implementation proceeds.
- Record status (`accepted`, `proposed`, or `superseded`), date, environment, decision, rationale, evidence, and rejected alternatives.
- Use `accepted (implemented)` only when the implementation and supporting evidence exist.
- Keep decision history in `docs/decisions/<module>.md`; code comments should point to the relevant history anchor. `INVARIANT`, `MEASURED`, `DOC`, and `LIMIT` comments identify current code-level rules.

## Index

| ADR | Topic | Status |
|---|---|---|
| [ADR-0001](ADR-0001-targets.md) | Historical target frameworks: net10 server / net48 add-in / netstandard2.0 core | superseded for Fusion add-in |
| [ADR-0002](ADR-0002-mcp-sdk.md) | Official C# MCP SDK and TCP :9876 bridge | accepted (implemented) |
| [ADR-0003](ADR-0003-adsk-stub.md) | Compile-time `adsknet.dll` stub for CI | superseded |
| [ADR-0004](ADR-0004-roslyn-execute-code.md) | Arbitrary `execute_code` tool | superseded |
| [ADR-0005](ADR-0005-facade-ifusioncontext.md) | `IFusionContext` facade and main-thread dispatch | superseded for current Fusion execution |
| [ADR-0006](ADR-0006-tool-catalog-ssot.md) | Single source of truth for the tool catalog | accepted (implemented) |
| [ADR-0007](ADR-0007-git-local.md) | Local Git history; no remote unless requested | accepted (implemented) |
| [ADR-0008](ADR-0008-style-gates.md) | C# version, nullable checks, warning policy, and xUnit | accepted (implemented) |
| [ADR-0009](ADR-0009-verification-block.md) | Verification levels and named checks | accepted (implemented) |
| [ADR-0010](ADR-0010-operation-id-journal.md) | `operation_id` and durable server-side journal | accepted (implemented) |
| [ADR-0011](ADR-0011-lightweight-revisioning.md) | Lightweight model revisioning | accepted (implemented) |

The public tool and wire contract is documented in [`../TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md). It covers mutation results, error kinds, retries, revisions, and verification tolerances; retry guidance is not exposed as a separate protocol field.
