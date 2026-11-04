# ADR-0005 — Testable handlers: `IFusionContext` facade and main-thread dispatch (D5)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D5 (2026-10-27), implemented in T3/T5–T7.
Environment: net48 add-in on Fusion's main thread; development tests run without Fusion; E2E on Fusion 2706.1.14.

## Decision

Command handlers use the thin `IFusionContext` facade over adsk (`activeDesign`, `rootComponent`, `bodiesByName`, `sketches`, `features`, `camera`, `export`, and related APIs). `AdskFusionContext` adapts the real API; unit tests exercise handlers with a fake context and no Fusion installation.

All adsk calls run on Fusion's main thread. Socket workers enqueue `WorkItem`s and post the `Fusion360MCP_BridgeEvent` CustomEvent; a 200 ms backup timer checks for undelivered events. Each socket worker waits on its own item completion signal. Lengths use centimetres and angles use radians (`Fusion360.Mcp.Core.Units.InternalUnits`).

The bridge deadline is 300 seconds. A queued item that expires before its handler starts is marked cancelled and skipped by `DrainQueue`, so it cannot execute after the deadline. A handler already running cannot be interrupted.

## Rationale

The adsk API is not thread-safe and must be called on Fusion's main thread. The facade enables handler tests without Fusion and helps enforce that mutations do not run on socket workers.

## Evidence recorded at implementation

- The recorded 142 AddIn tests covered handlers through fake contexts, `SocketServer`, `EventBridge`, and `McpLog` without Fusion.
- The recorded live smoke sequence checked centimetre units: a 2×2×2 cm box yielded 8 cm³, and an extrusion yielded approximately 141.3 g for steel.
- Tests covered the bridge cancellation path; a live invalid-body export returned `BODY_NOT_FOUND` with hints and traceback.

## Alternatives rejected

- **Call adsk directly from handlers:** makes handlers hard to test without Fusion and risks thread-safety violations.
- **Build a full in-memory Fusion simulator:** costly and likely to drift from adsk behavior; fakes cover only the interfaces handlers use.
- **Interrupt an expired adsk call:** the API has no cancellation mechanism; queued work is instead flagged and skipped before it starts.
