# AgentMouse

**让 AI Agent 在后台操作电脑，Agent 指针与用户鼠标解耦，不和你抢鼠标。**

[English](README.md) · [兼容性与实测记录](docs/compatibility.md) · [下载](https://github.com/Mint-hfut/AgentMouse/releases)

项目包含 Windows 控制程序、MCP 服务、Agent Skills 和安装脚本，接入 **Claude Code、Codex、ZCode、WorkBuddy、DSH**。无需虚拟机或分屏。

网页任务使用 **Browser Bridge**：取得真实标签页画面，按图像坐标发送鼠标按下、移动、释放；沿用当前 Chrome 和登录状态，不依赖网页 DOM 选择器、脚本点击或逐网站接口。桌面软件另有通用 Windows 采集和窗口消息通道。

**当前为测试版。** 融合服务已通过真实 Windows 控件到后台点击，以及 Chrome 完全被前台窗口遮挡时的十项鼠标检查。[融合桌面记录](docs/test-results/fusion-desktop-2026-11-04.json)。这不代表所有应用或真实验证码均兼容。

## 两个独立版本

[Core ZIP](https://github.com/Mint-hfut/AgentMouse/releases/download/v0.7.0-beta.3/AgentMouse-Core-0.7.0-beta.3-windows-x64.zip) · [Full ZIP](https://github.com/Mint-hfut/AgentMouse/releases/download/v0.7.0-beta.3/AgentMouse-Full-0.7.0-beta.3-windows-x64.zip)

| 版本 | 内容 | Python / Windows-MCP | MCP |
|---|---|---|---|
| **AgentMouse Core** | 我们的独立鼠标、截图、窗口保护、MCP/插件/技能 | 不包含，也不需要 | 11 工具 |
| **AgentMouse Full** | Core + Windows-MCP 系统工具/控件树 + 融合适配层 | 自带 Python 和依赖，无需单独安装 | 27 工具名 |

Core 可单独运行和构建，Full 通过适配层复用 Core。我们的实现与上游贡献见[模块设计](docs/architecture.md)。Full 中独立键盘/批量输入仍未开放，相关接口明确返回不支持。

## Windows-MCP 融合模式

新增融合版：Windows-MCP 的系统工具、目标控件树 + AgentMouse 后台截图与鼠标，统一提供 **27 个工具**。真实 Windows 控件标签到后台点击，以及被前台 ChatGPT 完全遮挡的 Chrome 十项检查均已通过。[融合能力、限制和用法](docs/fusion.md)。

## 一键安装到智能体

需要 Windows x64、**Node.js 18+ x64（含 npm）** 和 .NET Framework。原生被遮挡窗口采集要求 Windows 10 1903+。

下载 [Windows ZIP](https://github.com/Mint-hfut/AgentMouse/releases)，解压后双击 **`install.cmd`**，选择 Core 或 Full 下载包；各自默认加载对应引擎，安装时只需选择智能体。两版均不要求用户安装 Python 或 uv。源码方式：

```powershell
git clone https://github.com/Mint-hfut/AgentMouse.git
cd AgentMouse
.\install.cmd
```

Core 源码安装会下载锁定的 Node 依赖、禁用其生命周期脚本，并使用 Windows 自带的 .NET Framework 编译器构建程序，不要求 Visual Studio 或 .NET SDK。从源码开发 Full 时需要 uv，发布的 Full 已包含 Python。Codex 原生插件安装需要 Codex CLI；ZCode 自动查找标准目录或 PATH 中的客户端插件 CLI。

```powershell
.\install.cmd --engine fusion --agent codex,zcode
.\install.cmd --agent all
.\install.cmd --agent dsh --profile web
.\install.cmd --agent codex --mode mcp
.\install.cmd --agent codex --browser none
```

Codex、ZCode 默认安装**原生插件**，安装器查询其已安装列表确认启用；另外三种目前增加 **MCP＋技能**，不会因此出现插件卡片。`--mode mcp` 明确选择仅 MCP，`--browser none` 跳过浏览器文件准备。

安装后刷新或重启智能体，原生插件名称是 **`agentmouse@agentmouse`**。

### 连接你当前的 Chrome

安装器会准备配对的扩展目录并打印路径，通常为 `~/.agentmouse/browser/extension`。**不会自动修改 Chrome 设置、用户资料或企业策略。**

1. 你在 Chrome 中打开 `chrome://extensions`，自行开启开发者模式，选择“加载已解压的扩展程序”。
2. 选安装器打印的**生成目录**，不要选仓库里尚未配对的源码目录。
3. 查看并授予调试权限，用于读取页面画面和执行鼠标手势。正常使用无需开放调试端口、换浏览器资料目录或关闭 GPU。
4. 让 Agent 打开 AgentMouse，在本地控制器点击一次“允许全桌面操作”；网页任务选择窗口列表里的 **`browser:`** 标签页。

若企业策略阻止加载，应联系管理员，软件不会绕过策略。也可单独运行：

```powershell
node agentmouse.mjs browser-setup
node agentmouse.mjs browser-status
node agentmouse.mjs browser-stop
```

控制器目前为中文界面；Chrome 可能显示标准调试提示。当前一次只使用一个配对的浏览器资料和一个控制 Agent。

## 鼠标能力

融合模式的工具见上方链接；以下为原生模式 11 工具。

| MCP 工具 | 功能 |
|---|---|
| `pointer_open` | 打开本地控制器，新实例默认暂停 |
| `pointer_status` | 查看控制器能力、配对和浏览器连接状态 |
| `pointer_list_windows` | 窗口身份、可见性、坐标、前后台、层级及浏览器标签页 |
| `pointer_list_apps` | 按名称查询已运行进程，包括隐藏、托盘中的应用 |
| `pointer_inspect_window` | 只读获取指定窗口的控件名称、类型及坐标 |
| `pointer_observe` | 桌面截图、真实后台窗口表面或浏览器视口画面 |
| `pointer_move` | 默认仅移动独立箭头；带浏览器观察编号时移动/悬停页面虚拟指针 |
| `pointer_click` | 左右键；`clickCount=2` 双击，或 `holdMs` 长按 |
| `pointer_drag` | 按下、沿可选 `path` 路径移动、释放，可控制时长 |
| `pointer_scroll` | 浏览器后台滚轮；原生遮挡窗口改用截图中的滚动条点击 |
| `pointer_pause` | 停止、释放按键和采集资源、隐藏箭头 |

没有长期绑定某个目标。每次点击、拖动、滚动都必须明确提供目标 `windowId` 和对应的观察编号；目标不符直接拒绝。有窗口编号时默认采集该窗口的真实画面，不会把遮挡它的前台画面裁剪后当成后台画面。桌面总览之后的动作同样需要明确目标，下一次可重新观察其他窗口。

启动应用前先用 `pointer_list_apps` 检查进程；没看到窗口不代表程序没运行。隐藏、最小化窗口可以发现，但不能直接截图或点击，也不会擅自恢复或再次启动。借鉴 Windows-MCP 的语义观察方式，新增原生 .NET UIAutomation 控件查询；没有引入 Python 或其前台鼠标、键盘操作。部分自绘应用只能读到标题栏。

原生窗口操作后，如果截图时间戳没有前进，会返回 `inputAllowed=false` 并拦截后续点击，报 `unverified_frame`。应停下来检查控件和兼容性，不能不断重试。即使有新帧，也必须确认业务结果。

**以返回的坐标空间为准：** 原生图像用屏幕物理像素；浏览器图像用视口 CSS 像素。两者都返回原点和缩放：

```text
x = screenshot.x + imageX * screenshot.scaleX
y = screenshot.y + imageY * screenshot.scaleY
```

观察编号 15 秒过期、输入后单次消耗。必须再次看图确认效果，`inputDelivered` 不等于业务完成。中断时尝试释放按键；释放未确认则禁止继续输入。强制结束所有进程无法保证完整清理。

### 为什么不直接向后台 Chrome 发系统鼠标消息？

Chrome 在部分鼠标按下处理中会主动置前并激活窗口；仅仅不移动真实指针不能防止它这样做。软件因此阻止向非前台 Chromium 窗口发送这类消息，要求网页使用 Browser Bridge。桥接仍是鼠标输入，不是 `DOM.click()`。

通用 Windows 消息的兼容性取决于应用；最小化/系统隐藏窗口、独立键盘、跨应用系统拖放不保证支持。触摸为实验功能，不会作为后台兜底。

## 测试到了什么程度

真实扩展链路已验证本地组件：左键、右键、滚轮、滑块、带转折路径的拼图式拖动、长按、双击、顺序点选、HTML5 拖放，以及拖动中暂停后释放。断言同时检查**页面状态和截图像素**，不是只看“发送成功”。

这些是**本地交互验证，不是真实验证码服务验收**。真实站点还会检查设备、轨迹和风险信号，可能需要人完成。桌面不抢前台、客户端原生安装和无界面组件测试的证据分别记录在[验证说明](docs/compatibility.md)，失败结果也会保留。

## 配置、备份与卸载

运行目录默认 `%LOCALAPPDATA%\AgentMouse\0.7.0-beta.3`，融合模式增加 `-fusion` 后缀，并解析 Windows 打包应用重定向后的物理路径。浏览器配对目录 `~/.agentmouse/browser` 使用每用户密钥和仅监听本机的认证服务，密钥及私人浏览器资料不进仓库。

安装保留其他配置并备份到运行目录 `backups/`。MCP 模式配置：Claude `~/.claude.json`、Codex `~/.codex/config.toml`、ZCode `~/.zcode/cli/config.json`、WorkBuddy `~/.workbuddy/mcp.json`、DSH `~/.dsh/profiles/<profile>/cordis.patch.yml`。原生升级只迁移 AgentMouse 自己的市场来源；原生命令失败明确报告可能完成的部分，仅 MCP 设置写入支持回滚。

`npm ci --ignore-scripts` 后可用 `--dry-run` 预览；`--home` 用于隔离测试目录，`--install-dir` 指定运行目录。避免同时保留独立 MCP 和原生插件中的同一服务。卸载原生插件用宿主插件管理器；先停止桥接并移除 Chrome 扩展，再删除运行与配对目录。整体恢复备份会撤销备份之后的其他编辑。

## 构建与验证

```powershell
npm ci --ignore-scripts
npm run build
npm test
npm run test:state
node scripts/test-clients.mjs
```

前四条不执行桌面输入；`test-clients` 检查已安装 Codex/ZCode 缓存和实际 MCP/SDK 状态链路，不冒充插件管理页视觉验收。CI 在 Node 18/24 跑安装和协议检查，并用固定版本 Chrome for Testing 跑真实扩展无界面组件测试，后者不等同于桌面验收。

`scripts/build-release.ps1` 构建完整 ZIP 与 SHA-256，`scripts/test-release.ps1` 验证解压安装。详见[接入资料](adapters/README.zh.md)和[兼容性记录](docs/compatibility.md)。

[Apache-2.0](LICENSE)。与上述智能体或浏览器产品无隶属关系。
