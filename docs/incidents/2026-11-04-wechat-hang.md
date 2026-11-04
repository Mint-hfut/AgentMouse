# WeChat became unresponsive during automation

Status: contained, root cause unconfirmed. No compatibility pass claimed. The user recovered by restarting WeChat; stopping AgentMouse alone had not restored interaction.

The installed ZCode AgentMouse plugin was disabled. Its controller was paused and closed, and related Fusion/worker/browser processes were stopped. No further live WeChat UIAutomation, capture or input is authorized by this investigation phase. An independent Codex chat reviewed code and the specific trace read-only, without modifying files or contacting the live app.

## Observed evidence

- Native input results reported successful delivery and release to WeChat's custom renderer. They do not prove business success or rule out later target-side failure.
- Some subsequent native frames did not advance and input was marked disallowed. Other frames advanced after foreground changes.
- The agent used Full's unrestricted PowerShell tool to restore/raise the target and later execute a temporary script using SetCursorPos and global mouse_event. This bypassed the target/observation/pause path. The temporary script had no finally around button down/up, but returned success; this trace does not prove an interrupted down/up.
- Controls were repeatedly empty. The previous adapter omitted upstream TreeState.status, so empty results did not reliably distinguish no controls from provider failure.
- No crash dump, hung-thread stack or capture-state snapshot was obtained before the user restarted the app. UIA, native messages, WGC and foreground fallback remain separate suspects. None is established as the sole cause.

## Unreleased containment

- Remove arbitrary PowerShell from the Full catalog; do not attempt a keyword denylist for arbitrary scripts.
- Quarantine known WeChat process names before native input/capture and either UIA implementation. Discovery remains read-only and available. Full also checks before optional provider work and image capture.
- Default Snapshot to image-only; UIA requires explicit opt-in. Expose providerSucceeded/error.
- Keep the installed plugin disabled. Do not auto-reenable or roll out a test build against live WeChat.

These measures prevent known unsafe paths; they are not a fix for an established WeChat deadlock. Generic WinForms/browser fixture passes cannot establish that compatibility.

## Remaining investigation

Use only owned fixtures to separate blocking UIA providers, delayed DOWN handlers, delayed UP handlers, and pause/exit. Check message order and target capture after the sender exits. SendMessageTimeout does not undo already-entered target code. Existing finally-UP and cancellation handling need an explicit failure-path study before making stronger release claims.

Do not publish the private trace, screenshots, temporary script or account data as test artifacts.
