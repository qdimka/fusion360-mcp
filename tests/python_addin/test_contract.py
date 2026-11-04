"""Headless protocol tests for the packaged Fusion Python add-in."""

import json
import os
import socket
from pathlib import Path
import sys
import tempfile
import threading
import time
import types
import unittest
from unittest.mock import patch

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
from server.socket_server import Fusion360MCPServer, validate_bind_host  # noqa: E402


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

    def test_failed_export_with_new_file_reports_partial_effects(self):
        handler = self.handler()
        def fail(file_path):
            Path(file_path).write_text("exported")
            raise RuntimeError("export failed")
        handler.export_f3d = fail
        with tempfile.TemporaryDirectory() as directory:
            result = handler.execute_command({"type": "export_f3d", "params": {
                "file_path": str(Path(directory) / "output.f3d"),
            }})["result"]
        self.assertFalse(result["ok"])
        self.assertIs(result["partial_effects"], True)

    def test_failed_export_over_existing_file_never_claims_no_effect(self):
        handler = self.handler()
        def fail(file_path):
            Path(file_path).write_text("changed export")
            raise RuntimeError("export failed")
        handler.export_f3d = fail
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "output.f3d"
            path.write_text("old")
            result = handler.execute_command({"type": "export_f3d", "params": {
                "file_path": str(path),
            }})["result"]
        self.assertIsNot(result.get("partial_effects"), False)
        self.assertTrue(result.get("partial_effects") or result.get("outcome_unknown"))

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

    def test_socket_rejects_malformed_requests_and_recovers(self):
        handler = self.handler()

        class Bridge:
            def submit(self, command):
                if command.get("type") == "ping":
                    return {"status": "success", "result": {"status": "pong"}}
                return handler.execute_command(command)

        server = Fusion360MCPServer(Bridge())
        server._running = True
        client, peer = socket.socketpair()
        peer.settimeout(1)
        thread = threading.Thread(target=server._handle_client, args=(client,))
        thread.start()
        try:
            with peer.makefile("rb") as responses:
                for request in (b"{invalid}\n", b"[]\n"):
                    peer.sendall(request)
                    result = json.loads(responses.readline())
                    self.assertEqual("error", result["status"])
                    self.assertEqual("INVALID_INPUT", result["error_kind"])
                    self.assertTrue(result["message"])
                peer.sendall(b'{"type":"create_box","params":[]}\n')
                result = json.loads(responses.readline())
                self.assertEqual("success", result["status"])
                self.assertFalse(result["result"]["ok"])
                self.assertEqual("INVALID_INPUT", result["result"]["error_kind"])
                peer.sendall(b'{"type":"ping","params":{}}\n')
                result = json.loads(responses.readline())
                self.assertEqual("success", result["status"])
                self.assertEqual("pong", result["result"]["status"])
        finally:
            server._running = False
            peer.close()
            client.close()
            thread.join(2)
            self.assertFalse(thread.is_alive())

    def test_orient_camera_resolves_design_when_cam_product_is_active(self):
        handler = self.handler()
        handler.app.activeProduct = types.SimpleNamespace()  # CAM product
        point = lambda x, y, z: types.SimpleNamespace(x=x, y=y, z=z)
        viewport = types.SimpleNamespace(camera=types.SimpleNamespace())
        with patch.object(adsk.core, "Point3D", types.SimpleNamespace(create=point), create=True), \
             patch.object(adsk.core, "Vector3D", types.SimpleNamespace(create=point), create=True):
            handler._orient_camera(viewport, ((1, 1, 1), (0, 0, 1)))
        self.assertEqual((0, 0, 0), (viewport.camera.target.x, viewport.camera.target.y, viewport.camera.target.z))
        self.assertEqual((20, 20, 20), (viewport.camera.eye.x, viewport.camera.eye.y, viewport.camera.eye.z))

    def test_flat_pattern_uses_largest_planar_sheet_face(self):
        handler = self.handler()
        face_small = types.SimpleNamespace(area=1, geometry=types.SimpleNamespace(surfaceType=1))
        face_large = types.SimpleNamespace(area=8, geometry=types.SimpleNamespace(surfaceType=1))
        curved = types.SimpleNamespace(area=20, geometry=types.SimpleNamespace(surfaceType=2))
        body = types.SimpleNamespace(isSheetMetal=True, faces=[face_small, curved, face_large])
        calls = []
        component = types.SimpleNamespace(flatPattern=None, createFlatPattern=lambda face:
                                          calls.append(face) or types.SimpleNamespace(name="Flat1"))
        handler._root = lambda: component
        handler._body_by_name = lambda name: body
        with patch.object(adsk.core, "SurfaceTypes", types.SimpleNamespace(PlaneSurfaceType=1), create=True):
            result = handler.flat_pattern("Sheet")
        self.assertEqual([face_large], calls)
        self.assertEqual("Flat1", result["feature_name"])

    def test_flat_pattern_rejects_non_sheet_body(self):
        handler = self.handler()
        handler._body_by_name = lambda name: types.SimpleNamespace(isSheetMetal=False)
        with self.assertRaisesRegex(RuntimeError, "sheet metal"):
            handler.flat_pattern("Solid")

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
