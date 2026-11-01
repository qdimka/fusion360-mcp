# Verification — decisions and implementation notes

Module: the `verification` result block (P1, [ADR-0009](../adr/ADR-0009-verification-block.md)). The current contract is in [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md) §§1.2 and 4. Active invariants remain in code comments marked `INVARIANT`, `MEASURED`, `DOC`, or `LIMIT`.

P1 is implemented. The `roundtrip_checked` level is defined but no current result producer emits it.

## <a id="ladder"></a>Use verification levels rather than one flag (2026-10-30, P1)

Before P1, mutation success was represented by `ok: true` and `deltas`. Deltas show that a feature changed something, but do not establish that the result is correct. The `verification.level` ladder is monotone: `call_returned` → `existence_checked` → `geometry_checked` → `roundtrip_checked`. Report only the strongest level actually confirmed. `roundtrip_checked` requires an export/import round trip and a subsequent read.

## <a id="named-checks"></a>Named checks and unverified aspects (2026-10-30, P1)

A proposed single `verified: bool` was rejected because it could not distinguish a returned call from a geometry measurement and did not identify what remained unchecked.

Each `checks[]` entry is `{name, observed, expected, passed}`. Numeric values use the shared tolerance table, rather than being compared directly with tool input parameters. Each `unverified_aspects[]` entry is `{name, reason}`. An aspect that could not be checked should be identified with a reason.

Verification builders attach named checks where readback data is available. The result block lists the actual checks and the unverified aspects for that operation.

## <a id="tolerances"></a>Keep numeric tolerances in one place (2026-10-30, P0)

Duplicating tolerances across tools risks inconsistent comparisons. The shared values live in §4 of `TOOL_CONTRACTS.md` and in `VerificationTolerances.cs`:

| Measurement | Unit | Pass tolerance |
|---|---|---|
| Volume | cm³ | `abs(observed - expected) <= max(0.01, 0.001 * abs(expected))` |
| Bounding box coordinate | cm | `abs(observed - expected) <= 0.001` for each min/max coordinate |
| Mass | g | `abs(observed - expected) <= 0.001 * abs(expected)` (0.1%) |

A value outside tolerance makes the check fail; it does not create a new `error_kind`. If a required measurement cannot be read, report the aspect as unverified.
