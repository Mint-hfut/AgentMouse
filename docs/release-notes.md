AgentMouse 0.7.0-beta.1 — Windows-MCP + AgentMouse fusion.

- One 27-tool MCP server combines Windows-MCP 0.8.8 system services/UIAutomation and AgentMouse-only background images/mouse execution. The supplied Windows-MCP source directory is unchanged; its bundled source retains MIT notices separately from AgentMouse's Apache-2.0 license.
- Snapshot captures one HWND/browser target and returns scoped control labels, real PNG pixels, coordinateSpace and an observation token. Click/Move/Scroll must address that exact window/token; stale/wrong-target input is rejected. Drag has an explicit start and cannot inherit the real cursor position.
- Windows-MCP FileSystem, Process, Registry, PowerShell, HTTP Scrape, DisplayInventory, Clipboard and Notification are retained. Its physical mouse hooks/overlay are not started. Keyboard shortcuts/typing, Ctrl batches, foreground App launch/switch/resize, foreground DOM scraping, middle click and horizontal wheel are unavailable in background mode, with no physical-input fallback.
- Interactive install recommends fusion. Unattended: install.cmd --engine fusion --agent codex,zcode (or all). Requires uv; installs Python 3.14 and locked dependencies. Existing unattended commands without --engine keep native-only mode.
- Official SDK/27-tool tests, all five host adapter configurations, and real Windows control-label/WGC/input/result integration passed. The complete real-extension fusion path also passed ten desktop cases with Chrome fully covered by the foreground ChatGPT window, with no detected focus, relative-z-order or physical-pointer interference. Headless integration is checked separately in CI.

Refresh/restart selected clients after installing. Enable input in the local controller; load/reload the generated Browser Bridge extension for browser tasks. Clipboard/system operations remain shared Windows operations. Real WeChat → QQ Mail and third-party CAPTCHA acceptance are not verified.

中文：融合 Windows-MCP 的系统工具和控件树，鼠标与真实后台画面统一走 AgentMouse，提供 27 个工具。目标窗口、局部控件标签、观察编号绑定；明确拒绝不支持的共享键盘和前台输入。Windows 与 Chrome 融合链路实测通过；不代表所有应用、微信邮箱或真实验证码都保证可用。运行 install.cmd --engine fusion --agent codex,zcode，升级后刷新智能体会话。
