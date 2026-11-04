"""Headless protocol tests for the packaged Fusion Python add-in."""

import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import types
import unittest

ROOT = Path(__file__).resolve().parents[2]
os.environ["FUSION_MCP_LOG_DIR"] = tempfile.gettempdir()
sys.path.insert(0, str(ROOT / "addon/Fusion360MCP"))
sys.path.insert(0, str(ROOT / "scripts"))

adsk = types.ModuleType("adsk")
for part in ("core", "fusion", "cam"):
    module = types.ModuleType(f"adsk.{part}")
    setattr(adsk, part, module)
    sys.modules[f"adsk.{part}"] = module
sys.modules["adsk"] = adsk
adsk.core.CustomEventHandler = type("CustomEventHandler", (), {})
adsk.core.Application = type("Application", (), {})

from generate_addin_effects import generate  # noqa: E402
from server.command_handler import CommandHandler  # noqa: E402
from server.event_bridge import EventBridge  # noqa: E402
from server.revision import state as revision_state  # noqa: E402
from server.socket_server import validate_bind_host  # noqa: E402


class FakeEvent:
    def add(self, handler):
        self.handler = handler

    def remove(self, handler):
        pass


class FakeApp:
    def __init__(self):
        self.event = FakeEvent()

    def registerCustomEvent(self, name):
        return self.event

    def fireCustomEvent(self, name):
        pass

    def unregisterCustomEvent(self, name):
        pass


class ContractTests(unittest.TestCase):
    def setUp(self):
        revision_state._revisions.clear()
        revision_state._reported.clear()

    def handler(self):
        handler = CommandHandler.__new__(CommandHandler)
        document = types.SimpleNamespace(creationId="doc-1")
        root = types.SimpleNamespace(
            bRepBodies=types.SimpleNamespace(count=0),
            sketches=types.SimpleNamespace(count=0),
            allOccurrences=[],
        )
        design = types.SimpleNamespace(parentDocument=document, rootComponent=root)
        document.products = types.SimpleNamespace(itemByProductType=lambda _: design)
        handler.app = types.SimpleNamespace(activeDocument=document, activeProduct=design)
        handler._snapshot = lambda: None
        return handler

    def test_catalog_dispatch_and_effect_sets(self):
        catalog = json.loads((ROOT / "src/Fusion360.Mcp.Core/Catalog/tools_catalog.json").read_text())
        handler = self.handler()
        handler.execute_command({"type": "missing"})
        self.assertEqual({tool["name"] for tool in catalog}, set(handler._commands))
        self.assertEqual("UNKNOWN_COMMAND", handler.execute_command({
            "type": "execute_code", "params": {"code": "print('unsafe')"},
        })["error_kind"])
        self.assertEqual("UNKNOWN_COMMAND", handler.execute_command({
            "type": "reload_handler",
        })["error_kind"])
        effects = json.loads((ROOT / "addon/Fusion360MCP/server/effects.json").read_text())
        self.assertEqual(generate(), effects)

    def test_new_handler_instance_rebinds_dispatch(self):
        first, second = self.handler(), self.handler()
        first.get_design_type = lambda: {"design_type": "first"}
        second.get_design_type = lambda: {"design_type": "second"}
        self.assertEqual("first", first.execute_command({"type": "get_design_type"})["result"]["design_type"])
        self.assertEqual("second", second.execute_command({"type": "get_design_type"})["result"]["design_type"])

    def test_revision_and_server_only_arguments(self):
        handler = self.handler()
        received = []
        handler.create_box = lambda **params: received.append(params) or {"body_name": "Box1"}
        first = handler.execute_command({"type": "create_box", "params": {
            "length": 1, "operation_id": "server-id", "expected_revision": 0,
        }})["result"]
        self.assertTrue(first["ok"])
        self.assertEqual(1, first["revision_after"])
        self.assertEqual([{"length": 1}], received)
        stale = handler.execute_command({"type": "create_box", "params": {
            "length": 1, "expected_revision": 0,
        }})["result"]
        self.assertEqual("REVISION_CONFLICT", stale["error_kind"])
        self.assertIs(stale["partial_effects"], False)
        self.assertEqual(1, stale["revision_after"])
        self.assertEqual(1, len(received))

    def test_scene_revision_and_stale_name(self):
        handler = self.handler()
        handler.get_scene_info = lambda: {
            "bodies": [{"name": "OldBody"}], "sketches": [],
        }
        handler.create_box = lambda **_: handler._body_by_name("OldBody")
        scene = handler.execute_command({"type": "get_scene_info"})["result"]
        self.assertEqual(0, scene["model_revision"])
        failure = handler.execute_command({"type": "create_box"})["result"]
        self.assertEqual("STALE_REFERENCE", failure["error_kind"])
        self.assertTrue(failure["outcome_unknown"])

    def test_mutation_verification_reads_back_named_body(self):
        handler = self.handler()
        handler.create_box = lambda **_: {"body_name": "Box1"}
        handler._body_by_name = lambda name: object() if name == "Box1" else None

        result = handler.execute_command({"type": "create_box"})["result"]

        self.assertEqual("existence_checked", result["verification"]["level"])
        self.assertTrue(result["verification"]["checks"][0]["passed"])

    def test_failure_with_observed_change_reports_partial_effects(self):
        handler = self.handler()
        snapshots = iter([{"body_count": 0, "mass_g": 0, "bbox": None},
                          {"body_count": 1, "mass_g": 10, "bbox": None}])
        handler._snapshot = lambda: next(snapshots)

        def fail(**params):
            raise RuntimeError("feature failed after body creation")

        handler.create_box = fail
        result = handler.execute_command({"type": "create_box"})["result"]
        self.assertIs(result["partial_effects"], True)
        self.assertEqual(1, result["deltas"]["body_count_delta"])
        self.assertEqual(1, result["revision_after"])

    def test_invalid_expected_revision_does_not_reach_handler(self):
        handler = self.handler()
        called = []
        handler.create_box = lambda **_: called.append(True)
        result = handler.execute_command({"type": "create_box", "params": {
            "expected_revision": True,
        }})["result"]
        self.assertEqual("INVALID_INPUT", result["error_kind"])
        self.assertIs(result["partial_effects"], False)
        self.assertEqual([], called)

    def test_revisions_are_separate_per_document_and_survive_handler_recreation(self):
        first = self.handler()
        first.create_box = lambda **_: {"body_name": "Box1"}
        self.assertEqual(1, first.execute_command({"type": "create_box"})["result"]["revision_after"])

        second = self.handler()
        second.app.activeDocument.creationId = "doc-2"
        second.get_scene_info = lambda: {"bodies": [], "sketches": []}
        second.create_box = lambda **_: {"body_name": "Box2"}
        self.assertEqual(0, second.execute_command({"type": "get_scene_info"})["result"]["model_revision"])
        self.assertEqual(1, second.execute_command({"type": "create_box"})["result"]["revision_after"])

        restarted = self.handler()
        restarted.get_scene_info = lambda: {"bodies": [], "sketches": []}
        self.assertEqual(1, restarted.execute_command({"type": "get_scene_info"})["result"]["model_revision"])

    def test_bind_guard(self):
        validate_bind_host("localhost", None)
        validate_bind_host("127.0.0.1", None)
        with self.assertRaises(ValueError):
            validate_bind_host("0.0.0.0", None)
        validate_bind_host("0.0.0.0", "1")

    def test_bridge_cancels_queued_work(self):
        class Handler:
            called = False

            def execute_command(self, command):
                self.called = True
                return {"status": "success", "result": {"ok": True}}

        app, handler = FakeApp(), Handler()
        bridge = EventBridge(app, handler)
        try:
            result = bridge.submit({"type": "create_box"}, timeout=0.01)
            self.assertEqual("TIMEOUT", result["result"]["error_kind"])
            self.assertIs(result["result"]["partial_effects"], False)
            bridge.drain_queue()
            self.assertFalse(handler.called)
        finally:
            bridge.stop()

    def test_bridge_running_timeout_is_unknown(self):
        entered, release = threading.Event(), threading.Event()

        class Handler:
            def execute_command(self, command):
                entered.set()
                release.wait(1)
                return {"status": "success", "result": {"ok": True}}

        bridge = EventBridge(FakeApp(), Handler())
        result = []
        caller = threading.Thread(target=lambda: result.append(
            bridge.submit({"type": "create_box"}, timeout=0.05)))
        caller.start()
        deadline = time.monotonic() + 1
        while bridge._queue.empty() and time.monotonic() < deadline:
            time.sleep(0.001)
        runner = threading.Thread(target=bridge.drain_queue)
        runner.start()
        self.assertTrue(entered.wait(1))
        caller.join(1)
        self.assertTrue(result[0]["result"]["outcome_unknown"])
        release.set()
        runner.join(1)
        bridge.stop()

    def test_bridge_stop_rejects_queued_and_new_work(self):
        class Handler:
            called = False

            def execute_command(self, command):
                self.called = True
                return {"status": "success", "result": {"ok": True}}

        handler = Handler()
        bridge = EventBridge(FakeApp(), handler)
        results = []
        caller = threading.Thread(target=lambda: results.append(bridge.submit({"type": "create_box"})))
        caller.start()
        deadline = time.monotonic() + 1
        while bridge._queue.empty() and time.monotonic() < deadline:
            time.sleep(0.001)
        self.assertFalse(bridge._queue.empty())
        bridge.stop()
        caller.join(1)
        self.assertFalse(caller.is_alive())
        bridge.drain_queue()
        self.assertFalse(handler.called)
        self.assertEqual("ADDIN_STOPPED", bridge.submit({"type": "ping"})["result"]["error_kind"])


if __name__ == "__main__":
    unittest.main()
