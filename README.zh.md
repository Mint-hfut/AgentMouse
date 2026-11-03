# AgentMouse

**让 AI Agent 在后台操作电脑，Agent 指针与用户鼠标解耦，不和你抢鼠标。**

[English](README.md) · [兼容性与测试](docs/compatibility.md) · [反馈问题](https://github.com/Mint-hfut/AgentMouse/issues)

AgentMouse 为智能体提供 Windows 桌面截图、点击和滚动能力。橙色 **AGENT** 指针使用独立坐标，你移动真实鼠标时，它不会跟着移动。默认后台模式通过窗口消息操作界面，不移动系统鼠标，也不主动请求切换前台窗口。

无需虚拟机、分屏或安装驱动。包含本地控制程序、MCP 服务、Agent Skills 和启停命令，一键接入 **Claude Code、Codex、ZCode、WorkBuddy、DSH**，也可手动接入其他支持 MCP 的智能体。

**当前为实验版本，仅支持 Windows x64。** “后台”指尽量不打断你的鼠标操作，目标仍需在当前桌面可见，并非可以任意控制被遮挡或最小化的窗口。应用可能自行改变焦点；触摸实验模式可能移动真实鼠标。它没有为 Windows 创建第二套独立焦点或键盘。见[兼容性说明](docs/compatibility.md)。

## 一键安装

环境要求：Windows x64、**Node.js 18+ x64（含 npm）**、Windows .NET Framework 4.x。通常无需管理员权限；看图操作需要支持图片输入的模型。

从 [Releases](https://github.com/Mint-hfut/AgentMouse/releases) 下载完整 Windows ZIP（发布后可用），解压后双击 **`install.cmd`**，选择希望接入的智能体。包内含控制程序和安装器依赖，仍需本机安装 Node.js。

源码安装：

```powershell
git clone https://github.com/Mint-hfut/AgentMouse.git
cd AgentMouse
.\install.cmd
```

源码安装会下载锁定的 npm 依赖，禁用依赖的生命周期脚本，再调用 Windows 自带的 .NET Framework 编译器构建程序。不要求 Visual Studio 或 .NET SDK。

指定安装对象，无需菜单：

```powershell
.\install.cmd --agent codex
.\install.cmd --agent claude-code,codex,dsh
.\install.cmd --agent all
.\install.cmd --agent dsh --profile web
```

安装后重启选中的智能体，对它说：**“使用 AgentMouse 打开控制器，检查连接状态。”** 在控制器中点击一次 **允许全桌面操作**，即可跨软件执行任务。控制器当前为中文界面。

## 安装器会做什么

运行文件默认保存在 `%LOCALAPPDATA%\AgentMouse\0.5.0`，智能体引用这里的固定路径。安装后可删除下载目录；Node.js 需要保留在原路径。

| 智能体 | MCP 配置 | 技能目录 |
|---|---|---|
| Claude Code | `~/.claude.json` 的 `mcpServers.agentmouse` | `~/.claude/skills/agentmouse*` |
| Codex | `~/.codex/config.toml` 的 `mcp_servers.agentmouse` | `~/.agents/skills/agentmouse*` |
| ZCode | `~/.zcode/cli/config.json` 的 `mcp.servers.agentmouse` | `~/.zcode/skills/agentmouse*` |
| WorkBuddy | `~/.workbuddy/mcp.json` 的 `mcpServers.agentmouse` | `~/.codebuddy/skills/agentmouse*`（用户级兼容路径） |
| DSH | `~/.dsh/profiles/<profile>/cordis.patch.yml` 中的 MCP 插件 | `~/.dsh/skills/agentmouse*` |

`~` 是用户主目录。只向选中的智能体注册 MCP；Codex 使用共享技能目录，其他兼容工具也可能发现这些技能。Claude Code、ZCode、WorkBuddy 同时安装命令，五种智能体都有同等用途的启停技能。DSH 默认 `web` profile，需要已有可用的模型与服务配置。

已有其他服务和设置会保留。改动前的文件保存到运行目录 `backups/`，`index.json` 记录原路径与备份路径。重复安装不重复添加；配置解析失败会在修改设置前退出，写入失败会回滚。已有非本安装器管理的 AgentMouse TOML 条目会保留并提示手动处理。安装时建议关闭配置编辑器；安装器会尽可能检测并发修改。

仅预览，不写入设置和运行文件：

```powershell
npm ci --ignore-scripts
.\install.cmd --agent all --dry-run
```

高级参数：`--home <目录>` 使用独立用户目录并忽略真实目录的环境覆盖，`--install-dir <目录>` 指定运行目录。正常安装识别 `CODEX_HOME`、`CLAUDE_CONFIG_DIR`（技能与命令）、`DSH_HOME`。

卸载时先停止 AgentMouse，移除 `agentmouse` MCP 条目（DSH 为 `agentmouse-mcp` 插件），删除本项目安装的五个 `agentmouse*` 技能目录及 AgentMouse 命令目录，最后删除运行目录。用备份整体恢复配置也会撤销备份时间之后的其他配置改动。

## 使用方式

默认操作整个可见桌面，**不需要绑定目标窗口**。`windowId` 只用于裁剪截图，不限制后续点击范围。

| MCP 工具 | 功能 |
|---|---|
| `pointer_open` | 打开控制器，新实例默认暂停 |
| `pointer_status` | 查看状态、坐标、模式与限制 |
| `pointer_list_windows` | 列出可见窗口 |
| `pointer_observe` | 桌面截图，可选窗口裁剪 |
| `pointer_move` | 只移动 Agent 指针 |
| `pointer_click` | 左键或右键点击 |
| `pointer_scroll` | 在指定位置滚动 |
| `pointer_drag` | 触摸实验拖动，可能影响真实鼠标和焦点 |
| `pointer_pause` | 停止输入并隐藏 Agent 箭头 |

坐标是屏幕物理像素，支持多显示器负坐标；截图附带原点和缩放信息。点击、滚动、拖动使用新的 `observationId`，有效期 15 秒，单次使用。智能体必须再次观察来确认效果，`inputDelivered` 不等于任务完成。

你正在使用键鼠时，Agent 会等待，检测到冲突时取消操作。这用于减少干扰，并不意味着两套独立键盘或焦点可以完全同时工作。后台模式没有完整原生悬停、拖拽或文字输入能力；不兼容时不会偷偷调用真实鼠标瞬移兜底。

停止方式：控制器暂停按钮、界面显示的全局快捷键（通常 **Ctrl+Alt+P**）、MCP `pointer_pause`，或：

```powershell
node agentmouse.mjs stop
```

暂停后箭头隐藏，关闭控制器则完全退出。技能要求任务结束或中断后暂停；MCP 正常断开也会尝试清理本连接发起的输入，强制结束进程无法保证执行清理。

其他 MCP 客户端可用 `node agentmouse.mjs config mcp` 获取本地配置片段，再导入 `skills/`。原生插件清单见[接入说明](adapters/README.zh.md)。同一客户端只选择插件安装或脚本安装其中一种，避免重复注册。

## 开发与验证

```powershell
npm ci --ignore-scripts
npm run build
npm test
npm run test:state
```

测试涵盖 MCP 校验、stdio 与官方 SDK、接入包重定位、五种智能体配置、备份、重复安装、预览、回滚、原生指针状态与坐标独立性。安装测试全部使用临时用户目录，不改真实配置；这些命令不执行桌面点击或音频播放。CI 在 Windows 的 Node 18 和 24 执行同一组检查。

交互式测试见[验证指南](docs/compatibility.md)。`powershell -File scripts/build-release.ps1` 会构建、测试并生成完整 Windows ZIP 与 SHA-256 文件；`scripts/test-release.ps1` 检查解压后的安装，`scripts/test-source.ps1` 检查首次源码安装的依赖下载与编译，两者都使用临时用户目录。版本标签会触发 GitHub 发布工作流。私人 profile、开发日志和截图不会进入仓库或发布包。

## 许可证

[Apache-2.0](LICENSE)。项目与上述智能体产品无隶属关系。
