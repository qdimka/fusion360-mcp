"""
EventBridge — dispatches work from socket threads to Fusion's main thread.

Uses adsk.core.CustomEvent + a thread-safe queue so all Fusion API calls
happen on the main thread.  A 200 ms backup timer fires the custom event
periodically in case fireCustomEvent from a daemon thread is unreliable.
"""

import queue
import threading
import time
import traceback

import adsk.core

from . import get_logger
from .hints import classify

log = get_logger("bridge")

CUSTOM_EVENT_ID = "Fusion360MCP_BridgeEvent"
TIMER_INTERVAL_MS = 200  # backup polling interval


class WorkItem:
    """One unit of work submitted from a socket thread."""

    __slots__ = ("command", "result", "error", "done", "_state", "_lock")

    def __init__(self, command: dict):
        self.command = command
        self.result = None
        self.error = None
        self.done = threading.Event()
        self._state = "queued"
        self._lock = threading.Lock()

    def try_start(self) -> bool:
        with self._lock:
            if self._state != "queued":
                return False
            self._state = "executing"
            return True

    def try_cancel_before_start(self, error=None) -> bool:
        with self._lock:
            if self._state != "queued":
                return False
            self.error = error
            self._state = "cancelled"
            self.done.set()
            return True

    def complete(self) -> None:
        with self._lock:
            self._state = "completed"
            self.done.set()

    @property
    def state(self) -> str:
        with self._lock:
            return self._state


class _MainThreadHandler(adsk.core.CustomEventHandler):
    """Attached to the CustomEvent; runs on Fusion's main thread."""

    def __init__(self, bridge: "EventBridge"):
        super().__init__()
        self._bridge = bridge

    def notify(self, args):  # called on main thread
        self._bridge.drain_queue()


class EventBridge:
    """
    Bridge between daemon socket threads and Fusion's main thread.

    Socket thread calls ``submit(command)`` which blocks (up to *timeout*
    seconds) until the main thread has executed the command and stored the
    result.
    """

    def __init__(self, app: adsk.core.Application, command_handler):
        self._app = app
        self._handler = command_handler
        self._queue: queue.Queue = queue.Queue()
        self._lifecycle_lock = threading.Lock()
        self._stopped = False

        # Register a custom event on the main thread
        self._event = app.registerCustomEvent(CUSTOM_EVENT_ID)
        self._event_handler = _MainThreadHandler(self)
        self._event.add(self._event_handler)

        # Backup timer thread — fires the custom event every 200 ms
        self._timer_running = True
        self._timer_thread = threading.Thread(target=self._timer_loop, daemon=True)
        self._timer_thread.start()

        log.info("EventBridge initialised (custom event: %s)", CUSTOM_EVENT_ID)

    # ------------------------------------------------------------------
    # Called from socket (daemon) threads
    # ------------------------------------------------------------------

    def submit(self, command: dict, timeout: float = 300.0) -> dict:
        """Queue *command* for main-thread execution; block until done."""
        cmd_type = command.get("type", "?")
        with self._lifecycle_lock:
            if self._stopped:
                return self._stopped_result()
            # Ping never touches the Fusion API.
            if cmd_type == "ping":
                return {"status": "success", "result": {"ok": True, "status": "pong"}}
            log.debug("submit cmd=%s", cmd_type)
            item = WorkItem(command)
            self._queue.put(item)

        # Poke the main thread
        try:
            self._app.fireCustomEvent(CUSTOM_EVENT_ID)
        except Exception:
            pass  # timer will pick it up

        if not item.done.wait(timeout=timeout):
            if item.try_cancel_before_start():
                log.warning("Command %s timed out before execution", cmd_type)
                return self._timeout_result(timeout, partial_effects=False)
            if item.done.is_set() and item.state == "completed":
                return self._response_for(item, cmd_type)
            log.warning("Command %s timed out during execution", cmd_type)
            return self._timeout_result(timeout, outcome_unknown=True)

        return self._response_for(item, cmd_type)

    @staticmethod
    def _timeout_result(timeout, **fields):
        return {
            "status": "success",
            "result": {
                "ok": False,
                "error_kind": "TIMEOUT",
                "error_message": f"Command timed out after {timeout:g}s",
                **fields,
            },
        }

    @staticmethod
    def _stopped_result():
        return {
            "status": "success",
            "result": {
                "ok": False,
                "error_kind": "ADDIN_STOPPED",
                "error_message": "Add-in stopped before the command executed",
                "partial_effects": False,
            },
        }

    def _response_for(self, item, cmd_type):
        if item.error is not None:
            log.error("Command %s failed: %s", cmd_type, item.error)
            kind, hints = classify(item.error)
            return {
                "status": "error",
                "error_kind": kind,
                "hints": hints,
                "message": item.error,
            }

        log.debug("Command %s completed", cmd_type)
        return item.result

    # ------------------------------------------------------------------
    # Called on Fusion's main thread (from CustomEventHandler.notify)
    # ------------------------------------------------------------------

    def drain_queue(self):
        """Execute every queued work item (main thread only)."""
        while True:
            try:
                item: WorkItem = self._queue.get_nowait()
            except queue.Empty:
                break

            cmd_type = item.command.get("type", "?")
            with self._lifecycle_lock:
                if self._stopped:
                    item.try_cancel_before_start("Add-in stopped before the command executed")
                started = item.try_start()
            if not started:
                log.info("Skipping cancelled command %s", cmd_type)
                continue
            try:
                item.result = self._handler.execute_command(item.command)
            except Exception as exc:
                item.error = f"{exc}\n{traceback.format_exc()}"
                log.error("Main-thread exec of %s raised: %s", cmd_type, exc)
            finally:
                item.complete()

    # ------------------------------------------------------------------
    # Backup timer
    # ------------------------------------------------------------------

    def _timer_loop(self):
        interval = TIMER_INTERVAL_MS / 1000.0
        while self._timer_running:
            time.sleep(interval)
            if not self._queue.empty():
                try:
                    self._app.fireCustomEvent(CUSTOM_EVENT_ID)
                except Exception:
                    pass

    def stop(self):
        with self._lifecycle_lock:
            self._stopped = True
            self._timer_running = False
            # Release queued callers without allowing new work to enter.
            while True:
                try:
                    item = self._queue.get_nowait()
                except queue.Empty:
                    break
                item.try_cancel_before_start("Add-in stopped before the command executed")
        try:
            self._event.remove(self._event_handler)
        except Exception:
            pass
        try:
            self._app.unregisterCustomEvent(CUSTOM_EVENT_ID)
        except Exception:
            pass
        log.info("EventBridge stopped")
