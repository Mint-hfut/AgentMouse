---
name: agentmouse-start
description: Start the AgentMouse controller when the user requests this plugin's start/open command. Does not start arbitrary apps or enable input automatically.
---

Call the plugin's `pointer_open`, then `pointer_status`. Explain whether the controller is running and paused. A new controller requires the user to enable whole-desktop input once; do not claim it is already enabled. Do not begin unrelated desktop work.
