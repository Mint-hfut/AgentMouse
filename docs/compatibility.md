# Compatibility and evidence

AgentMouse has two input routes. Native Windows capture/messages provide desktop access with application-dependent input behavior. Browser Bridge provides page images and mouse gestures through Chrome's debugger API, without native mouse messages that may activate the window. Neither is a universal second Windows input seat.

## Current acceptance status

| Check | Evidence | Limit |
|---|---|---|
| Live WeChat after restart | Failed: isolated native screenshots returned, but one click raised WeChat and did not switch to QQ Mail reminders; controller paused | No email read; no WeChat UIA or physical mouse fallback. [Sanitized pilot](test-results/wechat-pilot-2026-11-04.json), [audit](audits/2026-11-04.md) |
| Unreleased beta.4 native input fixes | Old-backend regressions fail; covered Qt button business event, transparent-child routing and four release-failure scenarios pass with fixes | Owned fixtures only; live WeChat remains quarantined. [Results](test-results/native-input-2026-11-04.json) |
| Native plugin installation | Codex CLI 0.153.4 and ZCode CLI 0.17.1 report AgentMouse installed/enabled | Plugin-browser visual acceptance was stopped by the user; running conversations were not restarted |
| Declared MCP connectivity | Current source SDK checks 11 Core or 26 Full tool names; historical beta.3 Full had 27 | Separate from an actual model choosing a tool; installed ZCode beta.3 is disabled after the WeChat incident |
| Windows-MCP fusion | Real Windows control-label/WGC/background-message/result integration, SDK and five host adapter checks passed | Independent keyboard is unavailable; [fusion behavior and evidence](fusion.md) |
| Fusion browser desktop workflow | Ten cases passed with Chrome fully covered by foreground ChatGPT | [Separate fusion report](test-results/fusion-desktop-2026-11-04.json); local fixture, not real CAPTCHA-provider acceptance |
| Windows background capture | Real Chrome HWND pixels captured using Windows.Graphics.Capture while covered | Apps can stop repainting; no desktop-crop fallback |
| Native inactive Chrome input | Further testing reproduced activation/raising by Chrome | Now blocked; use Browser Bridge |
| Real browser extension | Chrome for Testing 157.0.8103.38, actual unpacked extension → authenticated bridge → MCP | Passed headless components and the separate covered-window desktop workflow |
| Foreground user window remains in front during full browser workflow | 2026-11-04: all ten checks passed with Chrome fully covered by ZCode; no focus, relative z-order or unattributed cursor movement detected | One Windows environment and local fixture; [machine-readable result](test-results/desktop-2026-11-04.json) |
| Third-party CAPTCHA acceptance | Not tested | Local widget tests do not prove server risk checks accept automation |

Both locally installed Codex/ZCode plugins were previously upgraded to beta.2 and passed cached-manifest/MCP status checks. ZCode's first upgrade failed with `EPERM` while renaming its staged cache; rerunning the installer completed its managed reinstall without errors. This intermittent native-client installation failure is not counted as a successful first attempt. Packaged native installation and repeat-install checks also passed in separate temporary homes.

## Real extension/component checks

The passing extension test used no GPU-disabling or background-throttling switches. It loaded the actual extension in an isolated Chrome for Testing profile, connected its service worker to the production authenticated loopback bridge, and exercised the real MCP handler/router and shared browser pointer engine.

The test browser rendered a local HTTP fixture. The fixture reports its own state to a separate test HTTP server. The controller chooses input coordinates from returned PNG colors, not DOM selectors. No product code injects scripts or clicks through a page DOM API. CDP in the test harness starts/cleans up the disposable browser; the input/capture path under test is the extension's `chrome.debugger` API.

Passed component cases:

- Left/right click and wheel, with actual page counters/scroll position.
- HTML range slider reaching its acceptance threshold.
- Puzzle-style pointer-capture drag following a path with a turn, held-button samples and successful drop geometry.
- Long press exceeding the fixture's 700ms threshold.
- Double-click producing exactly one `dblclick` result.
- Ordered point selection A → C → B.
- HTML5 drag/drop with intercepted drag data, keeping it out of the OS drag loop.
- Mid-drag pause followed by page pointer-up and confirmed released state.

Assertions check the fixture state **and expected pixels in a newly captured image**. Screenshot sampling time is not a native DWM frame timestamp. The demo [browser-verification.png](images/browser-verification.png) is an unmodified capture of the account-free fixture, from the covered-window desktop test.

The first restored-desktop attempt timed out at `Page.captureScreenshot` while the page was occluded. Browser observations now enable `Emulation.setFocusEmulationEnabled`, keeping the page rendering without activating its Windows window. Detaching the debugger clears that override; the page can receive simulated focus/visibility events while attached. The passing desktop test retained GPU acceleration and normal occlusion settings. Its sole launch adjustment was `--force-color-profile=srgb`, to make fixture RGB receipts independent of the monitor profile; production does not require this flag. See [Chromium's implementation](https://raw.githubusercontent.com/chromium/chromium/main/content/browser/devtools/protocol/emulation_handler.cc).

The desktop run also exposed sparse drag samples caused by slower native permission checks. Drag interpolation now preserves each explicit corner and at least twenty movement samples, even when delivery is slow. Requested duration is a minimum schedule, not a wall-clock upper bound. A regression check simulates slow permission checks and verifies corner delivery.

These checks validate mouse execution primitives. They do not solve CAPTCHA images, defeat bot detection, validate a third-party account flow or guarantee every real site accepts the gesture. Site verification may require a person.

## Why native Chrome clicks are not the guaranteed route

Earlier native-message experiments succeeded in selected cases, but subsequent tests exposed focus/z-order changes. Chromium's activation code explicitly calls `SetWindowPos(HWND_TOP)` and `SetForegroundWindow` during activation, including activation requested by mouse presses. A pointer coordinate independent of the system cursor does not suppress application code. Inactive Chromium native mouse input is therefore rejected before delivery in this build.

Native wheel handling has another problem: Chromium's wheel routing uses `WindowFromPoint` and may suppress/re-route the message according to the covering window. Native covered-window wheel input is rejected; a visual scrollbar click is a fallback for supported native apps. Browser Bridge wheels go directly to the captured page viewport.

PrintWindow experiments produced an empty page after an occluded resize. Native WGC captured real surfaces, but stock Chrome/GPU combinations could retain old page pixels after DOM changes. Disabling GPU/throttling helped a test configuration but did not establish reliable non-activation; those switches are not the production solution. The passing web gesture path uses Browser Bridge instead.

Primary references: [Chromium activation](https://raw.githubusercontent.com/chromium/chromium/main/ui/views/win/hwnd_message_handler.cc), [wheel routing](https://raw.githubusercontent.com/chromium/chromium/main/ui/base/win/mouse_wheel_util.cc), [occlusion behavior](https://chromium.googlesource.com/chromium/src/+/main/docs/windows_native_window_occlusion_tracking.md), [Chrome debugger API](https://developer.chrome.com/docs/extensions/reference/api/debugger), [Windows capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture).

## Native capture/input limits

### WeChat incident and beta.3 fixes

The provided ZCode trace used AgentMouse beta.2. The initial list contained no visible WeChat window, and the agent launched/relaunched Weixin without first checking running processes. Later actions used `capture=window` and the WeChat HWND. Recorded receivers were WeChat's `MMUIRenderSubWindowHW`, with `targetOccludedAtPoint=true`; the trace does not show those input messages being addressed to ZCode. However, subsequent captures repeatedly returned the exact pre-action native frame timestamp, and the agent retried clicks and a drag without a verified result. An overlay drawn over ZCode is not evidence of message delivery to ZCode.

Beta.3 adds process discovery (including WeChat/Weixin aliases), hidden/minimized window inventory, PID/class/owner/bounds, front-to-back depth and overlapping windows above. Rectangular overlap is advisory, not pixel-accurate occlusion. A running process is not proof that the desired account/session is logged in. Hidden/minimized windows are found but not restored automatically.

The read-only `pointer_inspect_window` uses .NET UIAutomation in a short-lived CLI process with an external timeout; this bounds the caller's wait but cannot guarantee that the target's provider stays responsive. It reads control names/roles, not password values, and never calls focus/Invoke/keyboard methods. Reviewing the local [Windows-MCP project](https://github.com/CursorTouch/Windows-MCP) informed this split between app/process/window/control discovery. Its foreground Click/App-switch implementations were not adopted in Core; Full later added the separately vendored upstream system/semantic modules and bundled Python.

All mouse actions now declare their intended window ID. MCP and native/browser routes check the target against the observation/hit receiver. A supplied window ID defaults to a real window capture; combining it with a desktop crop is rejected. After native input, an unchanged/older frame blocks further input with `unverified_frame` (also exposed as `inputAllowed=false`). A new frame only permits reassessment; it does not prove a business outcome. Skills explicitly stop retries/relaunches and distinguish overlay position from delivery target.

Current-machine read-only checks found the already-running hidden WeChat main window and obtained six accessible window/title-bar/button nodes, without exposing chat content. This does not establish that the login or main app's custom-rendered controls support background execution. The WeChat → QQ Mail business workflow has **not** passed.

`npm run test:routing` uses two owned overlapping WinForms windows to verify layers, UIAutomation, wrong-target rejection, delivery to the covered target (never the decoy), replayed frozen-frame rejection, and hidden/minimized discovery. It checks actual native-message receipt, separately from a WinForms business Click event: the latter did not fire in the exploratory covered-button test. The new full-browser desktop rerun aborted when the foreground changed; its partial result is not recorded as a new complete desktop pass. The beta.2 ten-case desktop report above remains historical evidence.

Windows 10 1903+ is required for HWND capture. Each capture now runs in an isolated helper with an MTA thread, closing its frame/session before returning. Timeout/pause can terminate that owned helper directly. PID, geometry, mapped buffers and image size are checked; previews are capped at 4 million pixels. Minimized/cloaked windows are rejected. Image hashes drive `frameChanged`; unchanged post-action pixels block further input even when a new capture timestamp exists. No business success is inferred from that.

Native input remains application-dependent. Focus/relative z-order changes pause input. Native dragging is intra-window; independent keyboard, persistent system hover and cross-app system drag/drop are not promised. Touch is experimental and is never a covered-window fallback. The controller starts paused. For an explicitly authorized task, Core `pointer_open(allowInput=true)` or Full `pointer_open(allow_input=true)` enables background input after input monitoring is ready and no held contact remains; an authorization already given need not be requested again.

## Browser bridge safety and pairing

The service binds only to `127.0.0.1`, checks Host/Origin, requires a random per-user token, bounds request sizes and exposes only the listed capture/input commands. Generated configuration stays in the user home, outside Git. One paired profile and one input owner are supported at a time. The extension never activates/selects a tab to execute a gesture and uses explicit zero input modifiers.

Mouse release runs on errors/pause. Client disconnect cancels its gesture; an extension-side watchdog releases an abandoned held button. An unconfirmed release blocks further input. Forced termination of all relevant processes is not a guaranteed cleanup path. Chrome's debugger permission/indicator is explicit; no enterprise policies are bypassed.

## Reproduce

```powershell
npm ci --ignore-scripts
npm run build
npm test
npm run test:state
node scripts/test-clients.mjs
```

Ordinary tests use temporary homes, mocked browser transports where stated, actual MCP stdio/SDK, native compilation and state checks. They do not perform desktop input. `test-clients` requires installed clients and is read-only.

Owned native-window regressions require an interactive Windows desktop:

```powershell
npm run test:routing
npm run test:native:failures
npm run test:fusion:windows
```

For the separate Qt business-event fixture, install its test dependency into an isolated directory (not either product runtime). PyQt5 5.15.11 supplies Qt 5.15.2 on this machine:

```powershell
uv pip install --python python --target compatibility-results/qt-fixture-runtime PyQt5==5.15.11
$env:AGENTMOUSE_QT_PYTHON = (Get-Command python).Source
$env:PYTHONPATH = (Resolve-Path compatibility-results/qt-fixture-runtime).Path
npm run test:fusion:qt
```

The fixture sends input only to its nonce/PID-scoped temporary window through Full MCP, checks a real `QPushButton.clicked` counter and a changed WGC image, and retains the existing foreground/cursor. It does not query UIA or touch real WeChat. The ordinary WinForms routing check still asserts message receipt, not a business Click event.

For the **real extension component** test (Node 22+):

```powershell
$env:AGENTMOUSE_CHROME = .\scripts\get-test-chrome.ps1
node scripts/test-background-chrome.mjs --headless-test --extension-bridge-test
```

Chrome for Testing is used because normal branded Chrome removed the command-line extension loader. This is Google's supported development/test distribution, not an attempt to bypass an administrator block. Production users load the generated extension themselves. [Official notice](https://groups.google.com/a/chromium.org/g/chromium-extensions/c/1-g8EFx2BBY/m/my-groups).

Omit `--headless-test` to reproduce the covered-window desktop acceptance with an existing foreground window. The harness lowers only its owned fixture, checks foreground/z-order, and aborts on interference. `--native-messages` is a diagnostic for the rejected native Chrome route and is expected to fail, not an acceptance pass.

Build/check ZIPs with `scripts/build-release.ps1` and `scripts/test-release.ps1`; `-NativeClients` additionally checks native Codex/ZCode installation in isolated homes. Raw reports, browser profiles and personal captures are excluded from Git. Historical 0.3/0.4 checks of selected Chrome/Edge/Notepad/Word actions are not treated as current universal guarantees; audio playback was never verified.
