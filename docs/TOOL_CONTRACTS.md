# Tool and wire contracts

This document describes shared behavior of the MCP tools and the add-in
protocol. Per-tool names, parameters, units, and effects are maintained in the
[generated tool catalog](TOOLS.md).

## 1. Socket protocol

The server and add-in exchange one JSON object per line over TCP. A request
uses `type` for the command name and `params` for its arguments:

```json
{"type":"extrude","params":{"sketch_name":"Sketch1","distance_cm":1.5}}
```

The add-in responds with a success frame containing a result object:

```json
{"status":"success","result":{"ok":true,"body_name":"Body1"}}
```

Infrastructure failures use an error frame (`status: "error"`, `message`,
and optional `error_kind`, `hints`, and `traceback`). A command-level failure
may instead be returned in a success frame with `result.ok: false`; clients
must check `result.ok` as well as the frame status.

### 1.1. Result shape

Successful results contain `ok: true` and tool-specific fields. Mutations may
also return `deltas` with before/after/count, mass, or bounding-box data.
Failures contain `ok: false`, `error_kind`, and `error_message`; `hints`,
`traceback`, and other diagnostic fields may be present when available.

```json
{"ok":true,"body_name":"Body1","deltas":{"body_count_before":0,"body_count_after":1,"body_count_delta":1}}
```

```json
{"ok":false,"error_kind":"PROFILE_NOT_CLOSED","error_message":"No profiles in sketch","hints":["close the sketch loop before extruding"]}
```

### 1.2. Additive mutation fields

Mutation results may include these fields. They are additive: clients should
ignore fields they do not use.

| Field | Meaning |
| --- | --- |
| `operation_id` | UUID v4 identifying the journaled operation. Client-supplied IDs are accepted; otherwise the server generates one. |
| `revision_after` | Model revision after a document mutation or a rejected stale-revision request. |
| `partial_effects` | On a failed mutation, `false` means no effects were established; `true` means changes were observed. Omitted when not known. |
| `outcome_unknown` | The response was lost or execution status cannot be determined. Reconcile the scene before continuing. |
| `verification` | Optional verification summary, described below. |

The `verification` object has `level`, `checks`, and `unverified_aspects`:

```json
{
  "level":"geometry_checked",
  "checks":[{"name":"body_count","observed":2,"expected":2,"passed":true}],
  "unverified_aspects":[{"name":"appearance","reason":"not read back from Fusion"}]
}
```

Verification levels, from least to most evidence, are `call_returned`,
`existence_checked`, `geometry_checked`, and `roundtrip_checked`. A level
records the strongest check actually performed. Checks report observed and
expected values; an unverified aspect must include a reason. An empty
`unverified_aspects` list means no unverified aspect was reported, not a
guarantee beyond the checks listed.

## 2. Error kinds

`error_kind` is a stable `SCREAMING_SNAKE_CASE` tag. Current values are:

`PROFILE_NOT_CLOSED`, `SKETCH_NOT_FOUND`, `BODY_NOT_FOUND`,
`SELF_INTERSECTION`, `REGEN_FAILED`, `BOOLEAN_NO_OP`, `INVALID_INPUT`,
`NO_ACTIVE_DESIGN`, `DESIGN_TYPE_MISMATCH`, `TIMEOUT`, `UNKNOWN_COMMAND`,
`OPERATION_ID_CONFLICT`, `REVISION_CONFLICT`, `STALE_REFERENCE`, and
`UNKNOWN`.

Treat the tag as machine-readable and `error_message`/`hints` as explanatory
text. New tags may be added; unknown tags should be handled as `UNKNOWN`.

## 3. Mutation idempotency

The server journals mutating operations before sending them to Fusion. Supply
an optional UUID `operation_id` argument to make a retry refer to the same
operation; otherwise the server creates one and returns it in the result.

- Repeating the same ID, command, and arguments after success returns the
  recorded result without executing the operation again.
- Reusing the ID with a different command or arguments returns
  `OPERATION_ID_CONFLICT`.
- A known clean failure can be retried with the same ID and arguments.
- An unknown outcome or observed partial effects cannot be replayed safely.
  Inspect the scene and deltas, then issue a new operation with a fresh ID.
- A durable intent with no terminal result after a server restart requires
  reconciliation before retrying.

Read-only tools do not use the mutation journal. External-output operations
such as exports are journaled to prevent duplicate side effects.

## 4. Revision guards and verification tolerances

Document mutation tools accept optional `expected_revision`. Obtain the
current value from `get_scene_info.model_revision` or a prior mutation's
`revision_after`. When it is stale, the operation is rejected with
`REVISION_CONFLICT` before model changes and includes the current
`revision_after`. Re-read the scene and retry with the current value. If a
previously observed name no longer resolves after a scene change, the add-in
may report `STALE_REFERENCE`.

When a geometry verification check uses numeric tolerances, the shared table
is:

| Measurement | Tolerance |
| --- | --- |
| Volume | `max(0.01 cm³, 0.001 × abs(expected))` |
| Bounding box | `0.001 cm` per coordinate |
| Mass | `0.001 × abs(expected)` grams |

The comparison is inclusive (`abs(observed - expected) <= tolerance`). A
check fails when a required value cannot be read.

## 5. Units and MCP rendering

Fusion internal lengths are centimetres. Tool parameters and returned values
use the units stated in the catalog; fields with unit suffixes (such as
`distance_cm` or `mass_g`) encode that unit in the name.

The MCP server renders a successful call as text content, including
tool-specific fields, deltas, and verification status. `render_view` can also
return an image content block. Application-level failures are returned with
MCP `isError: true`, along with the error tag, message, hints, and any
available mutation diagnostics.
