AgentMouse 0.6.0-beta.1 — real background images and browser mouse gestures.

- Browser Bridge uses your existing Chrome/profile, actual rendered images and coordinate input through `chrome.debugger`. No DOM selectors, injected clicks, remote debugging port or disabled GPU is needed in normal use.
- Supports click/right-click, wheel, double-click, long press, slider/puzzle paths, HTML5 drop and cancellation/release. Real extension integration passes on local fixtures in headless Chrome for Testing 157.
- Windows Graphics Capture reads covered native window surfaces. Native Chromium mouse input is blocked when inactive because it can activate/raise the window.
- Agent installers prepare a paired extension directory. You load it manually once and review Chrome permissions; no policy bypass or profile change.
- Preserves native Codex/ZCode plugin installation and other MCP + skills adapters, with backups and isolated installation tests.

**Beta limit:** the interactive desktop became unavailable, so final acceptance with the user's foreground window has not been completed. Headless fixture success is not a desktop pass or a promise that real CAPTCHA risk checks accept automation. Test results and failed native approaches are documented.

Extract the ZIP, double-click `install.cmd`, select agents, then load the printed generated Chrome extension directory. Refresh/restart agent clients, open AgentMouse and enable input once before actions. Requires Windows x64 and Node 18+; native HWND capture requires Windows 10 1903+.

中文：新版加入真实后台画面与浏览器鼠标通道。真实扩展链路已通过本地滑块、拼图式路径拖动、顺序点选、长按、双击、HTML5 拖放和中断释放测试。仍需用户加载扩展一次；不修改原 Chrome 资料或绕过策略。桌面恢复后的前台保持验收尚未完成，不能把组件通过宣传为所有真实验证码必过。
