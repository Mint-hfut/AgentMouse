UNRELEASED 0.7.0-beta.4 — capture isolation and audit fixes; live WeChat workflow failed.

New audit fixes: one capture process per screenshot with bounded timeout/cancellation and resource disposal before response; image-hash post-action guards; provider-failure label rejection; worker write/disconnect cleanup; confirmed extension release tracking; explicit input enable for an already-authorized task; Core/Full enable-EOF race cleanup; hidden-window background show and read-only input-state metadata. See [audit](audits/2026-11-04.md) for checks and remaining limits.

The user explicitly requested a live native-only pilot after restarting WeChat. Screenshots returned and the cursor stayed still, but the click raised WeChat and did not open QQ Mail reminders. The controller paused; no email was read. Normal quarantine and old-plugin disablement remain. This build has not been installed into the user's agents or released as a ZIP.

WeChat became unresponsive during an earlier Full session and required the user to restart the app. Keep the old installed plugin disabled; the later explicitly authorized repaired-source pilot above also failed compatibility. Do not use arbitrary scripts as a fallback.

Source containment: quarantine known WeChat process families, with an explicit PID/creation-time native-only diagnostic grant for a user-requested repaired-source pilot. UIA remains quarantined. Default Snapshot is image-only; Full has no arbitrary PowerShell tool (26 tool names). Core remains 11 tools. Ordinary regressions use owned fixtures; the separate failed live pilot is identified above.

Native fixes: skip transparent child render layers in covered-window input, and queue cancel/UP/cancel after uncertain message delivery. Old-backend regressions fail for a real Qt button business event and missing UP after delayed DOWN; both pass with the fixes. Delayed UP, pause and graceful controller shutdown also pass with actual target capture release. No repeated DOWN is sent. Queueing cleanup never counts as confirmed target execution.

Reproduce on an interactive Windows desktop: `npm run test:routing`, `npm run test:native:failures`, `npm run test:fusion:windows`. `npm run test:fusion:qt` additionally uses a separate test interpreter with PyQt5==5.15.11; see [compatibility](compatibility.md). Qt is a development fixture dependency only and is not shipped in Core or Full.

The earlier trace confirms physical-input/foreground fallback through Full PowerShell, but does not establish which operation caused the persistent app state. Target-side evidence was not collected before that restart. No persistent-hang resolution or live WeChat compatibility is claimed. The repaired source was used only for the explicit pilot, not installed into the user's agents or released as a ZIP.
