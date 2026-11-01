using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// One unit of work submitted from a socket thread (reference
/// event_bridge.py WorkItem). Carries the response frame produced on
/// the main thread, or the classified error that stopped execution.
/// </summary>
public sealed class WorkItem
{
    private const int Queued = 0;
    private const int Executing = 1;
    private const int Completed = 2;
    private const int Cancelled = 3;

    private readonly ManualResetEventSlim _done = new(false);
    private int _state = Queued;

    public WorkItem(JsonNode command) => Command = command;

    /// <summary>The raw command frame ({"type": ..., "params": ...}).</summary>
    public JsonNode Command { get; }

    /// <summary>The response frame produced on the main thread (null when skipped/failed).</summary>
    public AddInResponse? Result { get; internal set; }

    /// <summary>Exception text that stopped execution on the main thread, if any.</summary>
    public string? Error { get; internal set; }

    /// <summary>
    /// True when the waiting socket thread cancelled this item before start.
    /// An executing item cannot be interrupted by the bridge.
    /// INVARIANT: only queued items can transition to Cancelled.
    /// History: docs/decisions/protocol.md#timeout-cancel
    /// </summary>
    public bool IsCancelled => Volatile.Read(ref _state) == Cancelled;

    /// <summary>True once the main-thread handler has started.</summary>
    public bool IsExecuting => Volatile.Read(ref _state) == Executing;

    /// <summary>True when the handler's result has been recorded.</summary>
    public bool IsCompleted => Volatile.Read(ref _state) == Completed;

    internal bool TryStart() => Interlocked.CompareExchange(ref _state, Executing, Queued) == Queued;

    /// <summary>Cancel only work that has not started on Fusion's main thread.</summary>
    internal bool TryCancelBeforeStart() => Interlocked.CompareExchange(ref _state, Cancelled, Queued) == Queued;

    internal void Complete()
    {
        Interlocked.CompareExchange(ref _state, Completed, Executing);
        _done.Set();
    }

    internal void CompleteCancelled() => _done.Set();

    internal bool Wait(TimeSpan timeout) => _done.Wait(timeout);
}

/// <summary>
/// Marshals the custom event to Fusion's main thread. The production
/// implementation wraps adsk.core CustomEvent (<see cref="AdskMainThreadDispatcher"/>);
/// unit tests use a fake that invokes the notify handler on a chosen
/// thread, so the bridge's main-thread dispatch is testable without Fusion.
/// </summary>
public interface IMainThreadDispatcher
{
    /// <summary>Registers a handler that must run on the main thread when the event fires.</summary>
    void Attach(string customEventId, Action<object> notifyOnMainThread);

    /// <summary>Fires the custom event (Fusion marshals it to the main thread).</summary>
    void Fire(string customEventId);

    /// <summary>Removes the handler and unregisters the custom event (teardown).</summary>
    void Unregister(string customEventId);
}

/// <summary>
/// adsk implementation of <see cref="IMainThreadDispatcher"/>: all adsk
/// calls happen here; the bridge itself never touches the adsk API, so
/// it stays unit-testable.
/// </summary>
public sealed class AdskMainThreadDispatcher : IMainThreadDispatcher
{
    private sealed class Handler : adsk.core.CustomEventHandler
    {
        private readonly Action<object> _notify;

        public Handler(Action<object> notify) => _notify = notify;

        // Runs on Fusion's main thread when the custom event fires.
        public override void Notify(object args) => _notify(args);
    }

    private readonly adsk.core.Application _app;
    private readonly Dictionary<string, (adsk.core.CustomEvent Event, Handler Handler)> _events = new();
    private readonly object _gate = new();

    public AdskMainThreadDispatcher(adsk.core.Application app) => _app = app;

    public void Attach(string customEventId, Action<object> notifyOnMainThread)
    {
        lock (_gate)
        {
            var @event = _app.RegisterCustomEvent(customEventId);
            var handler = new Handler(notifyOnMainThread);
            @event.Add(handler);
            _events[customEventId] = (@event, handler);
        }
    }

    public void Fire(string customEventId) => _app.FireCustomEvent(customEventId);

    public void Unregister(string customEventId)
    {
        lock (_gate)
        {
            if (_events.TryGetValue(customEventId, out var entry))
            {
                _events.Remove(customEventId);

                try
                {
                    entry.Event.Remove(entry.Handler);
                }
                catch
                {
                    // Teardown is best-effort (reference stop() semantics).
                }

                try
                {
                    _app.UnregisterCustomEvent(customEventId);
                }
                catch
                {
                    // Teardown is best-effort.
                }
            }
        }
    }
}

/// <summary>
/// Bridges daemon socket threads to Fusion's main thread (C# port of
/// reference addon/server/event_bridge.py). Socket threads call
/// <see cref="Submit"/>, which queues a <see cref="WorkItem"/> and pokes
/// the main thread through the custom event (plus a 200 ms backup timer,
/// in case fireCustomEvent from a daemon thread is unreliable). All adsk
/// work executes in <see cref="DrainQueue"/>, on the main thread.
/// </summary>
public sealed class EventBridge
{
    /// <summary>The adsk custom event id (reference CUSTOM_EVENT_ID).</summary>
    public const string CustomEventId = "Fusion360MCP_BridgeEvent";

    /// <summary>Backup polling interval (reference TIMER_INTERVAL_MS).</summary>
    public const int TimerIntervalMs = 200;

    private const double DefaultTimeoutSeconds = 30.0;

    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _timerThread;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly IAddInCommandHandler _handler;
    private readonly McpLogger _log;
    private int _draining;
    private volatile bool _timerRunning = true;

    public EventBridge(IMainThreadDispatcher dispatcher, IAddInCommandHandler handler, McpLogger log)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _dispatcher.Attach(CustomEventId, _ => DrainQueue());
        _timerThread = new Thread(TimerLoop)
        {
            IsBackground = true,
            Name = "mcp-bridge-timer",
        };
        _timerThread.Start();

        _log.Info("EventBridge initialised (custom event: {0})", CustomEventId);
    }

    // ------------------------------------------------------------------
    // Called from socket (daemon) threads
    // ------------------------------------------------------------------

    /// <summary>
    /// Queues <paramref name="command"/> for main-thread execution and
    /// blocks up to <paramref name="timeout"/> (default 30 s, reference)
    /// until the result is ready. Fast path: "ping" never touches the
    /// adsk API and is answered immediately, without queueing.
    /// </summary>
    public AddInResponse Submit(JsonNode command, double timeoutSeconds = DefaultTimeoutSeconds)
    {
        string cmdType = command["type"]?.GetValue<string>() ?? "?";

        if (cmdType == "ping")
        {
            _log.Debug("ping (fast path)");
            return AddInResponse.Success(CommandResult.Success(new JsonObject { ["status"] = "pong" }));
        }

        _log.Debug("submit cmd={0}", cmdType);
        var item = new WorkItem(command);
        _queue.Add(item);

        // Poke the main thread; the backup timer picks the item up if
        // this fire never lands.
        try
        {
            _dispatcher.Fire(CustomEventId);
        }
        catch
        {
            // The timer will pick it up.
        }

        if (!item.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            if (item.TryCancelBeforeStart())
            {
                _log.Warning("Command {0} timed out before execution (cancelled)", cmdType);
                return TimeoutResponse(cmdType, timeoutSeconds, outcomeUnknown: false);
            }

            // The handler may already be inside a non-interruptible Fusion API call.
            // A racing completion is safe to return; otherwise its model outcome is unknown.
            if (item.Wait(TimeSpan.Zero))
            {
                return ResponseFor(item, cmdType);
            }

            _log.Warning("Command {0} timed out while executing; outcome is unknown", cmdType);
            return TimeoutResponse(cmdType, timeoutSeconds, outcomeUnknown: true);
        }

        return ResponseFor(item, cmdType);
    }

    private AddInResponse TimeoutResponse(string cmdType, double timeoutSeconds, bool outcomeUnknown)
    {
        var failure = CommandResult.Failure(
                ErrorKind.Timeout,
                $"Command timed out after {timeoutSeconds:0.##}s",
                new[]
                {
                    outcomeUnknown
                        ? "Execution had started and cannot be interrupted; inspect the scene " +
                          "before any retry."
                        : ProtocolConstants.TimeoutCancellationNote,
                    "Verify the scene with get_scene_info before re-issuing the operation.",
                    "Long operations (CAM toolpaths, complex fillets) exceed the add-in's 30 s " +
                    "bridge timeout — split them into smaller steps.",
                });
            if (outcomeUnknown)
            {
                failure.Fields["outcome_unknown"] = true;
            }
            else
            {
                failure.Fields["partial_effects"] = false;
            }

            return AddInResponse.Success(failure);
    }

    private AddInResponse ResponseFor(WorkItem item, string cmdType)
    {
        if (item.Error is not null)
        {
            _log.Error("Command {0} failed: {1}", cmdType, item.Error);
            var (kind, hints) = ErrorClassifier.Classify(item.Error);
            return AddInResponse.Error(item.Error, kind, hints);
        }

        _log.Debug("Command {0} completed", cmdType);
        return item.Result!;
    }

    // ------------------------------------------------------------------
    // Called on Fusion's main thread (from the custom-event handler)
    // ------------------------------------------------------------------

    /// <summary>
    /// Executes every queued work item (main thread only). Unlike the
    /// Python reference there is no "reload_handler" path: C# assemblies
    /// are not hot-reimportable; restart the add-in (Stop/Run) instead.
    /// </summary>
    public void DrainQueue()
    {
        if (Interlocked.Exchange(ref _draining, 1) != 0)
        {
            // DoEvents can dispatch the custom event recursively while a
            // handler is pumping Fusion. The outer drain owns the queue.
            return;
        }

        try
        {
            while (_queue.TryTake(out var item, 0))
            {
                string cmdType = item.Command["type"]?.GetValue<string>() ?? "?";

                if (!item.TryStart())
                {
                    _log.Info("Skipping non-queued command {0}", cmdType);
                    continue;
                }

                try
                {
                    item.Result = _handler.ExecuteCommand(item.Command);
                }
                catch (Exception ex)
                {
                    item.Error = ex.Message + "\n" + ex.ToString();
                    _log.Error("Main-thread exec of {0} raised: {1}", cmdType, ex.Message);
                }
                finally
                {
                    item.Complete();
                }
            }
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
        }
    }

    // ------------------------------------------------------------------
    // Backup timer
    // ------------------------------------------------------------------

    private void TimerLoop()
    {
        while (_timerRunning)
        {
            Thread.Sleep(TimerIntervalMs);
            if (_queue.Count > 0)
            {
                try
                {
                    _dispatcher.Fire(CustomEventId);
                }
                catch
                {
                    // A lost fire is retried on the next tick.
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Teardown
    // ------------------------------------------------------------------

    /// <summary>Stops the bridge: wakes blocked submitters, detaches the event.</summary>
    public void Stop()
    {
        _timerRunning = false;

        // Release any queued items so blocked socket threads wake up
        // immediately instead of waiting out their full timeout against
        // a dead bridge (reference stop() semantics).
        while (_queue.TryTake(out var item, 0))
        {
            if (item.TryCancelBeforeStart())
            {
                item.Error = "Add-in stopped before the command executed";
                item.CompleteCancelled();
            }
        }

        _dispatcher.Unregister(CustomEventId);

        if (_timerThread.IsAlive)
        {
            _timerThread.Join(TimeSpan.FromSeconds(1));
        }

        _log.Info("EventBridge stopped");
    }
}
