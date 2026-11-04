import assert from 'node:assert/strict';
import { mkdtemp, mkdir, cp, readFile, writeFile, access, rm } from 'node:fs/promises';
import { join, resolve, dirname, basename } from 'node:path';
import { tmpdir } from 'node:os';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
const temp = await mkdtemp(join(tmpdir(), 'agentmouse-bootstrap-'));
const client = new Client({ name: 'agentmouse-source-bootstrap', version: '0.5.1' });
try {
  await mkdir(join(temp, 'scripts'));
  for (const file of ['mcp-server.mjs', 'browser-router.mjs', 'bridge-client.mjs', 'scripts/build-native.mjs', 'DesktopInventory.cs','Native.cs','WindowCapture.cs', 'Core.cs', 'Transport.cs', 'Ui.cs', 'Program.cs', 'SelfTest.cs']) await cp(resolve(file), join(temp, file));
  await writeFile(join(temp, 'Native.cs'), '#warning AGENTMOUSE_BOOTSTRAP_TEST\n' + await readFile(join(temp, 'Native.cs'), 'utf8'));
  const transport = new StdioClientTransport({ command: process.execPath, args: [join(temp, 'mcp-server.mjs')], cwd: temp, stderr: 'pipe' });
  await client.connect(transport);
  let buildLog = ''; transport.stderr.on('data', chunk => { buildLog += chunk; });
  const result = await client.callTool({ name: 'pointer_status', arguments: {} });
  const value = JSON.parse(result.content.find(item => item.type === 'text').text);
  if (result.isError) assert.equal(value.code, 'not_running'); else assert.equal(value.scope, 'desktop');
  await access(join(temp, 'dist/AgentMouse.exe')); await access(join(temp, 'dist/AgentMouse.Cli.exe'));
  assert.match(buildLog, /AGENTMOUSE_BOOTSTRAP_TEST/);
  console.log('PASS source plugin first-use build: native files generated, compiler warnings stay on stderr, MCP status round-trip remains valid; no GUI/input');
} finally {
  await client.close();
  assert.equal(dirname(resolve(temp)), resolve(tmpdir())); assert.ok(basename(temp).startsWith('agentmouse-bootstrap-'));
  await rm(temp, { recursive: true, force: true });
}
