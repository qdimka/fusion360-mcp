// The blocking task waits below are deliberate: every waited-on task
// completes on a different thread (Task.Run / dedicated pump threads), so
// there is no sync-over-async deadlock risk — xUnit1031 is suppressed
// for this file.
#pragma warning disable xUnit1031
using System.Text.Json.Nodes;
using Xunit;
using Fusion360.Mcp.AddIn;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// EventBridge semantics ported from the reference addon/server/event_bridge.py:
/// ping fast path, main-thread marshalling, timeout + cancelled skip, stop
/// teardown, and the classified error frames.
/// </summary>
public sealed class EventBridgeTests : IDisposable
{
    private readonly string _logPath;
    private readonly McpLogger _log;

    public EventBridgeTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"fusion360mcp-addintests-{Guid.NewGuid():N}.log");
        McpLog.Configure(_logPath);
        _log = McpLog.ByName("test");
    }

    public void Dispose()
    {
        McpLog.Configure(null);
        try
        {
            File.Delete(_logPath);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Ping_FastPath_ReturnsPongWithoutQueueing()
    {
        var dispatcher = new FakeDispatcher();
        var handler = new StubHandler();
        var bridge = new EventBridge(dispatcher, handler, _log);

        try
        {
            var response = bridge.Submit(JsonNode.Parse("""{"type":"ping","params":{}}""")!);
            var frame = ProtocolCodec.ParseResponse(response.ToJson());

            Assert.True(frame.IsSuccess);
            Assert.True(frame.Result!.Ok);
            Assert.Equal("pong", frame.Result.Field("status")!.GetValue<string>());

            // The fast path never pokes the main thread and never reaches the handler.
            Assert.Null(dispatcher.LastFired);
            Assert.Equal(0, handler.Calls);
        }
        finally
        {
            bridge.Stop();
        }
    }

    [Fact]
    public void Command_IsMarshalledToTheMainThread_AndReturnsTheHandlerFrame()
    {
        var dispatcher = new FakeDispatcher();
        var handler = new StubHandler();
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        handler.Override = frame =>
        {
            started.Set(); // the handler is now running on the fake main thread
            release.Wait(5000);
            return AddInResponse.Success(CommandResult.Success(new JsonObject { ["status"] = "scene" }));
        };

        var bridge = new EventBridge(dispatcher, handler, _log);
        try
        {
            var task = Task.Run(() => bridge.Submit(JsonNode.Parse("""{"type":"get_scene_info","params":{}}""")!, 10.0));

            // Drain on a dedicated "main" thread — as the adsk CustomEvent would.
            // Pump until the handler has started (the enqueue is async on the
            // Task.Run thread, so an early no-op pump is retried).
            var mainThread = new Thread(() =>
            {
                while (!started.IsSet)
                {
                    dispatcher.Pump();
                    Thread.Sleep(20);
                }
            })
            {
                Name = "fake-main",
                IsBackground = true,
            };
            mainThread.Start();
            Assert.True(started.Wait(5000), "handler never started on the fake main thread");
            Assert.Equal("fake-main", handler.ExecutedOn);

            release.Set();
            mainThread.Join(5000);
            Assert.True(task.Wait(5000));

            var frame = ProtocolCodec.ParseResponse(task.Result.ToJson());
            Assert.True(frame.IsSuccess);
            Assert.Equal("scene", frame.Result!.Field("status")!.GetValue<string>());
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            release.Set();
            bridge.Stop();
        }
    }

    [Fact]
    public void DrainQueue_ReentrantPumpDoesNotRunNestedCommand()
    {
        var dispatcher = new FakeDispatcher();
        var calls = 0;
        var handler = new StubHandler
        {
            Override = _ =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    dispatcher.Pump();
                    Assert.Equal(1, Volatile.Read(ref calls));
                }
                return AddInResponse.Success(CommandResult.Success(new JsonObject()));
            },
        };
        var bridge = new EventBridge(dispatcher, handler, _log);
        try
        {
            var first = Task.Run(() => bridge.Submit(
                JsonNode.Parse("""{"type":"get_scene_info","params":{}}""")!, 5.0));
            Assert.True(SpinWait.SpinUntil(() => dispatcher.FireCount >= 1, 3000));
            var second = Task.Run(() => bridge.Submit(
                JsonNode.Parse("""{"type":"get_object_info","params":{}}""")!, 5.0));
            Assert.True(SpinWait.SpinUntil(() => dispatcher.FireCount >= 2, 3000));

            dispatcher.Pump();

            Assert.True(Task.WaitAll(new Task[] { first, second }, 3000));
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally
        {
            bridge.Stop();
        }
    }

    [Fact]
    public void HandlerException_BecomesAClassifiedErrorFrame()
    {
        var dispatcher = new SyncDispatcher();
        var handler = new StubHandler { ThrowOnce = new InvalidOperationException("No active design") };
        var bridge = new EventBridge(dispatcher, handler, _log);

        try
        {
            var response = bridge.Submit(JsonNode.Parse("""{"type":"extrude","params":{"body":"x"}}""")!, 5.0);
            var frame = ProtocolCodec.ParseResponse(response.ToJson());

            Assert.True(frame.IsError);
            Assert.Equal(ErrorKind.NoActiveDesign, frame.ErrorKind);
            Assert.Contains("No active design", frame.Message!);
            Assert.NotEmpty(frame.Hints);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            bridge.Stop();
        }
    }

    [Fact]
    public void Timeout_CancelsTheItem_AndDrainSkipsTheLateExecution()
    {
        var dispatcher = new FakeDispatcher();
        var handler = new StubHandler();
        var release = new ManualResetEventSlim(false);
        handler.Override = frame =>
        {
            release.Wait(5000); // simulate a long main-thread command
            return AddInResponse.Success(CommandResult.Success(new JsonObject { ["status"] = "late" }));
        };

        var bridge = new EventBridge(dispatcher, handler, _log);
        try
        {
            // The fake dispatcher never drains, so the submit times out and cancels the item.
            var response = bridge.Submit(JsonNode.Parse("""{"type":"extrude","params":{"body":"x"}}""")!, 0.3);
            var frame = ProtocolCodec.ParseResponse(response.ToJson());
            // P4: the timeout is a Success frame carrying a structured
            // ok:false result — not a bare infrastructure error frame —
            // that states partial_effects: false explicitly (the cancelled
            // item never executes, so the model was not modified,
            // TOOL_CONTRACTS §5.2).
            Assert.True(frame.IsSuccess);
            var result = frame.Result!;
            Assert.True(result.Fields["ok"]!.GetValue<bool>() == false);
            Assert.Equal("TIMEOUT", result.Fields["error_kind"]!.GetValue<string>());
            Assert.False(result.Fields["partial_effects"]!.GetValue<bool>(),
                "a cancelled timeout must state partial_effects: false");
            Assert.Contains("timed out", result.Fields["error_message"]!.GetValue<string>());
            Assert.Equal(0, handler.Calls);

            // The "long command" now completes on the main thread — but the item is
            // cancelled, so drain must skip it (reference: no late execution).
            release.Set();
            dispatcher.Pump();
            Assert.Equal(0, handler.Calls);

            // The bridge is still usable for new commands.
            var task = Task.Run(() => bridge.Submit(JsonNode.Parse("""{"type":"get_scene_info","params":{}}""")!, 5.0));
            var drained = new ManualResetEventSlim(false);
            handler.Override = frame =>
            {
                var response = AddInResponse.Success(CommandResult.Success(new JsonObject { ["status"] = "second" }));
                drained.Set();
                return response;
            };
            var mainThread = new Thread(() =>
            {
                while (!drained.IsSet && !task.IsCompleted)
                {
                    dispatcher.Pump();
                    Thread.Sleep(20);
                }
            })
            {
                IsBackground = true,
            };
            mainThread.Start();
            Assert.True(task.Wait(6000));
            mainThread.Join(5000);

            var second = ProtocolCodec.ParseResponse(task.Result.ToJson());
            Assert.True(second.IsSuccess);
            Assert.Equal("second", second.Result!.Field("status")!.GetValue<string>());
        }
        finally
        {
            release.Set();
            bridge.Stop();
        }
    }

    [Fact]
    public void Timeout_ResponseStatesPartialEffectsFalseExplicitly()
    {
        var dispatcher = new FakeDispatcher();
        var handler = new StubHandler();
        var bridge = new EventBridge(dispatcher, handler, _log);
        try
        {
            // The fake dispatcher never drains, so the submit times out and the
            // bridge cancels the item. The TIMEOUT response is a structured
            // ok:false result (not a bare infrastructure error frame).
            var response = bridge.Submit(JsonNode.Parse("""{"type":"extrude","params":{"body":"x"}}""")!, 0.3);
            var result = ProtocolCodec.ParseResponse(response.ToJson()).Result!;

            // P4 (TOOL_CONTRACTS §5.2): every TIMEOUT states
            // partial_effects: false explicitly — the cancelled item never
            // executes after the deadline, so the model was not modified.
            Assert.False(result.Ok);
            Assert.Equal(ErrorKind.Timeout, result.ErrorKind);
            Assert.NotNull(result.PartialEffects);
            Assert.False(result.PartialEffects!.Value,
                "a TIMEOUT must state partial_effects: false — the cancelled item never executes, so the model was not modified");
            Assert.Contains(ProtocolConstants.TimeoutCancellationNote, result.Hints);
            Assert.Equal(0, handler.Calls);
        }
        finally
        {
            bridge.Stop();
        }
    }

    [Fact]
    public void TimeoutWhileHandlerRuns_ReportsUnknownOutcome()
    {
        var dispatcher = new FakeDispatcher();
        var handler = new StubHandler();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        handler.Override = _ =>
        {
            started.Set();
            release.Wait(5000);
            return AddInResponse.Success(CommandResult.Success(new JsonObject()));
        };
        var bridge = new EventBridge(dispatcher, handler, _log);
        var submit = Task.Run(() => bridge.Submit(
            JsonNode.Parse("""{"type":"extrude","params":{}}""")!, 0.3));
        var mainThread = new Thread(() =>
        {
            while (!started.IsSet)
            {
                dispatcher.Pump();
                Thread.Sleep(10);
            }
            dispatcher.Pump();
        }) { IsBackground = true };
        mainThread.Start();
        try
        {
            Assert.True(started.Wait(3000));
            Assert.True(submit.Wait(3000));
            var result = ProtocolCodec.ParseResponse(submit.Result.ToJson()).Result!;
            Assert.Equal(ErrorKind.Timeout, result.ErrorKind);
            Assert.True(result.Fields["outcome_unknown"]!.GetValue<bool>());
            Assert.False(result.Fields.ContainsKey("partial_effects"));
        }
        finally
        {
            release.Set();
            mainThread.Join(3000);
            bridge.Stop();
        }
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Stop_WakesQueuedSubmitters_WithAStructuredError()
    {
        var dispatcher = new FakeDispatcher();
        var handler = new StubHandler();
        var bridge = new EventBridge(dispatcher, handler, _log);

        // The item sits in the queue (the fake never drains) while the submitter waits.
        var task = Task.Run(() => bridge.Submit(JsonNode.Parse("""{"type":"extrude","params":{"body":"x"}}""")!, 30.0));
        Thread.Sleep(100);

        bridge.Stop();

        Assert.True(task.Wait(5000), "stop must wake queued submitters immediately");
        var frame = ProtocolCodec.ParseResponse(task.Result.ToJson());
        Assert.True(frame.IsError);
        Assert.Equal(ErrorKind.Unknown, frame.ErrorKind);
        Assert.Contains("Add-in stopped before the command executed", frame.Message!);
        Assert.Equal(0, handler.Calls);
    }
}

/// <summary>Generic dispatch (reference execute_command routing): ping + unknown command.</summary>
public sealed class CommandHandlerTests
{
    [Fact]
    public void Ping_IsRegisteredInTheDispatchTable()
    {
        var handler = new CommandHandler();
        var frame = ProtocolCodec.ParseResponse(handler.ExecuteCommand(JsonNode.Parse("""{"type":"ping","params":{}}""")!).ToJson());

        Assert.True(frame.IsSuccess);
        Assert.Equal("pong", frame.Result!.Field("status")!.GetValue<string>());
    }

    [Fact]
    public void UnknownCommand_ReturnsTheLegacyErrorEnvelope()
    {
        var handler = new CommandHandler();
        var frame = ProtocolCodec.ParseResponse(handler.ExecuteCommand(JsonNode.Parse("""{"type":"fly_to_moon","params":{}}""")!).ToJson());

        Assert.True(frame.IsError);
        Assert.Equal(ErrorKind.UnknownCommand, frame.ErrorKind);
        Assert.Equal("Unknown command: fly_to_moon", frame.Message);
    }

    [Fact]
    public void ExecuteCode_IsRejectedAsAnUnknownCommand()
    {
        var handler = new CommandHandler();
        var frame = ProtocolCodec.ParseResponse(handler.ExecuteCommand(
            JsonNode.Parse("""{"type":"execute_code","params":{"code":"throw new Exception()"}}""")!).ToJson());

        Assert.True(frame.IsError);
        Assert.Equal(ErrorKind.UnknownCommand, frame.ErrorKind);
        Assert.Equal("Unknown command: execute_code", frame.Message);
    }
}
