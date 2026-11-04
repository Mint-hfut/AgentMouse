UNRELEASED 0.7.0-beta.4 — incident containment, not a WeChat hang root-cause fix.

WeChat became unresponsive during a real Full session and required the user to restart the app. Keep the currently installed plugin disabled. Do not retry against live WeChat or use arbitrary scripts as a fallback.

Source containment: quarantine known WeChat process families before native input/capture and either UIA provider path; default Snapshot to image-only; expose provider failure state; remove Full's arbitrary PowerShell tool (26 tool names). Core remains 11 tools. Tests use pure policy inputs and owned fixtures only.

The trace confirms physical-input/foreground fallback through Full PowerShell, but does not establish which operation caused the persistent app state. Target-side hung-thread/capture evidence was not collected before restart. No root-cause resolution or live WeChat compatibility is claimed, and this build has not been enabled or distributed to the user's clients.
