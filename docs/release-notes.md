UNRELEASED 0.7.0-beta.4 — incident containment and reproduced native input fixes; live WeChat recovery unverified.

WeChat became unresponsive during a real Full session and required the user to restart the app. Keep the currently installed plugin disabled. Do not retry against live WeChat or use arbitrary scripts as a fallback.

Source containment: quarantine known WeChat process families before native input/capture and either UIA provider path; default Snapshot to image-only; expose provider failure state; remove Full's arbitrary PowerShell tool (26 tool names). Core remains 11 tools. Tests use pure policy inputs and owned fixtures only.

Native fixes: skip transparent child render layers in covered-window input, and queue cancel/UP/cancel after uncertain message delivery. Old-backend regressions fail for a real Qt button business event and missing UP after delayed DOWN; both pass with the fixes. Delayed UP, pause and graceful controller shutdown also pass with actual target capture release. No repeated DOWN is sent. Queueing cleanup never counts as confirmed target execution.

Reproduce on an interactive Windows desktop: `npm run test:routing`, `npm run test:native:failures`, `npm run test:fusion:windows`. `npm run test:fusion:qt` additionally uses a separate test interpreter with PyQt5==5.15.11; see [compatibility](compatibility.md). Qt is a development fixture dependency only and is not shipped in Core or Full.

The trace confirms physical-input/foreground fallback through Full PowerShell, but does not establish which operation caused the persistent app state. Target-side hung-thread/capture evidence was not collected before restart. No root-cause resolution or live WeChat compatibility is claimed, and this build has not been enabled or distributed to the user's clients.
