---
name: agentmouse
description: Use AgentMouse MCP for background Windows/browser computer use without taking over the user mouse. Read real page/window images, click, hover, drag slider/puzzle paths and select points. Browser verification gestures require the paired Browser Bridge; not Android or file editing.
---

# AgentMouse

Discover the actual tool namespace from the host; do not invent one. The local controller requires Windows x64 and Node 18+. Native HWND capture requires Windows 10 1903+. Screenshot-guided tasks need an image-capable model.

## Connect and select the correct channel

- `pointer_open` opens the controller; new instances start paused. The user enables whole-desktop input once in its window. MCP cannot arm it. There is no permanent application binding.
- Inspect `pointer_status`, `pointer_list_apps(query="app name")` and `pointer_list_windows`. For **web tasks**, choose a **`browser:`** tab entry and call `pointer_observe` with that ID and `capture="window"`. Browser Bridge uses real images and mouse input, without DOM selectors or injected clicks.
- If no browser entry is present, inspect `node agentmouse.mjs browser-status` when the bundle path is available. Installation prepares `~/.agentmouse/browser/extension`; the user must load that generated extension and review Chrome's permission once. `browser-setup` prepares files only. Do not bypass browser/enterprise policy or silently change user profiles.
- Do not substitute native inactive Chrome mouse messages: Chromium can activate/raise its window. This route is blocked. Never activate, raise, restore or temporarily bring an app to the front to satisfy a background task.
- A supplied `windowId` defaults to `capture="window"`, reading that actual HWND/tab. Whole-desktop capture is for orientation; never use its foreground image as the background app image.

## Discover the app and its window state

A missing visible window does not mean an app is closed. Query running processes before launching: e.g. `pointer_list_apps(query="微信")` recognizes Weixin/WeChat, including tray/background processes. If already running but hidden/minimized, report this state; do not launch another instance or restore/activate it as an unannounced fallback. Login and main windows can belong to different PIDs/HWNDs; compare title, class, PID, owner and bounds before choosing.

Window inventory lists `foreground`, `visible`, `minimized`, `cloaked`, `canCapture`, `zOrder` (front-to-back) and `overlappingWindowsAbove`. Overlap uses rectangles, so shaped/transparent windows can differ. Hidden/minimized windows can be found but cannot be captured or clicked. Switching the user's foreground to ZCode does not turn the background WeChat HWND into ZCode.

Use `pointer_inspect_window(windowId)` to read accessible control names/roles and bounds. It never activates, invokes or types. Custom-rendered apps may expose only a title bar; do not invent missing controls. This provides Windows-MCP-style semantic context alongside real images, without enabling its foreground mouse/keyboard tools.

Every click/drag/scroll must declare the intended `windowId` as well as the matching observation token. Verify returned recipient PID/class/ID and then inspect a fresh image. The orange overlay appears above other windows; its visual location does not prove which app received an event.

## Coordinates and gestures

Follow `coordinateSpace`. Browser images use viewport CSS pixels, native images use physical desktop pixels. Convert an image point using:

```text
x = screenshot.x + imageX * screenshot.scaleX
y = screenshot.y + imageY * screenshot.scaleY
```

Round to integers. A fresh `observationId` is single-use and expires after 15 seconds. Reobserve after layout changes, errors or input; do not automatically retry an action with an unknown outcome.

- `pointer_click`: left/right; `clickCount=2` double-click or `holdMs` long press, not both.
- `pointer_drag`: press/move/release within a window/page. `path` contains optional intermediate coordinate points; `durationMs` is bounded. Browser HTML5 drags are intercepted so they do not enter an OS drag loop.
- `pointer_scroll`: browser wheel. Native covered-window wheels are rejected because apps may drop/re-route them using the foreground hit-test; use captured scrollbar/page buttons there.
- `pointer_move`: without an observation, only moves the Agent overlay. With a browser observation, moves/hovers the browser virtual pointer. Native persistent hover is unsupported.

## Verify outcomes

`inputDelivered=true` is not task success. If `inputAllowed=false` or `unverified_frame` is returned after an action, native capture has not produced a newer frame: stop input, inspect the control tree, and report that the app may have ignored the message or stopped repainting. Do not repeat clicks, test unrelated account controls, relaunch, or switch to clicking the foreground app. A new frame timestamp alone also does not prove business success. Confirm the expected page/control state in a new image. Repeated/unchanged frames may mean a static view or an app that stopped repainting; never call them proof of a changed state. The service checks capture source/geometry and refuses a missing background image or a desktop-crop fallback.

Local slider, puzzle-style drag, point-selection and mouse-event fixture tests do **not** guarantee any third-party CAPTCHA provider accepts automation. Device/behavior/risk checks are separate. Do not promise universal verification success or add stealth/evasion behavior; hand back to the user when a real site requires a person.

There is no independent keyboard tool. Do not use foreground keyboard/mouse injection as a hidden fallback while the user is working elsewhere. Explain missing capabilities. Touch is experimental, can affect real cursor/focus, and is never a covered-window fallback.

## Finish and stop

Call `pointer_pause` after this task's input or capture work, on interruption, and whenever the user asks to stop. It releases held buttons/captures and hides the overlay. Status/list-only probes need not pause someone else's work. Focus/z-order interference and an unconfirmed release must stop further input; inspect before continuing.

Use one paired browser profile and one controlling agent session at a time. Graceful disconnect and browser watchdogs attempt cleanup; forced termination of all processes is not a guaranteed release mechanism. The bundle also provides start/status/stop/doctor skills and the `agentmouse.mjs` CLI.
