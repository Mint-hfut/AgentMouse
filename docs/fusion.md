# Windows-MCP + AgentMouse fusion

融合模式将 Windows-MCP 的系统工具与控件识别，接到 AgentMouse 的后台鼠标、窗口采集和浏览器通道。Agent 连接一个 MCP 服务即可使用。原 Windows-MCP 目录没有被修改。

## 安装

需要 Windows x64、Node 18+ 和 [uv](https://docs.astral.sh/uv/getting-started/installation/)。安装器自动准备 Python 3.14 和锁定的 Python 依赖，保留 Windows-MCP 的 MIT 许可证。首次安装需要联网。

```powershell
.\install.cmd --engine fusion --agent codex,zcode
.\install.cmd --engine fusion --agent all
```

交互安装默认推荐融合模式；`--engine native` 保留原 11 工具的轻量模式，不需要 Python/uv。没有指定 `--engine` 的非交互命令维持原生模式，以兼容已有脚本。刷新或重启所选 Agent，融合模式应看到 **27 个工具**。

Python 环境在 `~/.agentmouse/fusion/windows-mcp/.venv`；所选 Agent 的插件/MCP 入口改为 `fusion-entry.mjs`。控制器仍需由用户启用输入，浏览器仍需加载生成的 Browser Bridge 扩展。不会自动启动、登录或把业务应用置前。

## 能力范围

| 部分 | 融合后的行为 |
|---|---|
| Snapshot / Screenshot | AgentMouse 采集真实目标画面；原生窗口叠加 Windows-MCP 的目标控件信息 |
| Click / Move / Scroll | 全部经 AgentMouse 执行；保留目标核对、单次观察编号、旧帧拦截、取消释放 |
| FileSystem / Process / Registry / PowerShell | 使用 Windows-MCP 原实现 |
| DisplayInventory / Clipboard / Notification | 使用 Windows-MCP 原实现；剪贴板和系统操作仍是共享资源 |
| Scrape | 保留 HTTP 抓取；不使用会混入当前前台页面的全局 DOM 抓取 |
| App | 查询程序和窗口；自动启动、切换前台、调整窗口的模式未开放 |
| Wait / WaitFor | 等待时间，或只读等待指定原生窗口的控件出现/可用 |
| Type / Shortcut / MultiSelect / MultiEdit | 明确返回不支持；独立键盘与共享 Ctrl 尚未解决，不会回退物理输入 |
| pointer_* | 保留 open、status、pause、list_apps、list_windows、inspect_window 六个工具 |

鼠标支持左右键、双击、长按、显式起点和路径拖动、浏览器悬停及纵向滚轮。中键、横向滚轮、原生持久悬停、跨窗口系统拖放未开放；被遮挡原生窗口需通过画面中的滚动条操作。

## 目标与坐标

先用 `App(mode="find", query="微信")` 检查进程，或 `App(mode="list")` 列出窗口。隐藏/最小化不代表没运行。选择 HWND 或 `browser:` 标签页后：

```json
{"name":"Snapshot","arguments":{"window_id":"123456"}}
```

Snapshot 返回图片、`observationId`、窗口身份、坐标空间和 `controls.elements`。点击必须使用同一窗口和本次编号：

```json
{"name":"Click","arguments":{"window_id":"123456","observation_id":"返回的 observationId","label":0}}
```

也可用 `loc=[x,y]`，但不能同时提供 label。原生坐标是物理桌面像素；浏览器是视口 CSS 像素。标签是本次观察的局部编号，不能从全局 UI 树或其他窗口拿来用。浏览器通道使用像素，不混用原生控件坐标。

拖动通过 `Move(drag=true, from_loc=[x,y], loc=[endX,endY], duration=0.65, ...)` 执行；可提供 `path=[[x1,y1],[x2,y2]]`。真实鼠标不会成为隐式起点。每次动作后重新观察、确认业务结果。

控件采集在独立进程执行，超时停止该进程并注明控件不可用，不会换成前台窗口。控件和截图按顺序采集，PID/窗口几何变化时丢弃标签；它们不是应用内容的原子快照，仍需看图核对。自绘应用可能提供很少的控件。

## 实测

- 官方 MCP SDK：27 工具、强制目标/观察编号、上游文件/显示器工具、进程发现，以及拒绝共享键盘/批量输入。
- 自建真实 Windows 窗口：Windows-MCP 标签 → AgentMouse WGC 画面 → 遮挡窗口输入 → 新控件状态/PNG；错误目标和旧编号被拒绝，前台及物理鼠标保持不变。
- 真实 Chrome 扩展与融合 MCP：Chrome 完全被前台 ChatGPT 遮挡，十项鼠标/滑块/拖动/点选/HTML5 拖放/中断释放检查通过。[结果](test-results/fusion-desktop-2026-11-04.json)。仅强制 sRGB 稳定颜色断言，未关闭 GPU 或遮挡机制。
- 同一路径也有无界面组件测试，加入 GitHub CI 和发布检查。

```powershell
node scripts/fusion-setup.mjs
npm run test:fusion
npm run test:fusion:windows
$env:AGENTMOUSE_CHROME = .\scripts\get-test-chrome.ps1
node scripts/test-background-chrome.mjs --headless-test --fusion-test
```

原生验收使用隔离的自建程序，不操作微信账户。微信 → QQ 邮箱和第三方真实验证码服务仍未验收通过。系统工具可修改真实文件、进程和设置；鼠标解耦不表示所有系统命令都不会影响桌面。

## English

One MCP server exposes 27 tools: Windows-MCP system services and target-scoped UIAutomation, plus AgentMouse-only background capture/input. Install with `install.cmd --engine fusion --agent codex,zcode`; uv is required and Python/dependencies are prepared automatically. Interactive installation recommends fusion; existing unattended commands default to native.

Mouse actions require the exact `window_id` and fresh `observation_id`; labels belong to one target observation. Physical keyboard, foreground App launching/switching, Ctrl batches, middle click, horizontal wheel, native hover and global foreground DOM scraping are not enabled. There is no physical-input fallback. System tools and clipboard remain shared Windows operations.

SDK, native label/capture/input integration, and ten covered-browser desktop cases passed. These are fixture tests, not universal app or CAPTCHA acceptance. [Third-party provenance/license](../THIRD_PARTY_NOTICES.md).
