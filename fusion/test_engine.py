import unittest
import sys
import os
from unittest.mock import patch
from types import SimpleNamespace
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from client import AgentMouseClient
from engine import FusionSession
from policy import require_allowed_process, require_allowed_window


class FakeClient:
    def __init__(self):
        self.calls = []
        self.allowed = True

    async def call(self, method, **args):
        self.calls.append((method, args))
        if method == "observe":
            return {"observationId": "fixture-token", "window": {"pid": 5, "bounds": {"x": 10, "y": 20, "width": 100, "height": 80}}, "inputAllowed": self.allowed}
        return {"inputDelivered": True, "buttonReleased": True}


class FusionTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.client = FakeClient()
        self.clock = 1

        async def controls(window):
            return {"elements": [{"label": 0, "center": {"x": 40, "y": 50}}], "pid": 5, "bounds": [10, 20, 110, 100]}

        self.session = FusionSession(self.client, controls, now=lambda: self.clock)

    async def test_target_label_and_single_use(self):
        await self.session.snapshot("123", True)
        with self.assertRaisesRegex(ValueError, "wrong_target"):
            await self.session.action("click", "456", "fixture-token", label=0)
        self.assertEqual(len(self.client.calls), 1)
        await self.session.action("click", "123", "fixture-token", label=0)
        self.assertEqual(self.client.calls[-1], ("click", {"windowId": "123", "observationId": "fixture-token", "x": 40, "y": 50}))
        with self.assertRaisesRegex(ValueError, "stale_observation"):
            await self.session.action("click", "123", "fixture-token", label=0)

    async def test_old_frame_and_expiry(self):
        self.client.allowed = False
        await self.session.snapshot("123")
        with self.assertRaisesRegex(ValueError, "unverified_frame"):
            await self.session.action("click", "123", "fixture-token", loc=[30, 40])
        self.client.allowed = True
        await self.session.snapshot("123")
        self.clock += 16
        with self.assertRaisesRegex(ValueError, "stale_observation"):
            await self.session.action("click", "123", "fixture-token", loc=[30, 40])
        self.assertEqual(len(self.client.calls), 2)

    async def test_geometry_drift_discards_labels(self):
        async def changed(window):
            return {"elements": [{"label": 0, "center": {"x": 1, "y": 1}}], "pid": 99, "bounds": [10, 20, 110, 100]}
        self.session.controls = changed
        result = await self.session.snapshot("123", True)
        self.assertEqual(result["controls"]["elements"], [])
        with self.assertRaisesRegex(ValueError, "Unknown label"):
            await self.session.action("click", "123", "fixture-token", label=0)

    async def test_failed_provider_discards_partial_labels(self):
        async def failed(window):
            return {"elements":[{"label":0,"center":{"x":1,"y":1}}],"providerSucceeded":False,"error":"Provider failed"}
        self.session.controls=failed
        result=await self.session.snapshot("123",True)
        self.assertEqual(result["controls"]["elements"],[])
        with self.assertRaisesRegex(ValueError,"Unknown label"):
            self.session.resolve("123","fixture-token",label=0)

    async def test_broken_worker_write_releases_pending_request(self):
        class Broken:
            def write(self,data):
                raise BrokenPipeError("fixture")
        transport=AgentMouseClient("node",Path("."))
        transport.process=SimpleNamespace(returncode=None,stdin=Broken())
        with self.assertRaisesRegex(ValueError,"worker_disconnected"):
            await transport.call("status")
        self.assertEqual(transport.pending,{})
        transport.process.returncode=1
        with self.assertRaisesRegex(ValueError,"worker_disconnected"):
            await transport.call("status")

    async def test_desktop_overview_cannot_target_foreground(self):
        await self.session.snapshot(None)
        with self.assertRaisesRegex(ValueError, "wrong_target"):
            await self.session.action("click", "zcode", "fixture-token", loc=[30, 40])
        await self.session.pause()
        self.assertIsNone(self.session.observation)

    async def test_snapshot_defaults_to_no_provider_calls(self):
        async def forbidden(window):
            raise AssertionError("UIAutomation must be explicit")
        self.session.controls=forbidden
        result=await self.session.snapshot("123")
        self.assertEqual(result["controls"]["elements"], [])

    async def test_quarantine_is_pure_and_covers_family(self):
        for name in ["Weixin", "WECHAT.exe", "WeChatAppEx.exe"]:
            with self.assertRaisesRegex(ValueError,"target_quarantined"):
                require_allowed_process(name)
        require_allowed_process("AgentMouse.Cli.exe")
        with self.assertRaisesRegex(ValueError,"target_quarantined"):
            require_allowed_window({"process":"Weixin"})

    async def test_native_pilot_never_allows_uia_or_another_process(self):
        window={"process":"Weixin","pid":42,"nativePilot":"42:123"}
        with patch.dict(os.environ,{"AGENTMOUSE_NATIVE_PILOT":"42:123"}):
            require_allowed_window(window)
            for other in [{**window,"pid":43},{**window,"nativePilot":"42:122"},{**window,"automationQuarantined":True}]:
                with self.assertRaisesRegex(ValueError,"target_quarantined"):
                    require_allowed_window(other)
            with self.assertRaisesRegex(ValueError,"target_quarantined"):
                require_allowed_window(window,accessibility=True)
            with self.assertRaisesRegex(ValueError,"target_quarantined"):
                require_allowed_process("Weixin")
        with patch.dict(os.environ,{"AGENTMOUSE_NATIVE_PILOT":""}):
            with self.assertRaisesRegex(ValueError,"target_quarantined"):
                require_allowed_window(window)


if __name__ == "__main__":
    unittest.main()
