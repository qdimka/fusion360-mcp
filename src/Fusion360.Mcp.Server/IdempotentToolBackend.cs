using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.Server;

/// <summary>
/// IToolBackend decorator that makes mutation tools idempotent (P2,
/// ADR-0010): every mutation goes through an <see cref="OperationJournal"/>
/// — the intent is durable before the command is sent, the terminal outcome
/// after. A retry with the same <c>operation_id</c> and the same arguments
/// returns the recorded result WITHOUT a second backend call; the same id
/// with different arguments (or a different command) is refused with
/// <c>OPERATION_ID_CONFLICT</c> before touching the model.
///
/// Read-only tools bypass the journal entirely. The operation_id comes from
/// the client's arguments when supplied, else the server generates a UUID
/// v4 and returns it in the result (TOOL_CONTRACTS §1.2).
/// </summary>
// INVARIANT: "already executing" is a barrier (wait for the first attempt's
// terminal), never a second command; a clean failure may retry with the same
// id, an unknown outcome / partial effects may not.
// History: docs/decisions/idempotency.md#operation-id
public sealed class IdempotentToolBackend : IToolBackend, IDisposable
{
    private readonly IToolBackend _inner;
    private readonly OperationJournal _journal;

    public IdempotentToolBackend(IToolBackend inner, OperationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    /// <summary>The backend this decorator forwards to.</summary>
    public IToolBackend Inner => _inner;

    /// <summary>The journal this decorator writes to.</summary>
    public OperationJournal Journal => _journal;

    /// <inheritdoc />
    public JsonObject Invoke(string toolName, JsonObject arguments)
    {
        // Only mutations are journaled — read-only tools stay side-effect
        // free and need no idempotency key.
        if (!IsMutation(toolName))
        {
            return _inner.Invoke(toolName, arguments);
        }

        BeginOutcome outcome;
        try
        {
            outcome = _journal.Begin(toolName, arguments, ReadOperationId(arguments));
        }
        catch (ArgumentException ex)
        {
            // A non-UUID operation_id from the client: a parameter error,
            // not a journal state — reject it as classified INVALID_INPUT.
            return new JsonObject
            {
                ["ok"] = false,
                ["error_kind"] = ErrorKind.InvalidInput.ToWireString(),
                ["error_message"] = ex.Message,
                ["hints"] = new JsonArray { "operation_id must be a UUID (e.g. \"3f2504e0-4f89-41d3-9a0c-0305e82c3301\")." },
            };
        }

        switch (outcome.Decision)
        {
            case JournalDecision.Replay:
                // Dedup: the operation already applied — the recorded result
                // (with its operation_id) comes back without a backend call.
                return outcome.Result!;

            case JournalDecision.Conflict:
                return new JsonObject
                {
                    ["ok"] = false,
                    ["error_kind"] = ErrorKind.OperationIdConflict.ToWireString(),
                    ["error_message"] = outcome.ConflictMessage ?? "operation_id conflict",
                    ["hints"] = HintsJson(outcome.ConflictHints),
                    ["operation_id"] = outcome.OperationId,
                };
        }

        // NewOperation: the intent is already durable; send the command.
        JsonObject result;
        var outcomeUnknown = false;
        try
        {
            result = _inner.Invoke(toolName, arguments);
        }
        catch (FusionError ex)
        {
            // Structured add-in error frame. P4: a bridge TIMEOUT no longer
            // arrives here — the bridge returns it as an ok:false result
            // carrying partial_effects: false, which the normal path below
            // records as a clean failure (known, no effects). The TIMEOUT
            // that CAN arrive here is a socket-level timeout / lost
            // response (the add-in never answered): whether the mutation
            // applied is NOT known, so the terminal is recorded as
            // outcome-unknown and a same-id retry is refused.
            outcomeUnknown = ex.OutcomeUnknown || ex.Kind is ErrorKind.Timeout;
            result = new JsonObject
            {
                ["ok"] = false,
                ["error_kind"] = ex.Kind.ToWireString(),
                ["error_message"] = ex.Message,
            };
            if (outcomeUnknown)
            {
                result["outcome_unknown"] = true;
            }
            if (ex.Hints.Count > 0)
            {
                result["hints"] = HintsJson(ex.Hints);
            }

            _journal.Complete(outcome.OperationId, result, outcomeUnknown);
            throw; // the server's catch path renders FusionErrorResult as before
        }
        catch
        {
            // Transport-level failure (not connected / lost): the command may
            // or may not have reached the add-in — outcome unknown. Record it
            // and let the exception propagate to the connection-error path.
            outcomeUnknown = true;
            var lost = new JsonObject
            {
                ["ok"] = false,
                ["error_kind"] = ErrorKind.Unknown.ToWireString(),
                ["error_message"] = "connection to the add-in was lost",
            };
            _journal.Complete(outcome.OperationId, lost, outcomeUnknown);
            throw;
        }

        outcomeUnknown = result["outcome_unknown"]?.GetValue<bool>() == true
            || (result["ok"]?.GetValue<bool>() == false && result["partial_effects"] is null);
        _journal.Complete(outcome.OperationId, result, outcomeUnknown);
        return WithOperationId(result, outcome.OperationId);
    }

    /// <inheritdoc /> Forwards to the inner backend; the journal has no
    /// connection of its own to drop.
    public void Reset() => _inner.Reset();

    /// <summary>Releases the journal (lock file + in-flight barriers).</summary>
    public void Dispose() => _journal.Dispose();

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>True when the command can cause a document or external effect.</summary>
    private static bool IsMutation(string toolName) => ToolEffects.OperationCommands.Contains(toolName);

    private static string? ReadOperationId(JsonObject arguments)
    {
        if (!arguments.TryGetPropertyValue("operation_id", out var node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var id) ? id : null;
    }

    private static JsonObject WithOperationId(JsonObject result, string operationId)
    {
        result["operation_id"] = operationId;
        return result;
    }

    private static JsonArray HintsJson(string[]? hints)
    {
        var array = new JsonArray();
        if (hints is not null)
        {
            foreach (var hint in hints)
            {
                array.Add(JsonValue.Create(hint));
            }
        }

        return array;
    }

    private static JsonArray HintsJson(IReadOnlyList<string> hints)
    {
        var array = new JsonArray();
        foreach (var hint in hints)
        {
            array.Add(JsonValue.Create(hint));
        }

        return array;
    }
}
