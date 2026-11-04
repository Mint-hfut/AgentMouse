# WeChat became unresponsive during automation

Status: two input defects reproduced and fixed in source; the persistent WeChat hang's root cause remains unconfirmed. No live WeChat compatibility pass claimed. The user recovered by restarting WeChat; stopping AgentMouse alone had not restored interaction.

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

### Confirmed input defects and regression checks

1. Covered-window hit testing used `ChildWindowFromPointEx` flags `3`, omitting `CWP_SKIPTRANSPARENT`. A read-only Win32 inventory of the recovered app found `MMUIRenderSubWindowHW` with `WS_EX_LAYERED | WS_EX_TRANSPARENT` (`0x80020`), matching the trace's receiver class. Native input now uses flags `7` for both covered and desktop routes. A transparent-child routing fixture fails against the old code and passes with the fix.
2. A timed-out DOWN handler can still complete later. The old finally block attempted another synchronous UP and, on failure, posted only CANCELMODE. An owned delayed-DOWN fixture proved that the target could process no UP. The fix queues CANCELMODE, UP, CANCELMODE after any uncertain mouse-message delivery instead of synchronously sending UP into an unfinished handler. A failed synchronous UP uses the same cleanup. Queue success is checked, but is never reported as confirmed execution; the controller stays paused and refuses further input.

The real Full MCP regression uses an owned Qt 5.15.2 window, an ordinary `QPushButton.clicked` business counter, and a layered/transparent native render child. With the old backend the counter remains zero; with the fix it becomes exactly one and the WGC image changes, while the foreground window and physical cursor remain unchanged. UIA is disabled for this test. This proves a business event for this Qt fixture, not compatibility with WeChat's different Qt 5.15.14/custom renderer.

Delayed DOWN, delayed UP, pause during a hold and controller shutdown during a hold also pass against owned windows. Tests check actual target capture release and no repeated DOWN. Controller shutdown means graceful transport disposal, not forcible process termination. [Sanitized results and commands](../test-results/native-input-2026-11-04.json).

Still required: isolate target-side UIA/provider behavior and validate the actual WeChat task without recurrence. The previous incident has no target-side dump, so these reproduced input defects do not establish which operation caused its persistent state. Keep real WeChat quarantined and the user's installed plugin disabled; do not publish/install this source patch as a verified WeChat fix. Queued cleanup cannot recover a permanently blocked target thread, and killing all sender processes is not a guaranteed cleanup path.

Do not publish the private trace, screenshots, temporary script or account data as test artifacts.
