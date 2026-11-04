"""Read one HWND through Windows-MCP's tree engine; run in a disposable process."""
import json
import sys
import ctypes
from dataclasses import asdict

ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))


def inspect(window_id):
    from windows_mcp.infrastructure.thread_affinity import _executor

    def read():
        import win32gui
        import win32process
        from windows_mcp.desktop.service import Desktop
        hwnd = int(window_id)
        if not win32gui.IsWindow(hwnd):
            raise ValueError("window_missing")
        pid = win32process.GetWindowThreadProcessId(hwnd)[1]
        bounds = win32gui.GetWindowRect(hwnd)
        desktop = Desktop()
        state = desktop.tree.get_state(None, [hwnd], use_dom=False)
        elements = []
        for node in state.interactive_nodes + state.scrollable_nodes:
            value = asdict(node)
            value["enabled"] = True if node in state.interactive_nodes else None
            if value.get("metadata", {}).get("is_password"):
                value["metadata"]["value"] = "[password]"
            value["label"] = len(elements)
            elements.append(value)
        if not win32gui.IsWindow(hwnd) or win32process.GetWindowThreadProcessId(hwnd)[1] != pid or win32gui.GetWindowRect(hwnd) != bounds:
            raise ValueError("window_changed")
        return {"windowId": str(hwnd), "pid": pid, "bounds": bounds, "elements": elements, "truncated": state.truncated, "source": "Windows-MCP.UIAutomation"}

    return _executor.submit(read).result()


if __name__ == "__main__":
    try:
        print(json.dumps(inspect(sys.argv[1]), ensure_ascii=True))
    except Exception as error:
        print(json.dumps({"error": str(error)}))
        sys.exit(1)
