AgentMouse 0.7.0-beta.3 — two independent editions, bundled Python in Full.

Download one:
- AgentMouse-Core-0.7.0-beta.3-windows-x64.zip: our standalone C#/Node background pointer, capture, target guards, MCP/plugins/skills. 11 tools. No Python, Windows-MCP source or fusion adapter.
- AgentMouse-Full-0.7.0-beta.3-windows-x64.zip: Core + Windows-MCP 0.8.8 + fusion adapter, with an isolated relocatable Python runtime and all locked dependencies. 27 tool names. No separate Python or uv installation/download on the user's machine.

Both require Windows x64 and Node 18+. Each package defaults to its own engine. Extract, run install.cmd and choose agents; refresh/restart clients afterward. They update the same AgentMouse plugin rather than starting two competing mouse services.

Core builds and runs independently; Full's adapter calls the same Core engine for every mouse operation. Windows-MCP contributes target-scoped UIAutomation and retained system tools. MIT/CPython/dependency notices are preserved. Upstream sources are marked vendored in repository language statistics.

Validation covers Core package isolation and native plugin installation, Full install without Python/uv on PATH in a Chinese home after the source payload is moved, 27-tool SDK connectivity, real Windows label/WGC/background-input/result checks, and ten covered-Chrome desktop cases with no detected foreground/z-order/physical-pointer interference. Separate headless checks run in CI. Portable-runtime checks ignore host Python environment variables.

Independent keyboard/shortcuts, Ctrl batches, foreground App activation and several native gesture variants remain unavailable; no physical-input fallback is used. System/clipboard tools affect shared Windows resources. WeChat → QQ Mail and third-party CAPTCHA acceptance remain unverified.

中文：两版独立下载。Core 展示我们的后台鼠标、截图、保护机制、MCP/插件/技能，不含 Python 或 Windows-MCP。Full 在 Core 上融合 Windows-MCP 的系统工具和控件树，自带 Python 与全部依赖，用户不需要另装 Python/uv。安装器识别包类型，默认加载相应引擎。代码按核心、融合适配、上游、运行时打包分层；真实窗口和浏览器链路均有实测，不能宣传所有软件或验证码必过。
