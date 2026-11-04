"""Windows-MCP system/semantic tools, with AgentMouse as the only mouse backend."""
import argparse
import asyncio
import json
import os
import subprocess
import sys
import functools
import re
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal

# The bundled interpreter isolates sys.path from user Python installations.
sys.path.insert(0, str(Path(__file__).resolve().parent))

from fastmcp import FastMCP
from fastmcp.tools import ToolResult
from mcp.types import ImageContent, TextContent
from client import AgentMouseClient
from engine import FusionSession


def json_errors(function):
    @functools.wraps(function)
    async def wrapped(*args, **kwargs):
        try:
            return await function(*args, **kwargs)
        except (ValueError, TimeoutError) as error:
            parts = str(error).split(":", 1)
            code = parts[0] if len(parts) == 2 and re.fullmatch(r"[a-z_]+", parts[0]) else "invalid_argument"
            return ToolResult(content=[TextContent(type="text", text=json.dumps({"code": code, "message": str(error)}))], is_error=True)
    return wrapped


def create_server(runtime, node, version):
    client = AgentMouseClient(node, runtime)

    async def controls(window_id):
        process = await asyncio.create_subprocess_exec(
            sys.executable, "-B", "-X", "utf8", str(runtime / "fusion/controls.py"), window_id,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL,
            limit=4 * 1024 * 1024, creationflags=subprocess.CREATE_NO_WINDOW,
        )
        try:
            stdout, _ = await asyncio.wait_for(process.communicate(), 5)
            result = json.loads(stdout)
            if process.returncode:
                return {"elements": [], "error": result.get("error", "Control inspection failed")}
            return result
        except (TimeoutError, asyncio.CancelledError):
            process.kill()
            await process.wait()
            if asyncio.current_task().cancelling():
                raise
            return {"elements": [], "error": "Windows-MCP control provider timed out; use the real image or stop"}

    session = FusionSession(client, controls)

    @asynccontextmanager
    async def lifespan(app):
        try:
            yield
        finally:
            await client.close()

    mcp = FastMCP("agentmouse", version=version, lifespan=lifespan, instructions=(
        "Windows-MCP + AgentMouse fusion. Mouse input always uses the independent background engine. "
        "Use App/list or pointer_list_apps to discover running apps before launching. "
        "Snapshot(window_id) returns a real target image and target-scoped Windows-MCP control labels. "
        "Each mouse action requires that exact window_id and single-use observation_id; reobserve and verify outcomes. "
        "Global snapshots are orientation only. Never silently use the foreground app. "
        "Type/Shortcut/batch Ctrl-selection and App activation are unavailable in background mode. "
        "System commands and clipboard remain real shared Windows operations. Pause after input/capture work."
    ))

    # Keep upstream system tools; do not start its physical-input hooks, overlay or ownership controller.
    from windows_mcp.tools import clipboard, display, filesystem, notification, process, registry, scrape, shell

    class ReadOnlyDesktop:
        def __init__(self):
            self.desktop = None

        def __getattr__(self, name):
            if name not in {"get_displays", "_display_to_view", "scrape", "clipboard"}:
                raise ValueError("This operation is not available through the background adapter")
            if self.desktop is None:
                from windows_mcp.desktop.service import Desktop
                self.desktop = Desktop()
            return getattr(self.desktop, name)

    desktop = ReadOnlyDesktop()
    for module in [clipboard, display, filesystem, notification, process, registry, scrape, shell]:
        module.register(mcp, get_desktop=lambda: desktop, get_analytics=lambda: None)

    def image_result(result):
        image = dict(result["screenshot"])
        data = image.pop("data")
        return [TextContent(type="text", text=json.dumps({**result, "screenshot": image}, ensure_ascii=True)), ImageContent(type="image", mimeType=image["mimeType"], data=data)]

    @mcp.tool(name="Snapshot", description="Real background HWND/browser image plus target-scoped Windows-MCP UIAutomation control labels. With no window_id, desktop overview only. Native locs are physical desktop pixels; browser locs are viewport CSS pixels. Use the returned observationId as observation_id; labels belong only to that observation. No activation/restore. Reobserve after every gesture.")
    @json_errors
    async def snapshot(window_id: str | None = None, use_ui_tree: bool = True):
        return image_result(await session.snapshot(window_id, use_ui_tree))

    @mcp.tool(name="Screenshot", description="Real background image for window_id; without it, desktop overview only. Returns observationId and coordinateSpace. No foreground activation; requires a fresh explicit target for input.")
    @json_errors
    async def screenshot(window_id: str | None = None):
        return image_result(await session.snapshot(window_id, False))

    @mcp.tool(name="Click", description="Background click/double-click/long-press at loc or target-scoped Snapshot label. Requires exact window_id and observation_id. clicks=0 is browser hover only. No physical mouse fallback. inputDelivered is not business success.")
    @json_errors
    async def click(window_id: str, observation_id: str, loc: list[int] | None = None, label: int | None = None, button: Literal["left", "right"] = "left", clicks: Literal[0, 1, 2] = 1, hold_ms: int = 0):
        if not 0 <= hold_ms <= 2000 or (clicks != 1 and hold_ms):
            raise ValueError("Invalid click count/hold duration")
        return await session.action("move" if clicks == 0 else "click", window_id, observation_id, loc, label, **({} if clicks == 0 else {"button": button, "clickCount": clicks, "holdMs": hold_ms}))

    @mcp.tool(name="Move", description="Browser hover or background intra-window drag. Explicit from_loc required for drag; never reads the real mouse position. Optional path contains intermediate points; duration 0.1-5 seconds. Native persistent hover and cross-window OS drag/drop are unsupported.")
    @json_errors
    async def move(window_id: str, observation_id: str, loc: list[int] | None = None, label: int | None = None, drag: bool = False, from_loc: list[int] | None = None, duration: float = 0.4, path: list[list[int]] | None = None):
        if not drag:
            if from_loc is not None or path is not None:
                raise ValueError("from_loc/path require drag=True")
            return await session.action("move", window_id, observation_id, loc, label)
        if from_loc is None or not 0.1 <= duration <= 5 or len(path or []) > 128:
            raise ValueError("Drag requires from_loc, 0.1-5 second duration and at most 128 path points")
        destination = session.resolve(window_id, observation_id, loc, label)
        points = []
        for point in path or []:
            resolved = session.resolve(window_id, observation_id, point)
            points.append({"x": resolved["x"], "y": resolved["y"]})
        return await session.action("drag", window_id, observation_id, from_loc, toX=destination["x"], toY=destination["y"], durationMs=round(duration * 1000), path=points)

    @mcp.tool(name="Scroll", description="Vertical browser wheel, explicitly targeted by observation. Native covered-window wheels and horizontal scrolling are unsupported; use captured scrollbar controls. No default to the physical mouse position.")
    @json_errors
    async def scroll(window_id: str, observation_id: str, loc: list[int] | None = None, label: int | None = None, direction: Literal["up", "down"] = "down", wheel_times: int = 1):
        if not 1 <= wheel_times <= 10:
            raise ValueError("wheel_times must be 1-10")
        return await session.action("scroll", window_id, observation_id, loc, label, delta=(120 if direction == "up" else -120) * wheel_times)

    @mcp.tool(name="Type", description="Unavailable in background mode: keyboard focus is shared. Never falls back to foreground typing.")
    @json_errors
    async def type_text(text: str):
        raise ValueError("independent_keyboard_unavailable: no text was typed")

    @mcp.tool(name="Shortcut", description="Unavailable in background mode: system keyboard shortcuts affect the user's foreground window.")
    @json_errors
    async def shortcut(shortcut: str):
        raise ValueError("independent_keyboard_unavailable: no shortcut was sent")

    @mcp.tool(name="MultiSelect", description="Unavailable as a background batch: Ctrl is shared and each click needs a new image. Use Snapshot and individual Click calls.")
    @json_errors
    async def multi_select():
        raise ValueError("Use fresh Snapshot + Click for each selection; shared Ctrl is not injected")

    @mcp.tool(name="MultiEdit", description="Unavailable in background mode: independent text input is not implemented.")
    @json_errors
    async def multi_edit():
        raise ValueError("independent_keyboard_unavailable: no fields were edited")

    @mcp.tool(name="App", description="Discover running apps and native/browser windows. mode=list returns windows; mode=find requires query. Tray/hidden/minimized is not stopped. Foreground launch/switch/resize modes are unavailable in background mode.")
    @json_errors
    async def app(mode: Literal["list", "find"] = "list", query: str | None = None):
        if mode == "find":
            if not query:
                raise ValueError("query is required")
            return await client.call("list_apps", query=query)
        return await client.call("list_windows")

    @mcp.tool(name="Wait")
    @json_errors
    async def wait(duration: float):
        if not 0 <= duration <= 30:
            raise ValueError("duration must be 0-30 seconds")
        await asyncio.sleep(duration)
        return {"waitedSeconds": duration}

    @mcp.tool(name="WaitFor", description="Read-only wait for named/enabled controls in one native window using Windows-MCP. Does not generate an input observation or activate windows. Browser pixels require a fresh Screenshot instead.")
    @json_errors
    async def wait_for(window_id: str, text: str, condition: Literal["element_exists", "element_enabled"] = "element_exists", timeout: float = 10):
        if window_id.startswith("browser:") or not 0 <= timeout <= 30:
            raise ValueError("WaitFor needs a native window_id and timeout 0-30 seconds")
        deadline = asyncio.get_running_loop().time() + timeout
        while True:
            result = await controls(window_id)
            for element in result.get("elements", []):
                if text.casefold() in element.get("name", "").casefold() and (condition != "element_enabled" or element.get("enabled") is True):
                    return {"matched": True, "element": element}
            if asyncio.get_running_loop().time() >= deadline:
                raise TimeoutError("Requested target control did not appear")
            await asyncio.sleep(0.25)

    @mcp.tool(name="ControlStatus")
    @json_errors
    async def control_status():
        return {"engine": "Windows-MCP+AgentMouse", "mouse": "background", "independentKeyboard": False, "status": await client.call("status")}

    # Keep the existing lifecycle/discovery names so installed AgentMouse skills still work.
    @mcp.tool(name="pointer_open")
    @json_errors
    async def pointer_open():
        return await client.call("open")

    @mcp.tool(name="pointer_status")
    @json_errors
    async def pointer_status():
        return await client.call("status")

    @mcp.tool(name="pointer_pause")
    @json_errors
    async def pointer_pause():
        return await session.pause()

    @mcp.tool(name="pointer_list_apps")
    @json_errors
    async def pointer_list_apps(query: str):
        return await client.call("list_apps", query=query)

    @mcp.tool(name="pointer_list_windows")
    @json_errors
    async def pointer_list_windows():
        return await client.call("list_windows")

    @mcp.tool(name="pointer_inspect_window")
    @json_errors
    async def pointer_inspect_window(windowId: str):
        return await controls(windowId)

    return mcp


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--node", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    os.environ["ANONYMIZED_TELEMETRY"] = "false"
    create_server(args.runtime, args.node, args.version).run(show_banner=False)
