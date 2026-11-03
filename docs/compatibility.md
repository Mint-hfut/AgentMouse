# Compatibility and validation

AgentMouse is an experimental Windows x64 controller, not a universal independent HID mouse. Overlay independence and application input acceptance are separate questions.

| Capability | Current behavior |
|---|---|
| Agent pointer | Own coordinates; real mouse movement does not update them |
| Default click/scroll | Window messages, no system-cursor movement or foreground-activation request |
| Global routing | Visible window underneath coordinates; no target binding |
| Background windows | Can work when visible but not focused; occluded/minimized content unsupported |
| Hover / mouse capture / native mouse drag | Not implemented as an independent system mouse |
| Keyboard/text entry | No MCP keyboard tool or independent keyboard focus |
| Touch mode | Experimental; may change real cursor and foreground focus |
| Elevated apps, secure desktop, games, raw-input apps | Not guaranteed; Windows integrity restrictions still apply |
| Multiple controlling agents | One session at a time; no multi-session ownership |
| Human input | Idle waiting and conflict detection; not complete concurrency isolation |

Applications can ignore messages or activate themselves. Input submission does not prove its UI effect. AgentMouse reports observed cursor/focus changes and requires fresh observations. It has no SendInput/SetCursorPos fallback and does not modify system security policy.

## Automated checks

`npm run build`, `npm test`, and `npm run test:state` do not start GUI interaction. They verify:

- MCP parameters, initialization, errors, image results, actual stdio framing and official SDK interoperability.
- Pause on graceful disconnect after input; read-only probes do not pause another controller; late input/open after disconnect is rejected.
- Host adapters and relocation with spaces and Unicode paths.
- Five installer layouts in temporary homes, preservation of other settings and opaque YAML `!!js` expressions, TOML markers inside multiline strings, backups, idempotence, dry run, rollback and concurrent modification detection.
- Native pointer visibility, independent coordinates, input tracking, x64 layouts, malformed requests and paused-input rejection.

These are **configuration/protocol checks, not five-client GUI acceptance tests**.

## Earlier prototype observations

The 0.3/0.4 prototype was checked on one Windows machine. Selected Chrome/Edge click, right-click and scroll actions, plus Notepad/Word caret and scroll actions succeeded using background input without observed cursor or foreground change (10 selected checks). A single enabled session also routed Chrome to Notepad through the official MCP SDK.

DSH loaded skills and made actual MCP calls; a later read-only load also worked. The music task was stopped by the user because no sound card was available, so audio playback was **not verified**. Touch showed negative cursor/focus effects and incomplete Word behavior. These are historical observations, not a new 0.5 end-to-end pass or a guarantee for all application versions.

Claude Code native loading was blocked by local administrator policy and was not bypassed. Codex, ZCode and WorkBuddy native client loading have not been fully accepted on this machine. Their installer layouts are tested separately.

## Optional interactive tests

Run only when you intend to open test windows and inject input into a disposable desktop session:

```powershell
.\build.ps1
New-Item -ItemType Directory -Force compatibility-results
.\dist\AgentMouse.Cli.exe --self-test .\compatibility-results\self-test.json
```

This opens practice windows and includes touch experiments that can affect real cursor/focus. `CompatibilityTest.cs` and `browser-fixture.html` supply additional app fixtures; inspect their command help and use temporary documents. UI Automation and Word COM in that harness are validators, not the MCP input backend.

For a manual check, open the controller and its practice window, enable desktop input and use its built-in demo. Watch the click count, foreground app and physical pointer before/after. Confirm pause hides the arrow and close test windows afterward. Include Windows/app versions, backend, result and cursor/focus effects in issues; avoid private screenshots and credentials.
