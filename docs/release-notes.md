AgentMouse 0.6.0-beta.3 — app discovery, window layers and frozen-frame input protection.

- Adds pointer_list_apps: running processes including WeChat/Weixin/微信 aliases, even when the app is in the tray or has no visible window. A missing visible window does not prove the app is closed.
- Window inventory includes hidden/minimized states, PID, class, owner, bounds, foreground, front-to-back order and rectangular overlaps above the target.
- Adds read-only pointer_inspect_window for accessible control names/roles/bounds. Uses built-in .NET UIAutomation in an isolated CLI process; no Python dependency, foreground input or copied upstream code.
- Every click/drag/scroll declares its intended window ID. Mismatches are rejected. An observation with a window ID defaults to its real background surface, never a foreground crop.
- Repeated/older native frames after input block subsequent input with inputAllowed=false / unverified_frame. Skills stop repeated clicks/relaunches when a target has no fresh frame.
- Real owned-window regression verifies overlapping layers, controls, correct native-message recipient, wrong-target and frozen-frame rejection, and hidden/minimized discovery. Message receipt is tested separately from application business success.

The reported WeChat trace used beta.2. Its logged input recipients were WeChat windows, not ZCode; the actual failure was unverified repeated input against a frozen capture, plus missing process discovery. WeChat → QQ Mail has not been verified working. Native message receipt does not guarantee a custom app responds; a covered WinForms button also received messages without raising its business Click event.

The historical beta.2 ten-case covered Chrome desktop report remains available. Browser Bridge still needs the generated extension loaded manually. A new full desktop rerun interrupted by foreground changes is not called a new pass.

Extract the ZIP and run install.cmd. Refresh/restart selected agent clients so they load eleven tools and the changed action schema. Close/reopen an older AgentMouse controller before input. If an older browser daemon is still running, restart Windows and reload the generated extension. Requires Windows x64 and Node 18+; native HWND capture requires Windows 10 1903+.

中文：新增已运行程序查询、隐藏/最小化窗口与层级信息、只读控件树，以及操作后旧帧拦截。点击必须带明确目标窗口，目标不符拒绝执行。专项真实窗口测试通过，但不代表微信后台打开 QQ 邮箱已经成功；收到输入与业务成功必须分开验证。升级后刷新 Agent，加载 11 个工具和新参数，关闭旧控制器再打开新版。
