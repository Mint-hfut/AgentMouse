---
name: agentmouse-stop
description: Stop AgentMouse input and hide its Agent pointer when the user requests the plugin's stop/pause command.
---

Call this plugin's `pointer_pause`, then check status if available. A paused service must report `armed=false` and `pointer.visible=false`. Treat an already closed controller as stopped. Do not restart the controller or continue the interrupted task. Pausing and fully exiting are distinct; the controller window can remain available for later use.
