using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;

namespace Fusion360.Mcp.Server;

/// <summary>
/// Decision of <see cref="OperationJournal.Begin"/> for one incoming mutation.
/// </summary>
public enum JournalDecision
{
    /// <summary>No usable record — proceed: send the command to the backend.</summary>
    NewOperation,

    /// <summary>The operation already succeeded — return the recorded result, do NOT re-send.</summary>
    Replay,

    /// <summary>The retry is unsafe or inconsistent — return OPERATION_ID_CONFLICT without touching the model.</summary>
    Conflict,
}

/// <summary>
/// One <see cref="OperationJournal.Begin"/> outcome: the decision plus,
/// for a replay, the recorded result, and for a conflict, the message/hints.
/// </summary>
public sealed record BeginOutcome
{
    private BeginOutcome(
        JournalDecision decision,
        string operationId,
        JsonObject? result,
        string? conflictMessage,
        string[] conflictHints)
    {
        Decision = decision;
        OperationId = operationId;
        Result = result;
        ConflictMessage = conflictMessage;
        ConflictHints = conflictHints;
    }

    /// <summary>The decision taken.</summary>
    public JournalDecision Decision { get; }

    /// <summary>The operation id in play (client-supplied or server-generated UUID v4).</summary>
    public string OperationId { get; }

    /// <summary>The recorded result to return verbatim (replay only).</summary>
    public JsonObject? Result { get; }

    /// <summary>Human-readable conflict reason (conflict only).</summary>
    public string? ConflictMessage { get; }

    /// <summary>Contextual repair hints (conflict only).</summary>
    public string[] ConflictHints { get; }

    public static BeginOutcome NewOperation(string operationId)
        => new(JournalDecision.NewOperation, operationId, null, null, Array.Empty<string>());

    public static BeginOutcome Replay(string operationId, JsonObject result)
        => new(JournalDecision.Replay, operationId, result, null, Array.Empty<string>());

    public static BeginOutcome Conflict(string operationId, string message, params string[] hints)
        => new(JournalDecision.Conflict, operationId, null, message, hints);
}

/// <summary>
/// Durable, append-only operation journal on the MCP server side (P2,
/// ADR-0010; the KOMPAS journaling invariants, docs/decisions/idempotency.md):
///
/// - an <b>intent</b> line (operation_id + command + args hash + timestamp)
///   is written and flushed BEFORE the mutation is sent to the add-in; it is
///   durable and survives a server restart;
/// - the <b>terminal</b> line (outcome + recorded result) is appended AFTER;
///   an intent without a terminal means "in flight" (barrier, not a license
///   to start over) or "needs reconciliation" after a restart;
/// - a retry with the same operation_id:
///   - same args + successful terminal → the recorded result is returned
///     WITHOUT re-sending the command (dedup);
///   - same id, different args (or command) → <c>OPERATION_ID_CONFLICT</c>;
///   - clean failure (known outcome, no partial effects) → a new attempt with
///     the same id is allowed; P4: a bridge-structured TIMEOUT (an ok:false
///     result carrying partial_effects: false) is such a clean failure —
///     the bridge cancelled the command, so the model was not modified;
///   - unknown outcome (a LOST connection: a TIMEOUT whose structured
///     response never arrived, or a transport-level failure) or partial
///     effects → re-applying is not allowed: <c>OPERATION_ID_CONFLICT</c>,
///     reconcile the scene and use a fresh id;
/// - every write happens under an exclusive cross-process file lock
///   (NamedFileLock lesson); a torn tail (a truncated last line) is repaired
///   ONLY under that lock.
/// </summary>
// INVARIANT: intent lines are durable (flushed before the backend call); a
// retry that is "already executing" is a barrier, never a second command.
// History: docs/decisions/idempotency.md#durable-journal
public sealed class OperationJournal : IDisposable
{
    private sealed record Entry(string Command, string ArgsHash, JournalTerminal? Terminal);

    private sealed record JournalTerminal(bool Ok, bool OutcomeUnknown, JsonObject? Result);

    private readonly ConcurrentDictionary<string, Entry> _index = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _inFlight = new();
    private readonly List<SemaphoreSlim> _allBarriers = new();
    private readonly object _gate = new();
    private readonly object _lockGate = new();
    private readonly string _lockPath;
    private FileStream? _exclusiveLock;
    private bool _loaded;
    private bool _disposed;

    /// <summary>The journal file: a JSONL of intent/terminal lines in the journal directory.</summary>
    public string JournalPath { get; }

    /// <summary>How long a barrier wait may last for an in-flight operation (covers the 30 s bridge timeout).</summary>
    public TimeSpan BarrierTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Creates a journal in <paramref name="directory"/> (created on demand).</summary>
    public OperationJournal(string directory)
    {
        Directory.CreateDirectory(directory);
        JournalPath = Path.Combine(directory, "operation_journal.jsonl");
        _lockPath = JournalPath + ".lock";
    }

    /// <summary>
    /// Operations whose intent survived but whose terminal was lost
    /// (server restart mid-flight) — they need reconciliation and block
    /// same-id retries.
    /// </summary>
    public int PendingReconciliationCount
    {
        get
        {
            EnsureLoaded();
            return _index.Values.Count(e => e.Terminal is null);
        }
    }

    /// <summary>
    /// Adjudicates one incoming mutation: records a new intent (or allows a
    /// clean-failure retry), returns the recorded result for a dedup, or
    /// refuses with <c>OPERATION_ID_CONFLICT</c> material. The operation id
    /// is the client's when supplied, else a generated UUID v4.
    /// </summary>
    public BeginOutcome Begin(string command, JsonObject arguments, string? clientOperationId = null)
    {
        CheckDisposed();
        var operationId = NormalizeOperationId(clientOperationId) ?? Guid.NewGuid().ToString("D");
        var argsHash = ComputeArgsHash(arguments);
        EnsureLoaded();

        while (true)
        {
            if (_index.TryGetValue(operationId, out var entry) && entry.Terminal is { } terminal)
            {
                if (entry.Command != command)
                {
                    return BeginOutcome.Conflict(
                        operationId,
                        $"operation_id {operationId} was already used for command '{entry.Command}', not '{command}'",
                        "An operation_id belongs to exactly one operation (command + arguments).",
                        "Use a NEW operation_id for this command, or retry with the original command and arguments.");
                }

                if (entry.ArgsHash != argsHash)
                {
                    return BeginOutcome.Conflict(
                        operationId,
                        $"operation_id {operationId} was already used with different arguments",
                        "A retry must keep the same arguments as the first execution.",
                        "Retry with the original arguments under the same operation_id, or use a NEW operation_id for the changed operation.");
                }

                if (terminal.Ok)
                {
                    // Dedup: the operation already applied — return a fresh
                    // copy of the recorded result without re-sending it
                    // (TOOL_CONTRACTS §1.2).
                    var recorded = terminal.Result?.DeepClone() as JsonObject ?? new JsonObject();
                    recorded["operation_id"] = operationId;
                    return BeginOutcome.Replay(operationId, recorded);
                }

                if (terminal.OutcomeUnknown || HasPartialEffects(terminal.Result))
                {
                    return BeginOutcome.Conflict(
                        operationId,
                        $"operation {operationId} failed with an unknown outcome or partial effects — re-applying it is not safe",
                        "The first execution left (or may have left) changes in the scene.",
                        "Reconcile: check the scene with get_scene_info / deltas, then start a NEW operation with a fresh operation_id.");
                }

                // Clean failure (known, no partial effects): a new attempt
                // with the same operation_id is allowed (TOOL_CONTRACTS §3).
                if (StartNewAttempt(operationId, entry.Command, argsHash, EntrySignature(entry)))
                {
                    return BeginOutcome.NewOperation(operationId);
                }

                continue; // a concurrent attempt started meanwhile — wait for it below
            }

            if (_index.TryGetValue(operationId, out var pending) && _inFlight.TryGetValue(operationId, out var barrier))
            {
                // Barrier, not a license: an attempt is in flight (or just
                // finished) — wait for its terminal, then adjudicate against it.
                if (!barrier.Wait(BarrierTimeout))
                {
                    return BeginOutcome.Conflict(
                        operationId,
                        $"operation {operationId} is still in flight (no terminal record within {BarrierTimeout.TotalSeconds:0}s)",
                        "The first execution has not finished (or its terminal line was not recorded before a server restart).",
                        "Reconcile: verify the scene with get_scene_info, then retry with a NEW operation_id.");
                }

                continue; // re-adjudicate against the terminal that is now recorded
            }

            if (_index.TryGetValue(operationId, out _))
            {
                // Durable intent on disk, no terminal, nothing in flight: a
                // restart lost the outcome → reconciliation is required.
                return BeginOutcome.Conflict(
                    operationId,
                    $"operation {operationId} needs reconciliation: its terminal record is missing after a restart",
                    "The journal has the intent but no terminal — the first execution's outcome is unknown.",
                    "Reconcile: verify the scene with get_scene_info, then retry with a NEW operation_id.");
            }

            if (StartNewAttempt(operationId, command, argsHash, expectedSignature: null))
            {
                return BeginOutcome.NewOperation(operationId);
            }
        }
    }

    /// <summary>
    /// Records the terminal outcome of an in-flight operation: appends the
    /// terminal line (durable) and releases the in-flight barrier.
    /// </summary>
    /// <param name="result">The full result dict (success payload or ok:false error shape).</param>
    /// <param name="outcomeUnknown">
    /// True when the outcome is not safely dedupable (a TIMEOUT or a lost
    /// connection) — same-id retries are then refused.
    /// </param>
    public void Complete(string operationId, JsonObject result, bool outcomeUnknown)
    {
        CheckDisposed();
        EnsureLoaded();

        var terminalLine = new JsonObject
        {
            ["rec"] = "terminal",
            ["operation_id"] = operationId,
            ["ok"] = GetOk(result),
            ["outcome_unknown"] = outcomeUnknown,
            ["result"] = result.DeepClone(),
            ["ts"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
        var terminalRecord = new JournalTerminal(GetOk(result), outcomeUnknown, result.DeepClone() as JsonObject ?? new JsonObject());

        WriteLineUnderLock(terminalLine);
        var existing = _index.TryGetValue(operationId, out var entry)
            ? entry
            : new Entry("?", ComputeArgsHash(new JsonObject()), null);
        _index[operationId] = new Entry(existing.Command, existing.ArgsHash, terminalRecord);

        if (_inFlight.TryRemove(operationId, out var barrier))
        {
            barrier.Release(); // waiters (other in-process calls) may now proceed
        }
        // Deliberately NOT disposed here: in-process waiters may still hold a
        // reference; all barriers are disposed with the journal.
    }

    // ── attempt lifecycle ───────────────────────────────────────────

    /// <summary>
    /// Starts a new attempt (fresh operation or clean-failure retry):
    /// registers the in-flight barrier BEFORE the intent is visible, appends
    /// the durable intent, and holds the barrier for the attempt's duration
    /// (released by <see cref="Complete"/>). Returns false when a
    /// concurrent attempt already started for this id — the caller must then
    /// wait on that barrier instead.
    /// </summary>
    private bool StartNewAttempt(string operationId, string command, string argsHash, string? expectedSignature)
    {
        lock (_gate)
        {
            if (_inFlight.TryGetValue(operationId, out _))
            {
                return false; // a concurrent attempt is (re)starting — barrier, not a second command
            }

            Lock();
            try
            {
                // Another process may have appended an intent or terminal
                // since this instance loaded its in-memory index. Re-read
                // under the same lock that protects intent admission.
                LoadAndRepairTornTail();
                var currentSignature = _index.TryGetValue(operationId, out var current)
                    ? EntrySignature(current)
                    : null;
                if (!string.Equals(currentSignature, expectedSignature, StringComparison.Ordinal))
                {
                    return false;
                }

                var barrier = new SemaphoreSlim(1, 1);
                lock (_allBarriers)
                {
                    _allBarriers.Add(barrier);
                }
                _inFlight[operationId] = barrier;

                var intent = new JsonObject
                {
                    ["rec"] = "intent",
                    ["operation_id"] = operationId,
                    ["command"] = command,
                    ["args_hash"] = argsHash,
                    ["ts"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                AppendLineWithLock(intent);
                _index[operationId] = new Entry(command, argsHash, null);
            }
            finally
            {
                Unlock();
            }
        }

        _inFlight[operationId].Wait(); // hold for the attempt's duration
        return true;
    }

    // ── file + lock layer ───────────────────────────────────────────

    /// <summary>
    /// Loads the journal into the in-memory index, repairing a torn tail
    /// (truncated last line) — only under the exclusive lock.
    /// </summary>
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        Lock();
        try
        {
            LoadAndRepairTornTail();
            _loaded = true;
        }
        finally
        {
            Unlock();
        }
    }

    private void LoadAndRepairTornTail()
    {
        if (!File.Exists(JournalPath))
        {
            return;
        }

        var text = File.ReadAllText(JournalPath);
        var lines = text.Split('\n');
        var valid = new List<string>(lines.Length);
        var droppedTornTail = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            if (!TryParseObject(line, out _))
            {
                // Only an unterminated final line can be a torn append.
                // Never truncate a suffix after middle or newline-terminated
                // corruption: it may contain completed operation terminals.
                if (i == lines.Length - 1 && !text.EndsWith("\n", StringComparison.Ordinal))
                {
                    droppedTornTail = true;
                    break;
                }

                throw new InvalidDataException(
                    $"Operation journal corruption at line {i + 1}; refusing to discard later records.");
            }

            valid.Add(line);
        }

        if (droppedTornTail || !text.EndsWith("\n", StringComparison.Ordinal))
        {
            var repaired = string.Join("\n", valid) + (valid.Count > 0 ? "\n" : string.Empty);
            File.WriteAllText(JournalPath, repaired);
        }

        _index.Clear();
        foreach (var line in valid)
        {
            if (!TryParseObject(line, out var parsed))
            {
                continue;
            }

            var record = parsed!;
            var operationId = GetString(record, "operation_id");
            if (operationId is null)
            {
                continue;
            }

            switch (GetString(record, "rec"))
            {
                case "intent":
                    _index[operationId] = new Entry(
                        GetString(record, "command") ?? "?",
                        GetString(record, "args_hash") ?? string.Empty,
                        null);
                    break;
                case "terminal":
                    var terminalRecord = new JournalTerminal(
                        IsTrueBool(record, "ok"),
                        IsTrueBool(record, "outcome_unknown"),
                        record["result"] as JsonObject);
                    if (_index.TryGetValue(operationId, out var existing))
                    {
                        _index[operationId] = new Entry(existing.Command, existing.ArgsHash, terminalRecord);
                    }
                    else
                    {
                        _index[operationId] = new Entry("?", string.Empty, terminalRecord);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Appends one JSON line, flushed to disk BEFORE the caller sends the
    /// mutation (durable intent), under the exclusive cross-process lock.
    /// </summary>
    private void WriteLineUnderLock(JsonObject record)
    {
        Lock();
        try
        {
            AppendLineWithLock(record);
        }
        finally
        {
            Unlock();
        }
    }

    /// <summary>Appends one record while the caller holds the cross-process lock.</summary>
    private void AppendLineWithLock(JsonObject record)
    {
        // WriteThrough: the line must survive a crash (ADR-0010).
        using var stream = new FileStream(JournalPath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(record.ToJsonString() + "\n");
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string EntrySignature(Entry entry)
    {
        var terminal = entry.Terminal;
        return $"{entry.Command}\0{entry.ArgsHash}\0{terminal?.Ok}\0{terminal?.OutcomeUnknown}\0" +
               terminal?.Result?.ToJsonString();
    }

    /// <summary>
    /// Exclusive cross-process lock on <c>operation_journal.jsonl.lock</c>
    /// (the KOMPAS NamedFileLock lesson): open with FileShare.None and
    /// retry with backoff; held for the duration of one write/repair.
    /// </summary>
    private void Lock()
    {
        lock (_lockGate)
        {
            if (_exclusiveLock is not null)
            {
                return; // re-entrant within this process
            }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                try
                {
                    _exclusiveLock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);
                    return;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    private void Unlock()
    {
        lock (_lockGate)
        {
            _exclusiveLock?.Dispose();
            _exclusiveLock = null;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic hash of the arguments with <c>operation_id</c> stripped
    /// and every object's keys sorted (key order must not change the hash).
    /// </summary>
    public static string ComputeArgsHash(JsonObject arguments)
    {
        // The idempotency key itself must not change the arguments' hash:
        // retrying with the key re-supplied (or omitted) is the SAME
        // operation, not a different one.
        var stripped = arguments.DeepClone() as JsonObject ?? new JsonObject();
        stripped.Remove("operation_id");
        var canonical = Canonical(stripped) ?? stripped;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToJsonString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JsonNode? Canonical(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                var sorted = new JsonArray();
                foreach (var item in array)
                {
                    sorted.Add(Canonical(item));
                }

                return sorted;
            case JsonObject obj:
                var sortedObject = new JsonObject();
                foreach (var (key, value) in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    sortedObject[key] = Canonical(value);
                }

                return sortedObject;
            default:
                return node is null ? null : node.DeepClone();
        }
    }

    /// <summary>
    /// Parses one journal line as a JSON object. A torn tail (a truncated
    /// line from a crash mid-write) throws instead of returning null, so
    /// the repair path needs the exception form.
    /// </summary>
    private static bool TryParseObject(string line, [NotNullWhen(true)] out JsonObject? record)
    {
        try
        {
            record = JsonNode.Parse(line) as JsonObject;
            return record is not null;
        }
        catch (JsonException)
        {
            record = null;
            return false;
        }
    }

    private static bool IsTrueBool(JsonObject record, string key)
        => record.TryGetPropertyValue(key, out var node)
           && node is JsonValue value
           && value.TryGetValue<bool>(out var b)
           && b;

    private static bool GetOk(JsonObject result)
        => result.TryGetPropertyValue("ok", out var ok) && ok is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static bool HasPartialEffects(JsonObject? result)
        => result is not null
           && result.TryGetPropertyValue("partial_effects", out var node)
           && node is JsonValue value
           && value.TryGetValue<bool>(out var b)
           && b;

    private static string? GetString(JsonObject record, string key)
        => record.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static string? NormalizeOperationId(string? clientOperationId)
    {
        var trimmed = clientOperationId?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!Guid.TryParse(trimmed, out _))
        {
            throw new ArgumentException($"operation_id must be a UUID (got '{clientOperationId}').", nameof(clientOperationId));
        }

        return trimmed.ToLowerInvariant();
    }

    private void CheckDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(OperationJournal));
    }

    /// <summary>Releases the lock file and in-flight barriers.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_allBarriers)
        {
            foreach (var barrier in _allBarriers)
            {
                barrier.Dispose();
            }

            _allBarriers.Clear();
        }

        _inFlight.Clear();
        Unlock();
        _disposed = true;
    }
}
