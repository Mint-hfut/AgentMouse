AgentMouse 0.5.1 fixes native plugin installation for Codex and ZCode. Version 0.5.0 registered standalone MCP and skills but did not create an installed-plugin entry.

- Codex/ZCode now use their native marketplace/install commands and verify the installed/enabled result. Missing native CLIs fail explicitly.
- Claude Code, WorkBuddy and DSH use MCP + skills; `--mode mcp` explicitly selects that mode for any target.
- Marketplace catalogs, clean plugin payloads, configuration backups and physical Windows path resolution.
- Real Codex 0.153.4 / ZCode 0.17.1 installation and read-only MCP connectivity checked locally. GUI actions/browser visibility were not accepted.
- MCP server, five Agent Skills, lifecycle commands and native plugin manifests.
- Preserves existing settings, makes backups, supports dry runs and rolls back failed settings writes.
- English/Chinese READMEs and documented compatibility limits.
- Windows build, MCP/SDK tests, installer tests and native state checks.

Extract the Windows ZIP, install Node.js 18+ x64 if needed, then double-click `install.cmd`. Choose your agent and restart it. Enable desktop input once in the local controller before use.

Experimental: background messages depend on app compatibility; visible windows only, no independent keyboard/native hover/background drag. Touch may move the real cursor or change focus. Five-host configuration testing does not imply five-host GUI acceptance.

中文：解压 Windows ZIP 后双击 `install.cmd`，选择要接入的智能体。需要 Node.js 18+ x64。默认后台模式不移动真实鼠标；触摸为实验模式，可能影响真实鼠标和焦点。详见中文 README。
