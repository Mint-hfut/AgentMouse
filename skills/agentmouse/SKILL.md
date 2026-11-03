---
name: agentmouse
description: Operate visible Windows desktop apps through AgentMouse MCP. Use for screenshot-guided clicking, scrolling and cross-app GUI tasks with this plugin; not for Android or ordinary file editing.
---

# AgentMouse

The controller runs locally on Windows x64 with .NET Framework 4.x and Node.js 18+. The host model must accept image inputs for screenshot-guided work. MCP is portable across hosts; the Windows controller is not a macOS/Linux controller.

## Connect and operate

Discover the plugin's actual tool names from the host. Names may be prefixed, for example `mcp__agentmouse__pointer_observe`; do not invent a prefix.

- `pointer_open` opens the controller if necessary. A new instance is paused. `pointer_status` reports `armed`, `scope`, the backend and limitations. If paused, the user enables **whole-desktop** input once in the controller. There is no application binding and no `pointer_select_window` tool.
- `pointer_observe` defaults to the entire visible desktop. Optional `windowId` only crops the screenshot; it never restricts later input. Match the intended app and control in the actual visible image before acting.
- `pointer_click` and `pointer_scroll` route by screen location to the visible window underneath. Use a fresh `observationId` after each action. An observation expires after 15 seconds; reobserve after layout, occlusion or window changes.
- `pointer_move` moves only the orange AGENT overlay. It does not produce native mouse hover.

All action coordinates are **physical screen pixels**, including negative monitor coordinates. Preview images may be resized. Convert an image point using the returned metadata:

```text
screenX = screenshot.x + imageX * screenshot.scaleX
screenY = screenshot.y + imageY * screenshot.scaleY
```

Round to integers. Do not pass raw preview-image coordinates as desktop coordinates when the scale or origin differs.

## Verify effects

`inputDelivered=true` means input submission, not task completion. Reobserve and verify the requested effect: changed UI state, the expected document position, player title/state, or another task-specific result. A timeout or interruption can occur after partial effects; inspect before deciding whether a retry is appropriate.

Default to the background-message backend. Touch is experimental and may move the real pointer or activate a window; `pointer_drag` is a touch gesture, not a fully independent mouse drag. Report unavailable capabilities rather than describing them as verified.

The service currently has no keyboard/text-entry tool. If the task needs text input, use an available host capability within the user's task scope, and distinguish that assistance from this mouse service. The same applies to launching a target application: `pointer_open` launches the controller, not an arbitrary app.

## Finish or stop

After submitting desktop input or moving this plugin's overlay, call `pointer_pause` when that desktop task is completed or interrupted. Also pause whenever the user explicitly asks to stop. Read-only discovery/status checks need not pause a different ongoing controller. Pause disables input and hides the AGENT overlay; it need not terminate the controller window.

A graceful MCP disconnect also attempts to pause after this connection has submitted pointer actions. Hosts may keep MCP connections alive between tasks, so explicitly pause at task end. Forced process termination is not a guaranteed cleanup mechanism. Use one controlling agent session at a time; concurrent input ownership is not implemented.

For setup, status, stopping and diagnostics, the bundle also includes `agentmouse-start`, `agentmouse-status`, `agentmouse-stop`, and `agentmouse-doctor` skills plus the `agentmouse.mjs` CLI.
