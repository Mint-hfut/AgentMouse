# 智能体接入

推荐使用根目录 `install.cmd`：选择智能体后，安装器向用户配置增量加入 MCP，并复制技能；路径见 [README](../README.zh.md)。所有接入复用相同的本地控制程序和九个 MCP 工具。

## 插件与手动接入

根目录 `plugin.json`、`mcp.json` 是 Agent Plugins 格式，另有 `.claude-plugin`、`.codex-plugin`、`.zcode-plugin` 兼容清单。清单使用宿主的插件根路径变量；直接添加 stdio MCP 应使用 `node agentmouse.mjs config <host>` 生成绝对路径配置。仓库没有附带本地插件市场。

通过宿主支持的本地插件入口加载完整构建目录。原生插件加载未全部验收，安装配置测试不能替代客户端实际加载。同一客户端不要同时启用脚本和插件两份 MCP。

`commands/` 提供 start、status、stop、doctor；对应 `agentmouse-*` 技能不依赖统一的斜杠命令语法。

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
