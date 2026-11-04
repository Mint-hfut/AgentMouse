# 智能体接入

推荐使用根目录 `install.cmd`：Codex、ZCode 默认通过原生插件 CLI 登记市场并安装插件，安装后查询已安装列表确认启用；其他三种默认增量加入 MCP 和技能。`--mode mcp` 可明确选择仅 MCP 接入。见 [README](../README.zh.md)。

## 插件与手动接入

根目录 `plugin.json`、`mcp.json` 是 Agent Plugins 格式，另有 `.claude-plugin`、`.codex-plugin`、`.zcode-plugin` 兼容清单。现在同时附带 `.agents/plugins/marketplace.json`、根目录 `marketplace.json` 和 Claude 兼容市场清单，均指向本插件。直接添加 stdio MCP 可用 `node agentmouse.mjs config <host>`，但不会创建插件卡片。

手动接入原生市场时，Codex 使用 `codex plugin marketplace add <目录>` 后执行 `codex plugin add agentmouse@agentmouse`；ZCode 在插件页添加该本地目录或 GitHub 仓库为市场，再安装 AgentMouse。源码形式缺少二进制时，首次控制器调用会自动构建原生程序，编译输出走 stderr，不混入 MCP 标准输出。

通过宿主支持的本地插件入口加载完整构建目录。Codex/ZCode 已验收原生安装和只读 MCP 连接；其他客户端的配置测试不能替代实际加载。同一客户端不要同时保留独立 MCP 注册和原生插件的同一服务。

`commands/` 提供 start、status、stop、doctor；对应 `agentmouse-*` 技能不依赖统一的斜杠命令语法。

0.6 测试版另附 Browser Bridge。安装器只准备配对目录，用户在 Chrome 加载一次生成的扩展；不修改现有资料目录、不开放调试端口或绕过策略。网页任务使用 `browser:` 标签页，按真实截图坐标操作。ZCode 在 Windows 替换既有缓存时可能出现 EPERM，版本/来源变化时安装器通过其原生命令保留数据并重装 AgentMouse 自己的插件与市场，避免改动其他插件。

需要 DSH 原生 bundle 或 WorkBuddy Connector 目录时，在已构建目录运行：

```powershell
node agentmouse.mjs prepare dsh .\dsh-agentmouse-bundle
node agentmouse.mjs prepare workbuddy .\workbuddy-agentmouse-connector
```

目标目录必须不存在。包内含运行程序和技能；移动后先在包内运行 `node setup.mjs` 更新绝对路径，再通过宿主入口导入。脚本安装 DSH 时仅加入 MCP、使用默认用户技能目录；导出的 DSH bundle 则自带专用技能提供方，两种方式任选一种。

## 格式参考

- [Claude Code MCP](https://code.claude.com/docs/en/mcp)：用户级 `~/.claude.json`。
- [Agent Skills](https://learn.chatgpt.com/docs/build-skills)：Codex 共享用户技能目录 `~/.agents/skills`。
- [ZCode MCP](https://zcode.z.ai/cn/docs/mcp-services)：`~/.zcode/cli/config.json`，结构为 `mcp.servers`。
- [ZCode Skills](https://zcode.z.ai/cn/docs/skill)：`~/.zcode/skills`。
- [WorkBuddy MCP](https://www.codebuddy.cn/docs/workbuddy/From-Beginner-to-Expert-Guide/Function-Description/MCP-Guide)：`~/.workbuddy/mcp.json`，结构为 `mcpServers`。
- [WorkBuddy 项目与兼容规则](https://www.codebuddy.cn/docs/workbuddy/From-Beginner-to-Expert-Guide/Function-Description/Project)：支持用户级 `.codebuddy` 技能与命令兼容路径。
- [Agent Plugins 规范](https://agent-plugins.org/specification)。
- DSH 依据其 0.2.1-alpha.1 安装包中的 MCP 客户端、文件系统技能文档与实际加载结果适配；其他版本需复核约定。

已测/未测项目与原型结果见[兼容性说明](../docs/compatibility.md)。
