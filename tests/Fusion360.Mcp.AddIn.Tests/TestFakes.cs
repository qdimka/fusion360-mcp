using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// Fake main-thread dispatcher: <see cref="Fire"/> only records the fired
/// event id; tests drive the drain explicitly via <see cref="Pump"/>,
/// optionally on a dedicated thread — simulating the adsk CustomEvent
/// main thread without Fusion.
/// </summary>
public sealed class FakeDispatcher : IMainThreadDispatcher
{
    private readonly List<Action<object>> _handlers = new();
    private readonly object _gate = new();
    private int _fireCount;

    /// <summary>The last event id fired (null when the bridge never poked the main thread).</summary>
    public string? LastFired { get; private set; }
    public int FireCount => Volatile.Read(ref _fireCount);

    public void Attach(string customEventId, Action<object> notifyOnMainThread)
    {
        lock (_gate)
        {
            _handlers.Add(notifyOnMainThread);
        }
    }

    public void Fire(string customEventId)
    {
        LastFired = customEventId;
        Interlocked.Increment(ref _fireCount);
    }

    public void Unregister(string customEventId)
    {
        lock (_gate)
        {
            _handlers.Clear();
        }
    }

    /// <summary>Runs the registered notify handlers on the calling thread (the "main thread" simulation).</summary>
    public void Pump()
    {
        List<Action<object>> snapshot;
        lock (_gate)
        {
            snapshot = _handlers.ToList();
        }

        foreach (var handler in snapshot)
        {
            handler(null!);
        }
    }
}

/// <summary>
/// Fake main-thread dispatcher that drains inline on the calling
/// (socket/test) thread — for full-pipeline socket tests.
/// </summary>
public sealed class SyncDispatcher : IMainThreadDispatcher
{
    private readonly List<Action<object>> _handlers = new();
    private readonly object _gate = new();

    public void Attach(string customEventId, Action<object> notifyOnMainThread)
    {
        lock (_gate)
        {
            _handlers.Add(notifyOnMainThread);
        }
    }

    public void Fire(string customEventId)
    {
        List<Action<object>> snapshot;
        lock (_gate)
        {
            snapshot = _handlers.ToList();
        }

        foreach (var handler in snapshot)
        {
            handler(null!);
        }
    }

    public void Unregister(string customEventId)
    {
        lock (_gate)
        {
            _handlers.Clear();
        }
    }
}

/// <summary>Configurable fake command handler.</summary>
public sealed class StubHandler : IAddInCommandHandler
{
    /// <summary>How many times <see cref="ExecuteCommand"/> ran.</summary>
    public int Calls { get; private set; }

    /// <summary>The name of the thread that executed the handler (null when never run).</summary>
    public string? ExecutedOn { get; private set; }

    /// <summary>Throws from ExecuteCommand on the next call (one-shot).</summary>
    public Exception? ThrowOnce { get; set; }

    /// <summary>Optional gate the handler waits on before producing its result.</summary>
    public ManualResetEventSlim? Gate { get; set; }

    /// <summary>Optional per-call handler override.</summary>
    public Func<JsonNode, AddInResponse>? Override { get; set; }

    /// <summary>The default success result returned when no override/throw is set.</summary>
    public AddInResponse Response { get; set; } =
        AddInResponse.Success(CommandResult.Success(new JsonObject { ["status"] = "ok" }));

    public AddInResponse ExecuteCommand(JsonNode frame)
    {
        Calls++;
        ExecutedOn = Thread.CurrentThread.Name ?? "(unnamed)";

        if (ThrowOnce is { } ex)
        {
            ThrowOnce = null;
            throw ex;
        }

        Gate?.Wait(5000);
        return Override?.Invoke(frame) ?? Response;
    }
}
