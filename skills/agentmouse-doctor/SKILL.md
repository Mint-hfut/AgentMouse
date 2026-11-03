---
name: agentmouse-doctor
description: Diagnose AgentMouse plugin installation or connection problems without starting GUI operations.
---

Use the bundled `agentmouse.mjs doctor` command if the bundle path is available, and inspect the host's MCP tool registration. Check Windows x64, Node.js 18+, the bundled native files, and the presence of the pointer tools. Use `pointer_status` only as a read-only connection check. Do not start GUI/playback tests or open apps as part of diagnostics. Distinguish host registration, model image support, controller availability and actual app compatibility; one successful check does not prove all four.
