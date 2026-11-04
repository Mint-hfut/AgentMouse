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

**0.5.1 起，选择 Codex、ZCode 会真正安装原生插件。** Codex 需要 CLI（缺少时可安装 `npm install -g @openai/codex`）；ZCode 使用随客户端附带的插件 CLI，自动查找标准 Windows 安装目录或 PATH。找不到时会明确报错，不会把 MCP 接入冒充插件安装成功。Claude Code、WorkBuddy、DSH 当前仍使用 MCP＋技能接入，不会因此在它们的插件管理页产生插件卡片。

指定安装对象，无需菜单：

```powershell
.\install.cmd --agent codex
.\install.cmd --agent claude-code,codex,dsh
.\install.cmd --agent all
.\install.cmd --agent dsh --profile web
.\install.cmd --agent codex --mode mcp
```

安装后刷新插件页或重启智能体。Codex、ZCode 应列出 **agentmouse@agentmouse** 为已安装、已启用；安装器会查询原生插件列表确认后才报告成功。`--mode mcp` 明确选择仅 MCP＋技能，不产生插件卡片。随后对智能体说：**“使用 AgentMouse 打开控制器，检查连接状态。”** 在控制器中点击一次 **允许全桌面操作**，即可跨软件执行任务。控制器当前为中文界面。

## 安装器会做什么

运行文件默认保存在 `%LOCALAPPDATA%\AgentMouse\0.5.1`；Windows 打包应用可能重定向该目录，安装器会解析真实路径供其他进程使用。原生模式建立干净的 `plugin/` 目录和市场清单，再通过宿主原生命令安装；配置备份留在插件包外。安装后可删除下载目录，Node.js 仍需保留。

下表是 **仅 MCP 模式** 的配置路径（Claude Code、WorkBuddy、DSH 默认使用；Codex、ZCode 需加 `--mode mcp`）：

| 智能体 | MCP 配置 | 技能目录 |
|---|---|---|
| Claude Code | `~/.claude.json` 的 `mcpServers.agentmouse` | `~/.claude/skills/agentmouse*` |
| Codex | `~/.codex/config.toml` 的 `mcp_servers.agentmouse` | `~/.agents/skills/agentmouse*` |
| ZCode | `~/.zcode/cli/config.json` 的 `mcp.servers.agentmouse` | `~/.zcode/skills/agentmouse*` |
| WorkBuddy | `~/.workbuddy/mcp.json` 的 `mcpServers.agentmouse` | `~/.codebuddy/skills/agentmouse*`（用户级兼容路径） |
| DSH | `~/.dsh/profiles/<profile>/cordis.patch.yml` 中的 MCP 插件 | `~/.dsh/skills/agentmouse*` |

`~` 是用户主目录。只向选中的智能体注册 MCP；Codex 使用共享技能目录，其他兼容工具也可能发现这些技能。Claude Code、ZCode、WorkBuddy 同时安装命令，五种智能体都有同等用途的启停技能。DSH 默认 `web` profile，需要已有可用的模型与服务配置。

已有其他服务和设置会保留。备份保存到运行目录 `backups/`，`index.json` 记录原路径。重复安装不重复添加。仅 MCP 模式会在配置解析失败时提前退出、写入失败时回滚；原生模式先备份配置和插件登记文件，再调用宿主安装器，失败时明确报告可能已完成的部分，不承诺原生命令的整组回滚。仅 MCP 模式下，非本安装器管理的 AgentMouse TOML 条目不会被覆盖。安装时建议关闭配置编辑器。

仅预览，不写入设置和运行文件：

```powershell
npm ci --ignore-scripts
.\install.cmd --agent all --dry-run
```

高级参数：`--home <目录>` 使用独立用户目录并忽略真实目录的环境覆盖，`--install-dir <目录>` 指定运行目录。正常安装识别 `CODEX_HOME`、`CLAUDE_CONFIG_DIR`（技能与命令）、`DSH_HOME`。

原生插件通过宿主插件管理器卸载，Codex 也可用 `codex plugin remove agentmouse@agentmouse`。仅 MCP 模式需先停止 AgentMouse，移除其 MCP 条目、五个技能目录及命令目录，最后删除运行目录。整体恢复备份也会撤销备份之后的其他配置改动。由 0.5.0 升级时，需移除旧的独立 MCP 登记，避免与原生插件重复。

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

其他 MCP 客户端可用 `node agentmouse.mjs config mcp` 获取本地配置片段，再导入 `skills/`；原生模式的运行文件位于 `plugin/` 内。见[接入说明](adapters/README.zh.md)。同一客户端不要同时保留独立 MCP 注册和原生插件中的同一服务，避免重复。

## 开发与验证

```powershell
npm ci --ignore-scripts
npm run build
npm test
npm run test:state
```

测试涵盖 MCP 校验、stdio 与官方 SDK、接入包重定位、五种智能体配置、备份、重复安装、预览、回滚、原生指针状态与坐标独立性。安装测试全部使用临时用户目录，不改真实配置；这些命令不执行桌面点击或音频播放。CI 在 Windows 的 Node 18 和 24 执行同一组检查。

另有 `node scripts/test-clients.mjs`，检查**真正安装的 Codex、ZCode 客户端**及缓存插件，读取它们的 MCP 清单，验证九个工具和真实只读状态调用。要求本机已有两个客户端和插件，普通 CI 不会把它冒充为已完成。0.5.1 在 Codex CLI 0.153.4、ZCode CLI 0.17.1 上通过；插件页视觉显示和实际桌面动作仍是另外两项验收。

交互式测试见[验证指南](docs/compatibility.md)。`powershell -File scripts/build-release.ps1` 会构建、测试并生成完整 Windows ZIP 与 SHA-256 文件；`scripts/test-release.ps1` 检查解压后的安装，`scripts/test-source.ps1` 检查首次源码安装的依赖下载与编译，两者都使用临时用户目录。版本标签会触发 GitHub 发布工作流。私人 profile、开发日志和截图不会进入仓库或发布包。

## 许可证

[Apache-2.0](LICENSE)。项目与上述智能体产品无隶属关系。
