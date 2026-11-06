---
name: agentmouse
description: Use AgentMouse MCP for background Windows/browser computer use without taking over the user mouse. Read real page/window images, click, hover, drag slider/puzzle paths and select points. Browser verification gestures require the paired Browser Bridge; not Android or file editing.
---

# AgentMouse

Discover the actual tool namespace from the host; do not invent one. The local controller requires Windows x64 and Node 18+. Native HWND capture requires Windows 10 1903+. Screenshot-guided tasks need an image-capable model.

## Protected native operations

Native window pointer actions require the installed activation guard. Check `activationGuard=true` in controller and window metadata. The controller obtains target-thread protection before sending native input. Use the normal MCP tools and verify each action from a fresh image. Accessibility inspection of WeChat processes remains quarantined, so use `Screenshot` or `Snapshot(use_ui_tree=false)` and read the image.

For an explicitly authorized task, call Full `pointer_open(allow_input=true)` or Core `pointer_open(allowInput=true)` without asking the user to enable the controller again. Input monitoring and button-release checks remain mandatory. Stop input after changed identity/layout, unconfirmed release, or reported foreground interference. If an action leaves the source image unchanged, list windows and inspect a newly visible popup only when its owner/PID proves it belongs to the intended app. A fresh observation of that verified popup may authorize its own action. Do not click the unchanged source again. For any existing hidden or minimized application, invoke App mode=show with its discovered window_id, then confirm a fresh Screenshot. Restore uses a verified registered tray entry under protection. Applications without an unambiguous restore capability return an explicit error. Never force native visibility or launch another instance merely because capture is unavailable.

Full no longer exposes arbitrary PowerShell execution. Snapshot defaults to image-only; set `use_ui_tree=true` only for an explicitly selected, non-quarantined target when controls are actually needed. Inspect `providerSucceeded/error`, and never interpret a failed provider's empty tree as success.

## Windows-MCP fusion engine

If the catalog exposes `Snapshot`, `Screenshot`, `Click`, `Move`, and `Scroll`, use this engine instead of the native-only `pointer_observe/click/drag/scroll` names below. The lifecycle/discovery tools `pointer_open/status/pause/list_apps/list_windows/inspect_window` remain available.

1. Discover running apps with `App(mode="find", query="微信")` or `pointer_list_apps`; list HWND/browser IDs with `App(mode="list")`.
2. Call `Snapshot(window_id="...")` for a real background image plus Windows-MCP controls, or `Screenshot(window_id="...")` for image only. Without a target, these are desktop overviews and cannot authorize a target action.
3. Pass the returned `observationId` as `observation_id`, alongside the same `window_id`, to `Click`. Choose either `loc=[x,y]` in the returned coordinate space or a `label` from that exact Snapshot. Labels are scoped to one observation/window.
4. Drag with `Move(drag=True, from_loc=[x,y], loc=[endX,endY], duration=0.65, window_id=..., observation_id=...)`; optional `path` is a list of intermediate `[x,y]` points. The real cursor is never the implicit start. Browser and protected Qt wheel use `Scroll` with an explicit loc/label inside the scrollable region and direction. Scroll the appropriate list region to discover items below the visible rows.
5. Reobserve and verify a changed control/image. `inputDelivered` is not business success. Pause after work or interruption.

Windows-MCP's `FileSystem`, `Process`, `Registry`, HTTP `Scrape`, `DisplayInventory`, `Clipboard`, and `Notification` are retained. File/clipboard operations act on the real shared system. `WaitFor` reads controls in one non-quarantined native target; it does not create an input observation.

Independent keyboard remains unavailable: `Type`, `Shortcut`, Ctrl/batch selection and `MultiEdit` return explicit errors. `App` discovers apps/windows; automatic launch/resize/switch, foreground DOM scraping, middle click, native hover and horizontal/covered-native wheel are not substituted with physical input. Never work around these limits by injecting foreground keys/mouse while the user works elsewhere.

The sections below describe the native-only engine's tool names and the shared discovery/verification rules.

## Connect and select the correct channel

- `pointer_open` opens the controller paused by default. If the user explicitly authorizes input for the current task, use Core `allowInput=true` / Full `allow_input=true`; do not request the same authorization again. Input monitoring must be ready and no held contact may remain. There is no permanent application binding.
- Inspect `pointer_status`, `pointer_list_apps(query="app name")` and `pointer_list_windows`. For **web tasks**, choose a **`browser:`** tab entry and call `pointer_observe` with that ID and `capture="window"`. Browser Bridge uses real images and mouse input, without DOM selectors or injected clicks.
- If no browser entry is present, inspect `node agentmouse.mjs browser-status` when the bundle path is available. Installation prepares `~/.agentmouse/browser/extension`; the user must load that generated extension and review Chrome's permission once. `browser-setup` prepares files only. Do not bypass browser/enterprise policy or silently change user profiles.
- Prefer Browser Bridge for normal browser tabs. Native HWND actions acquire activation protection and still require image-based business verification. Restore hidden/minimized applications through App mode=show and its verified application entry. Preserve the user foreground throughout.
- A supplied `windowId` defaults to `capture="window"`, reading that actual HWND/tab. Whole-desktop capture is for orientation; never use its foreground image as the background app image.

## Discover the app and its window state

A missing visible window does not mean an app is closed. Query running processes before launching: e.g. `pointer_list_apps(query="微信")` recognizes Weixin/WeChat, including tray/background processes. If already running but hidden/minimized, invoke App mode=show with its exact window_id, verify its restored state, and take a fresh image. If no verified restore entry exists, report the capability error. Do not launch a duplicate instance. Login and main windows can belong to different PIDs/HWNDs; compare title, class, PID, owner and bounds before choosing.

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
- `pointer_scroll`: browser and protected Qt vertical wheel. Other native targets require captured scrollbar/page controls until their routing is verified.
- `pointer_move`: without an observation, only moves the Agent overlay. With a browser observation, moves/hovers the browser virtual pointer. Native persistent hover is unsupported.

## Verify outcomes

`inputDelivered=true` is not task success. If `inputAllowed=false` or `unverified_frame` is returned after an action, native capture has not produced a newer frame: stop input, inspect the control tree, and report that the app may have ignored the message or stopped repainting. Do not repeat clicks, test unrelated account controls, relaunch, or switch to clicking the foreground app. A new frame timestamp alone also does not prove business success. Confirm the expected page/control state in a new image. Repeated/unchanged frames may mean a static view or an app that stopped repainting; never call them proof of a changed state. The service checks capture source/geometry and refuses a missing background image or a desktop-crop fallback.

Local slider, puzzle-style drag, point-selection and mouse-event fixture tests do **not** guarantee any third-party CAPTCHA provider accepts automation. Device/behavior/risk checks are separate. Do not promise universal verification success or add stealth/evasion behavior; hand back to the user when a real site requires a person.

There is no independent keyboard tool. Do not use foreground keyboard/mouse injection as a hidden fallback while the user is working elsewhere. Explain missing capabilities. Touch is experimental, can affect real cursor/focus, and is never a covered-window fallback.

## Finish and stop

Call `pointer_pause` after this task's input or capture work, on interruption, and whenever the user asks to stop. It releases held buttons/captures and hides the overlay. Status/list-only probes need not pause someone else's work. Focus/z-order interference and an unconfirmed release must stop further input; inspect before continuing.

Use one paired browser profile and one controlling agent session at a time. Graceful disconnect and browser watchdogs attempt cleanup; forced termination of all processes is not a guaranteed release mechanism. The bundle also provides start/status/stop/doctor skills and the `agentmouse.mjs` CLI.
