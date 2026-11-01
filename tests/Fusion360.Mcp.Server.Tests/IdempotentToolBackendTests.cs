using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;
using Fusion360.Mcp.Server;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>
/// P2 (ADR-0010): the IToolBackend decorator that journals mutations.
/// Read-only tools bypass the journal; mutations get a durable intent
/// before send, a terminal after, and OPERATION_ID_CONFLICT material on
/// unsafe retries — without a second backend call ever going out.
/// </summary>
public class IdempotentToolBackendTests
{
    /// <summary>A controllable inner backend that records every call.</summary>
    private sealed class FakeBackend : IToolBackend
    {
        public readonly List<string> Calls = new();
        public Func<string, JsonObject, JsonObject>? Behavior { get; set; }

        public JsonObject Invoke(string toolName, JsonObject arguments)
        {
            Calls.Add(toolName);
            return Behavior?.Invoke(toolName, arguments)
                   ?? new JsonObject { ["ok"] = true, ["tool"] = toolName, ["body_name"] = "Body1" };
        }

        public void Reset() { }
    }

    private static (IdempotentToolBackend backend, FakeBackend inner, OperationJournal journal) Setup()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), "fusion360mcp_idem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dirPath);
        var inner = new FakeBackend();
        var journal = new OperationJournal(dirPath);
        return (new IdempotentToolBackend(inner, journal), inner, journal);
    }

    [Fact]
    public void ReadOnlyTool_BypassesTheJournal()
    {
        var (backend, inner, journal) = Setup();
        using var _dispose = backend;

        var result = backend.Invoke("get_scene_info", new JsonObject());

        Assert.Contains("get_scene_info", inner.Calls);
        Assert.False(result.TryGetPropertyValue("operation_id", out _), "read-only results must not carry an operation id");
        Assert.False(File.Exists(journal.JournalPath), "read-only tools must not write journal lines");
    }

    [Fact]
    public void Mutation_InjectsAGeneratedOperationId()
    {
        var (backend, inner, journal) = Setup();
        using var _dispose = backend;

        var result = backend.Invoke("extrude", new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0 });

        Assert.True(result.TryGetPropertyValue("operation_id", out var opId), "mutation results carry the generated operation id");
        Assert.True(Guid.TryParse(opId!.GetValue<string>(), out _), "generated id must be a UUID");
        Assert.Contains("extrude", inner.Calls);
        Assert.Contains("\"rec\":\"terminal\"", File.ReadAllText(journal.JournalPath));
    }

    [Theory]
    [InlineData("create_sketch")]
    [InlineData("delete_parameter")]
    [InlineData("export_step")]
    public void EveryDocumentOrFileSideEffect_IsJournaled(string command)
    {
        var (backend, inner, journal) = Setup();
        using var _dispose = backend;

        var result = backend.Invoke(command, new JsonObject());

        Assert.Contains(command, inner.Calls);
        Assert.True(result.TryGetPropertyValue("operation_id", out _));
        Assert.Contains("\"rec\":\"terminal\"", File.ReadAllText(journal.JournalPath));
    }

    [Fact]
    public void Retry_SameIdSameArgs_ReturnsReplay_WithoutASecondBackendCall()
    {
        var (backend, inner, _) = Setup();
        var args = new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0, ["operation_id"] = Guid.NewGuid().ToString("D") };

        var first = backend.Invoke("extrude", args);
        var retry = backend.Invoke("extrude", args);

        Assert.Single(inner.Calls); // dedup: no second backend call
        Assert.Equal(first["body_name"]?.ToJsonString(), retry["body_name"]?.ToJsonString());
        Assert.True(retry.TryGetPropertyValue("operation_id", out _), "the replayed result still carries the id");
    }

    [Fact]
    public void Retry_SameIdDifferentArgs_IsAnOperationIdConflict()
    {
        var (backend, inner, _) = Setup();
        var id = Guid.NewGuid().ToString("D");

        backend.Invoke("extrude", new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0, ["operation_id"] = id });
        var retry = backend.Invoke("extrude", new JsonObject { ["sketch"] = "S1", ["distance"] = 5.0, ["operation_id"] = id });

        Assert.Single(inner.Calls);
        Assert.False((bool)retry["ok"]!);
        Assert.Equal("OPERATION_ID_CONFLICT", retry["error_kind"]!.GetValue<string>());
        Assert.Equal(id, retry["operation_id"]!.GetValue<string>());
        Assert.NotNull(retry["hints"]);
    }

    [Fact]
    public void Retry_SameIdDifferentCommand_IsAnOperationIdConflict()
    {
        var (backend, inner, _) = Setup();
        var id = Guid.NewGuid().ToString("D");

        backend.Invoke("extrude", new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0, ["operation_id"] = id });
        var retry = backend.Invoke("fillet", new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0, ["operation_id"] = id });

        Assert.Single(inner.Calls);
        Assert.Equal("OPERATION_ID_CONFLICT", retry["error_kind"]!.GetValue<string>());
    }

    [Fact]
    public void MalformedOperationId_IsInvalidInput_AndNeverReachesTheBackend()
    {
        var (backend, inner, _) = Setup();

        var result = backend.Invoke("extrude", new JsonObject { ["sketch"] = "S1", ["operation_id"] = "not-a-uuid" });

        Assert.Empty(inner.Calls);
        Assert.Equal("INVALID_INPUT", result["error_kind"]!.GetValue<string>());
    }

    [Fact]
    public void CleanFailure_AllowsARetryWithTheSameId()
    {
        var (backend, inner, _) = Setup();
        inner.Behavior = (_, _) => new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = "PROFILE_NOT_CLOSED",
            ["error_message"] = "No profiles in sketch",
            ["partial_effects"] = false,
        };
        var id = Guid.NewGuid().ToString("D");
        var args = new JsonObject { ["sketch"] = "S1", ["operation_id"] = id };

        var first = backend.Invoke("extrude", args);
        Assert.Equal("PROFILE_NOT_CLOSED", first["error_kind"]!.GetValue<string>());

        inner.Behavior = (_, _) => new JsonObject { ["ok"] = true, ["body_name"] = "Body1" };
        var retry = backend.Invoke("extrude", args);

        Assert.Equal(2, inner.Calls.Count); // clean failure: a new attempt is allowed
        Assert.True((bool)retry["ok"]!);
    }

    [Fact]
    public void Timeout_RecordsOutcomeUnknown_AndRefusesASameIdRetry()
    {
        var (backend, inner, _) = Setup();
        inner.Behavior = (_, _) => throw new FusionError("bridge timeout", ErrorKind.Timeout);
        var id = Guid.NewGuid().ToString("D");
        var args = new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0, ["operation_id"] = id };

        Assert.Throws<FusionError>(() => backend.Invoke("extrude", args));

        // The retry must be refused WITHOUT reaching the backend again:
        // the outcome of the timed-out attempt is unknown.
        var retry = backend.Invoke("extrude", args);
        Assert.Single(inner.Calls);
        Assert.Equal("OPERATION_ID_CONFLICT", retry["error_kind"]!.GetValue<string>());
        var hints = retry["hints"]!.AsArray().Select(n => n?.GetValue<string>() ?? string.Empty).ToList();
        Assert.Contains(hints, h => h.Contains("Reconcile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StructuredUnknownOutcome_RefusesSameIdRetry()
    {
        var (backend, inner, _) = Setup();
        inner.Behavior = (_, _) => new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = "TIMEOUT",
            ["outcome_unknown"] = true,
        };
        var id = Guid.NewGuid().ToString("D");
        var args = new JsonObject { ["sketch"] = "S1", ["operation_id"] = id };

        backend.Invoke("extrude", args);
        var retry = backend.Invoke("extrude", args);

        Assert.Single(inner.Calls);
        Assert.Equal("OPERATION_ID_CONFLICT", retry["error_kind"]!.GetValue<string>());
    }

    [Fact]
    public void TransportLoss_RecordsOutcomeUnknown_AndRefusesASameIdRetry()
    {
        var (backend, inner, _) = Setup();
        inner.Behavior = (_, _) => throw new ConnectionException("add-in not connected");
        var id = Guid.NewGuid().ToString("D");
        var args = new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0, ["operation_id"] = id };

        Assert.Throws<ConnectionException>(() => backend.Invoke("extrude", args));

        var retry = backend.Invoke("extrude", args);
        Assert.Single(inner.Calls);
        Assert.Equal("OPERATION_ID_CONFLICT", retry["error_kind"]!.GetValue<string>());
    }
}
