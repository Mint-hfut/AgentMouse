import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFile, access } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { findClient, installedPlugin } from './install-plugins.mjs';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

// Read-only acceptance against installed clients; no model requests or desktop input.
for (const agent of ['codex', 'zcode']) {
  const cli = findClient(agent);
  const output = execFileSync(cli.executable, [...cli.args, ...(agent === 'codex' ? ['plugin', 'list', '--marketplace', 'agentmouse', '--json'] : ['plugins', 'list', '--json'])], { encoding: 'utf8', windowsHide: true, timeout: 30000, maxBuffer: 8 * 1024 * 1024 });
  const plugin = installedPlugin(agent, JSON.parse(output));
  assert.equal(plugin?.enabled, true);
  if (agent === 'codex') assert.equal(plugin.installed, true);
  else { assert.equal(plugin.skillCount, 5); assert.deepEqual(plugin.mcpServerNames, ['plugin:agentmouse:agentmouse']); assert.deepEqual(plugin.diagnostics, []); }
  const root = agent === 'codex' ? join(process.env.CODEX_HOME || join(homedir(), '.codex'), 'plugins/cache/agentmouse/agentmouse', plugin.version) : plugin.rootPath;
  await access(join(root, 'skills/agentmouse/SKILL.md'));
  const config = JSON.parse(await readFile(join(root, agent === 'codex' ? 'mcp.json' : '.mcp.json'), 'utf8')).mcpServers.agentmouse;
  const args = config.args.map(arg => arg.replace(/\$\{(?:PLUGIN_ROOT|CLAUDE_PLUGIN_ROOT|ZCODE_PLUGIN_ROOT)\}/g, root));
  const client = new Client({ name: 'agentmouse-host-acceptance', version: '0.7.0-beta.4' });
  try {
    await client.connect(new StdioClientTransport({ command: config.command, args, cwd: root, stderr: 'pipe' }));
    const tools = (await client.listTools()).tools;
    const fused = args.some(arg => arg.endsWith("fusion-entry.mjs"));
    assert.equal(tools.length, fused ? 26 : 11); assert.equal(client.getServerVersion().version, plugin.version);
    if (!fused) assert.deepEqual(tools.find(tool => tool.name === 'pointer_observe').inputSchema.properties.capture.enum, ['desktop', 'window']);
    const status = await client.callTool({ name: 'pointer_status', arguments: {} });
    const value = JSON.parse(status.content.find(item => item.type === 'text').text);
    if (status.isError) assert.equal(value.code, 'not_running'); else assert.equal(value.scope, 'desktop');
    console.log(`PASS ${agent}: native plugin installed/enabled, cached skills and MCP manifest, ${tools.length} tools, real pointer_status round-trip (${status.isError ? 'controller closed' : 'controller running'})`);
  } finally { await client.close(); }
}
