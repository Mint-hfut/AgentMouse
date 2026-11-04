# AgentMouse

**Background computer use for AI agents on Windows — an independent Agent pointer that leaves your mouse to you.**

[中文说明](README.zh.md) · [Compatibility](docs/compatibility.md) · [Issues](https://github.com/Mint-hfut/AgentMouse/issues)

AgentMouse gives MCP-capable agents desktop screenshots, clicking and scrolling across your current Windows desktop. Its orange **AGENT** pointer has its own coordinates: moving your real mouse does not move the Agent pointer. The default background mode sends window messages without moving the system cursor or requesting foreground activation.

No virtual machine, split screen or driver installation. MCP tools, Agent Skills and lifecycle commands share one local controller. The installer supports **Claude Code, Codex, ZCode, WorkBuddy and DSH**.

**Status: experimental, Windows x64 only.** Background input compatibility depends on the application. This is not a second Windows input seat: applications can still change focus, and touch mode may move the real cursor. It operates visible on-screen windows, not arbitrary hidden or minimized apps. See [tested scope and limitations](docs/compatibility.md).

## Install

Requires Windows x64, Node.js **18+ x64** with npm, and Windows .NET Framework 4.x. A vision-capable host model is needed to interpret screenshots. Administrator rights are normally unnecessary.

Download a complete Windows ZIP from [Releases](https://github.com/Mint-hfut/AgentMouse/releases) when available, extract it, and double-click **`install.cmd`**. Select one or more agents from the menu. The release includes native binaries and installer dependencies; Node.js is still required.

From source:

```powershell
git clone https://github.com/Mint-hfut/AgentMouse.git
cd AgentMouse
.\install.cmd
```

The source installer downloads locked npm dependencies without running package lifecycle scripts and compiles the controller using Windows' .NET Framework compiler. No Visual Studio or .NET SDK is required.

**Since 0.5.1, selecting Codex or ZCode installs a native plugin**, not just a standalone MCP entry. Codex needs its CLI (`npm install -g @openai/codex` if missing); ZCode uses its bundled plugin CLI, detected in the standard Windows install locations or PATH. Installation fails explicitly if the native CLI cannot be found. Claude Code, WorkBuddy and DSH currently use MCP + skills registration, which does not create a plugin card in their plugin browser.

To skip the menu:

```powershell
.\install.cmd --agent codex
.\install.cmd --agent claude-code,codex,dsh
.\install.cmd --agent all
.\install.cmd --agent dsh --profile web
.\install.cmd --agent codex --mode mcp
```

Refresh the plugin browser or restart the selected agents afterward. Codex and ZCode should list **agentmouse@agentmouse** as installed/enabled. The installer verifies the native client's installed-plugin response before reporting success. `--mode mcp` explicitly chooses standalone MCP + skills and does **not** create a plugin card. Ask your agent: **“Use AgentMouse to open its controller and check its status.”** Click **允许全桌面操作** (Allow desktop input) once in the controller, then give the task. The controller UI is currently Chinese.

## Installation details

The persistent runtime defaults to `%LOCALAPPDATA%\AgentMouse\0.5.1`. Windows packaged hosts may redirect this location; the installer resolves the physical path so other agents can read it. Native mode creates a clean `plugin/` payload and marketplace catalogs, then uses each host's native marketplace/install commands. Backups stay outside that payload. Deleting the download/source folder afterward is safe. Node.js must remain installed.

The following table describes **MCP-only mode** (the default for Claude Code, WorkBuddy and DSH; use `--mode mcp` for Codex/ZCode):

| Agent | User MCP configuration | Skills location |
|---|---|---|
| Claude Code | `~/.claude.json` → `mcpServers.agentmouse` | `~/.claude/skills/agentmouse*` |
| Codex | `~/.codex/config.toml` → `mcp_servers.agentmouse` | `~/.agents/skills/agentmouse*` |
| ZCode | `~/.zcode/cli/config.json` → `mcp.servers.agentmouse` | `~/.zcode/skills/agentmouse*` |
| WorkBuddy | `~/.workbuddy/mcp.json` → `mcpServers.agentmouse` | `~/.codebuddy/skills/agentmouse*` (compatible user skills) |
| DSH | `~/.dsh/profiles/<profile>/cordis.patch.yml` → managed MCP client | `~/.dsh/skills/agentmouse*` |

`~` means your user home. Codex skills use the shared Agent Skills directory and may also be discovered by other compatible hosts. MCP registration is added only to selected agents. Commands are copied for Claude Code, ZCode and WorkBuddy; every host receives equivalent lifecycle skills. DSH defaults to `web` and needs its own existing model/provider configuration.

Unrelated settings are retained. Changed files are backed up under the runtime's `backups/`, with an `index.json` mapping. Repeating an installation makes no duplicate entries. In MCP-only mode, malformed configurations fail before settings are written and write failures roll back applied changes. Native mode backs up the client's configuration/registries, uses its own installer, and reports any partial failure; it does not claim transactional rollback of native client operations. An existing unmanaged AgentMouse TOML entry is left alone in MCP-only mode. Close settings editors during installation.

Preview without writing settings or runtime files:

```powershell
npm ci --ignore-scripts
.\install.cmd --agent all --dry-run
```

Advanced: `--home <directory>` selects an alternate home and ignores host-specific environment overrides. `--install-dir <directory>` selects the runtime directory. Normal installation respects `CODEX_HOME`, `CLAUDE_CONFIG_DIR` (skills/commands), and `DSH_HOME`.

For a native plugin, uninstall through the host's plugin manager (`codex plugin remove agentmouse@agentmouse`, or ZCode's installed-plugin page). For MCP-only mode, stop AgentMouse, remove only its `agentmouse` MCP entry (DSH: `agentmouse-mcp` inserted plugin), and delete the five project skill folders and AgentMouse command folder. Delete the runtime last. Restoring a complete backup also reverts unrelated settings edited after that backup. If migrating from 0.5.0, remove its standalone MCP registration to avoid duplicate tools alongside the native plugin.

## Use from any MCP agent

For another MCP host, configure this stdio server using absolute paths:

```json
{
  "mcpServers": {
    "agentmouse": {
      "command": "C:\\Program Files\\nodejs\\node.exe",
      "args": ["C:\\path\\to\\AgentMouse\\mcp-server.mjs"]
    }
  }
}
```

Point it at the built source directory or installed runtime (`plugin/` in native mode). `node agentmouse.mjs config <host>` generates a local configuration fragment. Install `skills/` using that host's Agent Skills mechanism. See [adapter details and references](adapters/README.zh.md). Avoid keeping a standalone MCP registration alongside the native plugin's MCP service.

The repository now includes marketplace catalogs for direct native plugin distribution. When a Git-backed plugin is installed from source without binaries, its first controller call builds the native files locally using Windows' compiler; build messages go to stderr, preserving MCP stdio framing.

| MCP tool | Purpose |
|---|---|
| `pointer_open` | Open the controller; a new instance starts paused |
| `pointer_status` | Inspect backend, pointer coordinates and limitations |
| `pointer_list_windows` | List visible windows |
| `pointer_observe` | Screenshot the desktop; optional window crop |
| `pointer_move` | Move only the Agent overlay |
| `pointer_click` | Left/right background click at screen coordinates |
| `pointer_scroll` | Scroll at screen coordinates |
| `pointer_drag` | Experimental touch gesture; may affect real cursor/focus |
| `pointer_pause` | Disable input and hide the Agent pointer |

There is **no target-window binding**. One enable covers the current desktop. `windowId` only crops a screenshot. Coordinates are physical desktop pixels, including negative monitor origins; images return scaling metadata. Click, scroll and drag need a fresh, single-use `observationId` valid for 15 seconds.

AgentMouse waits while you use the keyboard or mouse and cancels conflicting input where detected. This reduces interference; it does not provide simultaneous independent keyboard/focus sessions. `inputDelivered` confirms submission, not success: agents must observe again to verify the effect. Independent native hover, background drag and keyboard input are not implemented.

Stop with the controller button, its displayed global pause shortcut (normally **Ctrl+Alt+P**), `pointer_pause`, or:

```powershell
node agentmouse.mjs stop
```

Pause hides the overlay; closing the controller exits it. Skills instruct agents to pause at task end. Graceful MCP disconnect attempts cleanup after submitting input; forced process termination does not guarantee cleanup.

## Build and test

```powershell
npm ci --ignore-scripts
npm run build
npm test
npm run test:state
```

Checks cover MCP validation, actual stdio transport with the official SDK, adapter relocation, five installer layouts in temporary homes, settings preservation, backups, repeat installation, dry run, rollback, and native state/coordinate independence. They do not start GUI actions or audio playback. CI runs these checks on Windows with Node 18 and 24.

`node scripts/test-clients.mjs` separately checks **actually installed Codex and ZCode clients**, their cached plugin payloads, MCP manifests, all nine tools and a real read-only `pointer_status` round-trip. It requires both native clients and installed AgentMouse plugins; normal CI does not pretend these clients are present. The local 0.5.1 check passed with Codex CLI 0.153.4 and ZCode CLI 0.17.1. This is not a visual plugin-browser or desktop-action acceptance test.

See [the validation guide](docs/compatibility.md) for optional interactive tests. `powershell -File scripts/build-release.ps1` builds and tests a complete Windows ZIP with native binaries, bundled installer dependencies and a SHA-256 file. `powershell -File scripts/test-release.ps1` checks the extracted ZIP installation; `scripts/test-source.ps1` checks first-time source installation with dependency download and compilation. Both use temporary homes. Version tags trigger the GitHub release workflow. Private profiles, local reports and screenshots are excluded from Git and releases.

## License

[Apache-2.0](LICENSE). This project is not affiliated with the supported agent products.
