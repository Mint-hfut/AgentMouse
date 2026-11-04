# AgentMouse

**Background computer use for AI agents on Windows, with a pointer independent of your real mouse.**

[中文](README.zh.md) · [Compatibility and evidence](docs/compatibility.md) · [Releases](https://github.com/Mint-hfut/AgentMouse/releases)

Keep working while your agent observes and operates another app. AgentMouse combines a Windows controller, MCP server, reusable Agent Skills and installers for **Claude Code, Codex, ZCode, WorkBuddy and DSH**. No virtual machine or split screen.

For web tasks, **Browser Bridge** reads real tab images and sends coordinate-based mouse input through Chrome's debugger API. It uses your existing browser/profile and does not rely on DOM selectors, injected clicks or per-site scripts. Native Windows capture/messages remain available for desktop apps.

**Beta:** real browser-extension gesture integration passes in headless Chrome for Testing. Final acceptance with a foreground user window is still pending because the interactive desktop became unavailable. This is not a universal app-compatibility or CAPTCHA-solving guarantee.

## Install

Requires Windows x64, Node.js **18+ x64** with npm and .NET Framework. Native covered-window capture requires Windows 10 1903+.

Extract the [Windows release ZIP](https://github.com/Mint-hfut/AgentMouse/releases), double-click **`install.cmd`**, and select your agents. From source:

```powershell
git clone https://github.com/Mint-hfut/AgentMouse.git
cd AgentMouse
.\install.cmd
```

The source installer installs locked dependencies without lifecycle scripts and compiles native files with Windows' .NET Framework compiler. No Visual Studio/.NET SDK is required. Codex native installation needs its CLI; ZCode uses its bundled plugin CLI in standard install locations or PATH.

```powershell
.\install.cmd --agent codex,zcode
.\install.cmd --agent all
.\install.cmd --agent dsh --profile web
.\install.cmd --agent codex --mode mcp
.\install.cmd --agent codex --browser none
```

Codex/ZCode default to **native plugin installation**, verified through their installed-plugin lists. Claude Code, WorkBuddy and DSH currently receive **MCP + skills**, which does not create a plugin card. `--mode mcp` selects that mode explicitly. `--browser none` skips browser-file preparation.

Refresh/restart the selected agents after installation. The native plugin is **`agentmouse@agentmouse`**.

### Connect your existing Chrome once

The installer prepares a paired extension directory and prints its path, normally `~/.agentmouse/browser/extension`. It does **not** change Chrome settings, profiles or enterprise policies.

1. In Chrome, open `chrome://extensions`, enable Developer mode and choose **Load unpacked** yourself.
2. Select the **generated directory printed by the installer**, not the unpaired source folder.
3. Review the debugger permission. It provides page images and mouse input. No remote debugging port, new browser profile or disabled GPU is needed for normal use.
4. Ask your agent to open AgentMouse, enable desktop input once in its local controller, then select a **`browser:`** entry from the window list for web work.

If enterprise policy blocks this, stop and consult your administrator. The installer does not bypass it. Browser preparation can also be run separately:

```powershell
node agentmouse.mjs browser-setup
node agentmouse.mjs browser-status
node agentmouse.mjs browser-stop
```

The controller UI is currently Chinese. Chrome may display its standard debugging indicator. Use one paired browser profile and one controlling agent at a time.

## Mouse and capture capabilities

| Tool | Behavior |
|---|---|
| `pointer_open` | Open the local controller; a new instance starts paused |
| `pointer_status` | Controller/capture capabilities and browser pairing status |
| `pointer_list_windows` | Native windows plus connected `browser:` tabs |
| `pointer_observe` | Desktop image, native window surface, or real browser viewport image |
| `pointer_move` | Overlay only by default; with a browser observation, move/hover its virtual pointer |
| `pointer_click` | Left/right click; `clickCount=2` double click or `holdMs` long press |
| `pointer_drag` | Press/move/release with optional intermediate `path` and bounded duration |
| `pointer_scroll` | Browser wheel; native covered-window wheels are rejected, use scrollbar clicks |
| `pointer_pause` | Stop input, release held buttons/captures and hide the overlay |

There is no permanent target binding. `capture="desktop"` uses visible screen-position routing; native `windowId` then only crops the screen. `capture="window"` addresses one HWND or `browser:` tab for that single-use observation. Reobserve another target to switch apps/tabs.

**Follow the returned coordinate space:** native captures use physical desktop pixels; browser images use viewport CSS pixels. Both return image origin and scaling metadata:

```text
x = screenshot.x + imageX * screenshot.scaleX
y = screenshot.y + imageY * screenshot.scaleY
```

Observations expire after 15 seconds and are consumed by input. Verify the actual image/state afterward; `inputDelivered` is not business success. Cancellation/failure releases buttons where the channel remains available; an unconfirmed release blocks more input. Forced termination cannot guarantee all cleanup.

Native messages cannot guarantee inactive Chromium windows remain behind: Chrome can activate itself during mouse presses. Those inputs are blocked and require Browser Bridge. Native app compatibility still varies; minimized/cloaked windows, independent keyboard input and cross-app system drag-and-drop are not promised. Touch is experimental and never a background fallback.

## Tested scope

The real extension path has passed local HTML checks for left/right clicks, wheel, native range slider, curved puzzle-style drag, long press, double click, ordered point selection, HTML5 drag/drop, and release after mid-drag pause. Assertions check page state **and captured pixels**. These are local interaction checks, **not third-party CAPTCHA acceptance**; server risk checks can still require a person.

Desktop non-interference, actual-client native loading and component tests are separate evidence categories. [The validation record](docs/compatibility.md) preserves failures and pending checks.

## Configuration and removal

Runtime: `%LOCALAPPDATA%\AgentMouse\0.6.0-beta.1` (physical path resolved for packaged Windows hosts). Browser pairing: `~/.agentmouse/browser`, with a per-user local token and authenticated loopback-only service. Generated secrets/private profiles stay outside the repo. Existing settings are retained with backups under the runtime's `backups/`.

MCP-only paths: Claude `~/.claude.json`, Codex `~/.codex/config.toml`, ZCode `~/.zcode/cli/config.json`, WorkBuddy `~/.workbuddy/mcp.json`, DSH `~/.dsh/profiles/<profile>/cordis.patch.yml`. Codex native upgrades relocate only AgentMouse's own marketplace when the versioned source changes. Native installer failures report possible partial completion; MCP-only settings writes support rollback.

Use `--dry-run` to preview after `npm ci --ignore-scripts`. `--home` isolates a home for testing; `--install-dir` selects the runtime. Do not retain a standalone MCP registration alongside the native plugin's same server. Remove native plugins in the host plugin manager; stop the browser bridge and remove its extension before deleting runtime/pairing files. Restoring whole backups also reverts later unrelated edits.

## Build and validation

```powershell
npm ci --ignore-scripts
npm run build
npm test
npm run test:state
node scripts/test-clients.mjs
```

The first four commands do not perform desktop input. `test-clients` reads installed Codex/ZCode payloads and checks their declared server/SDK path; it is not a GUI plugin-browser test. CI runs Node 18/24, isolated installer checks and a real extension/component test with pinned Chrome for Testing. The latter is headless and does not claim desktop non-interference.

`scripts/build-release.ps1` builds a complete ZIP and SHA-256 file; `scripts/test-release.ps1` verifies its extracted MCP installation. See [adapter references](adapters/README.zh.md) and [interactive test instructions](docs/compatibility.md).

[Apache-2.0](LICENSE). Not affiliated with the supported agent/browser products.
