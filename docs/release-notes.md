AgentMouse 0.6.0-beta.2 — verified covered-window browser operation.

- Browser Bridge uses your existing Chrome/profile, actual rendered images and coordinate input through `chrome.debugger`. No DOM selectors, injected clicks, remote debugging port or disabled GPU is needed in normal use.
- All ten local desktop checks pass with Chrome fully covered by foreground ZCode: fresh page images, click/right-click, wheel, double-click, long press, slider/puzzle paths, ordered points, HTML5 drop and cancellation/release. No foreground, relative z-order or unattributed system-pointer interference was detected.
- Fixes occluded screenshot timeouts by keeping the attached page rendering, and preserves drag corners/samples when native permission checks are slow. GPU acceleration stays enabled.
- Windows Graphics Capture reads covered native window surfaces. Native Chromium mouse input is blocked when inactive because it can activate/raise the window.
- Agent installers prepare a paired extension directory. You load it manually once and review Chrome permissions; no policy bypass or profile change.
- Preserves native Codex/ZCode plugin installation and other MCP + skills adapters, with backups and isolated installation tests.

**Beta limit:** desktop acceptance covers one Windows environment and a local fixture in Chrome for Testing 157, with sRGB forced only for fixture color checks. It is not a promise that all apps or real CAPTCHA risk checks accept automation. Simulated page focus/visibility can change page behavior; Windows focus remains separate. Test results and failed native approaches are documented.

When upgrading from beta.1, restart Windows if its older browser service is still running, then reload the generated extension. Version checks reject an older running service instead of silently using it.

Extract the ZIP, double-click `install.cmd`, select agents, then load the printed generated Chrome extension directory. Refresh/restart agent clients, open AgentMouse and enable input once before actions. Requires Windows x64 and Node 18+; native HWND capture requires Windows 10 1903+.

中文：本版已补齐真实桌面验收：Chrome 完全被前台 ZCode 遮挡时，10 项本地操作及新画面检查全部通过，未检测到抢前台或带动真实鼠标。修复遮挡时截图超时、慢速状态检查跳过拖动转折点的问题。仍需用户加载扩展一次；不能宣传为所有软件或真实验证码必过。beta.1 的旧服务若仍在运行，重启 Windows 后重新加载生成的扩展。
