---
name: agentmouse-start
description: Start the AgentMouse controller, with an explicit input-enable option for a user-authorized task. Does not start arbitrary apps.
---

Call the plugin's `pointer_open`, then `pointer_status`. Default to paused. If the user explicitly authorized input for this task, pass Full `allow_input=true` or Core `allowInput=true` without another confirmation. Report the actual returned state. Do not begin unrelated desktop work.
