using System.Text.Json.Nodes;
using Fusion360.Mcp.Server;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>
/// P2 (ADR-0010): the durable operation journal — KOMPAS invariants:
/// durable intent before send, terminal after; dedup without re-send;
/// OPERATION_ID_CONFLICT material for unsafe retries; torn-tail repair
/// under the exclusive lock; the barrier semantics ("already executing"
/// waits, it never starts over).
/// </summary>
public class OperationJournalTests
{
    private static JsonObject Args(double distance = 2.0)
        => new() { ["sketch"] = "S1", ["distance"] = distance };

    private static JsonObject Success()
        => new() { ["ok"] = true, ["body_name"] = "Body1" };

    [Fact]
    public void Begin_FreshOperation_ReturnsNewOperation_AndPersistsDurableIntent()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);

        var outcome = journal.Begin("extrude", Args());

        Assert.Equal(JournalDecision.NewOperation, outcome.Decision);
        Assert.True(Guid.TryParse(outcome.OperationId, out _), "generated operation id must be a UUID");

        // The intent line is durable ON DISK before any backend call.
        var lines = File.ReadAllLines(journal.JournalPath);
        var intent = lines.Single(l => l.Contains("\"rec\":\"intent\""));
        Assert.Contains(outcome.OperationId, intent);
        Assert.Contains("\"extrude\"", intent);
        Assert.Contains("args_hash", intent);
    }

    [Fact]
    public async Task TwoJournalInstances_AdmitOnlyOneConcurrentOperationId()
    {
        using var dir = new TempDir();
        using var firstJournal = new OperationJournal(dir.Dir);
        using var secondJournal = new OperationJournal(dir.Dir);
        _ = firstJournal.PendingReconciliationCount;
        _ = secondJournal.PendingReconciliationCount;
        using var start = new ManualResetEventSlim(false);
        var id = Guid.NewGuid().ToString("D");

        var first = Task.Run(() =>
        {
            start.Wait();
            return firstJournal.Begin("create_sketch", Args(), id);
        });
        var second = Task.Run(() =>
        {
            start.Wait();
            return secondJournal.Begin("create_sketch", Args(), id);
        });
        start.Set();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, o => o.Decision == JournalDecision.NewOperation);
        Assert.Single(outcomes, o => o.Decision == JournalDecision.Conflict);
        Assert.Single(File.ReadAllLines(firstJournal.JournalPath),
            line => line.Contains("\"rec\":\"intent\"", StringComparison.Ordinal));

        var owner = outcomes[0].Decision == JournalDecision.NewOperation ? firstJournal : secondJournal;
        owner.Complete(id, new JsonObject { ["ok"] = false, ["outcome_unknown"] = true }, outcomeUnknown: true);
    }

    [Fact]
    public void Complete_Success_ThenBegin_SameIdAndArgs_ReturnsReplay_WithoutResend()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        var first = journal.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.NewOperation, first.Decision);
        journal.Complete(id, Success(), outcomeUnknown: false);

        var retry = journal.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.Replay, retry.Decision);
        Assert.NotNull(retry.Result);
        Assert.True(retry.Result!.TryGetPropertyValue("operation_id", out var opId) && opId!.GetValue<string>() == id,
            "the replayed result must carry the operation id");
        Assert.Equal("Body1", retry.Result!["body_name"]?.GetValue<string>());
    }

    [Fact]
    public void Begin_SameId_DifferentArgs_ReturnsConflict()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        journal.Begin("extrude", Args(2.0), id);
        journal.Complete(id, Success(), outcomeUnknown: false);

        var retry = journal.Begin("extrude", Args(5.0), id);
        Assert.Equal(JournalDecision.Conflict, retry.Decision);
        Assert.NotNull(retry.ConflictMessage);
        Assert.NotEmpty(retry.ConflictHints);
    }

    [Fact]
    public void Begin_SameId_DifferentCommand_ReturnsConflict()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        journal.Begin("extrude", Args(), id);
        journal.Complete(id, Success(), outcomeUnknown: false);

        var retry = journal.Begin("fillet", Args(), id);
        Assert.Equal(JournalDecision.Conflict, retry.Decision);
    }

    [Fact]
    public void Begin_AfterCleanFailure_ReturnsNewAttempt_WithSameId()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        journal.Begin("extrude", Args(), id);
        var failed = new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = "PROFILE_NOT_CLOSED",
            ["error_message"] = "No profiles in sketch",
        };
        journal.Complete(id, failed, outcomeUnknown: false);

        // Clean failure (known outcome, no partial effects): a new attempt
        // with the same id is allowed (TOOL_CONTRACTS §3).
        var retry = journal.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.NewOperation, retry.Decision);

        var intents = File.ReadAllLines(journal.JournalPath).Count(l => l.Contains("\"rec\":\"intent\""));
        Assert.Equal(2, intents);
    }

    [Fact]
    public void Begin_AfterUnknownOutcome_ReturnsConflict()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        journal.Begin("extrude", Args(), id);
        journal.Complete(id, new JsonObject { ["ok"] = false, ["error_kind"] = "TIMEOUT" }, outcomeUnknown: true);

        var retry = journal.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.Conflict, retry.Decision);
    }

    [Fact]
    public void Begin_AfterPartialEffects_ReturnsConflict()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        journal.Begin("boolean_operation", Args(), id);
        journal.Complete(id, new JsonObject { ["ok"] = false, ["partial_effects"] = true }, outcomeUnknown: false);

        var retry = journal.Begin("boolean_operation", Args(), id);
        Assert.Equal(JournalDecision.Conflict, retry.Decision);
    }

    [Fact]
    public void TornTail_IsRepaired_OnLoad_AndTheSuccessRecordSurvives()
    {
        using var dir = new TempDir();
        var dirPath = dir.Dir;
        var id = Guid.NewGuid().ToString("D");

        using (var journal = new OperationJournal(dirPath))
        {
            journal.Begin("extrude", Args(), id);
            journal.Complete(id, Success(), outcomeUnknown: false);
        }

        // Simulate a crash mid-write of the next intent line.
        File.AppendAllText(
            Path.Combine(dirPath, "operation_journal.jsonl"),
            "{\"rec\":\"intent\",\"operation_id\":\"" + id + "\",\"command\":\"extrude\""); // truncated, no newline

        // A fresh process (new journal instance) repairs the tail and keeps
        // the valid prefix — the success record must still be replayable.
        using var journal2 = new OperationJournal(dirPath);
        var retry = journal2.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.Replay, retry.Decision);

        foreach (var line in File.ReadAllLines(journal2.JournalPath))
        {
            if (line.Length > 0)
            {
                Assert.NotNull(JsonNode.Parse(line));
            }
        }
    }

    [Fact]
    public void CorruptMiddleRecord_FailsClosedWithoutTruncatingSuffix()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Dir, "operation_journal.jsonl");
        var contents = "{\"rec\":\"intent\",\"operation_id\":\"first\",\"command\":\"extrude\",\"args_hash\":\"a\"}\n"
            + "{bad middle record}\n"
            + "{\"rec\":\"terminal\",\"operation_id\":\"later\",\"ok\":true}\n";
        File.WriteAllText(path, contents);
        using var journal = new OperationJournal(dir.Dir);

        Assert.Throws<InvalidDataException>(() => _ = journal.PendingReconciliationCount);
        Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void Begin_AfterRestartWithMissingTerminal_NeedsReconciliation()
    {
        using var dir = new TempDir();
        var dirPath = dir.Dir;
        var id = Guid.NewGuid().ToString("D");

        using (var journal = new OperationJournal(dirPath))
        {
            var outcome = journal.Begin("extrude", Args(), id);
            Assert.Equal(JournalDecision.NewOperation, outcome.Decision);
            Assert.Equal(1, journal.PendingReconciliationCount); // intent without terminal, still in flight
        }
        // No Complete — the server died mid-flight.

        using var journal2 = new OperationJournal(dirPath);
        Assert.Equal(1, journal2.PendingReconciliationCount);

        var retry = journal2.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.Conflict, retry.Decision);
        Assert.Contains("reconciliation", retry.ConflictMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Begin_SameIdWhileFirstAttemptInFlight_IsABarrier_NotASecondCommand()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var id = Guid.NewGuid().ToString("D");

        var first = journal.Begin("extrude", Args(), id);
        Assert.Equal(JournalDecision.NewOperation, first.Decision);

        // The second, same-id call must WAIT for the first attempt's
        // terminal — not start a second command.
        var second = Task.Run(() => journal.Begin("extrude", Args(), id));
        await Task.Delay(300);
        Assert.False(second.IsCompleted, "same-id call must block on the in-flight barrier");

        journal.Complete(id, Success(), outcomeUnknown: false);

        var finished = await second;
        Assert.Equal(JournalDecision.Replay, finished.Decision);
        Assert.Equal("Body1", finished.Result!["body_name"]?.GetValue<string>());
    }

    [Fact]
    public async Task Begin_BlocksWhileAnotherProcessHoldsTheJournalLock()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);
        var lockPath = journal.JournalPath + ".lock";

        var holder = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var task = Task.Run(() => journal.Begin("extrude", Args()));

        await Task.Delay(500);
        Assert.False(task.IsCompleted, "Begin must respect the cross-process lock, not proceed");

        holder.Close();
        var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(winner, task), "lock wait did not finish within 10s");
        var outcome = await task;
        Assert.Equal(JournalDecision.NewOperation, outcome.Decision);
    }

    [Fact]
    public void Begin_MalformedOperationId_ThrowsArgumentError()
    {
        using var dir = new TempDir();
        using var journal = new OperationJournal(dir.Dir);

        Assert.Throws<ArgumentException>(() => journal.Begin("extrude", Args(), "not-a-uuid"));
    }

    [Fact]
    public void ComputeArgsHash_IsKeyOrderIndependent_AndIgnoresOperationId()
    {
        var a = new JsonObject
        {
            ["a"] = 1,
            ["nested"] = new JsonObject { ["x"] = 2, ["y"] = 3 },
        };
        var b = new JsonObject
        {
            ["nested"] = new JsonObject { ["y"] = 3, ["x"] = 2 },
            ["a"] = 1,
        };
        Assert.Equal(OperationJournal.ComputeArgsHash(a), OperationJournal.ComputeArgsHash(b));

        Assert.Equal(
            OperationJournal.ComputeArgsHash(new JsonObject { ["a"] = 1 }),
            OperationJournal.ComputeArgsHash(new JsonObject { ["a"] = 1, ["operation_id"] = "3f2504e0-4f89-41d3-9a0c-0305e82c3301" }));
    }

    /// <summary>A fresh, self-cleaning temp directory (DirectoryInfo is not IDisposable on net10).</summary>
    private sealed class TempDir : IDisposable
    {
        public string Dir { get; }

        public TempDir()
        {
            Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fusion360mcp_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Dir);
        }

        public void Dispose() => System.IO.Directory.Delete(Dir, recursive: true);
    }
}
