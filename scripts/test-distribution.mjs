import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, realpath } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { resolve, join, dirname, basename } from 'node:path';
import { tmpdir } from 'node:os';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const payload = resolve(process.argv[2]);
const native = process.argv.includes('--native');
const temp = await mkdtemp(join(tmpdir(), 'agentmouse-distribution-'));
try {
  const home = join(temp, '用户 home'), runtime = join(temp, '独立 runtime');
  // The payload must work without node_modules or relative paths from the checkout.
  const options = [join(payload, 'install.mjs'), '--agent', native ? 'codex,zcode' : 'all', ...(!native ? ['--mode', 'mcp'] : []), '--home', home, '--install-dir', runtime];
  execFileSync(process.execPath, options, { cwd: temp, windowsHide: true });
  const again = execFileSync(process.execPath, options, { cwd: temp, windowsHide: true, encoding: 'utf8' });
  if (native) assert.equal((again.match(/"enabled": true/g) || []).length, 2); else assert.match(again, /"changes": \[\]/);
  const activeRoot = native ? join(runtime, 'plugin') : runtime;
  const doctor = JSON.parse(execFileSync(process.execPath, [join(activeRoot, 'agentmouse.mjs'), 'doctor'], { cwd: temp, windowsHide: true, encoding: 'utf8' }));
  assert.equal(doctor.ready, true);
  if (!native) assert.deepEqual(JSON.parse(await readFile(join(home, '.workbuddy/mcp.json'), 'utf8')).mcpServers.agentmouse.args, [join(await realpath(runtime), 'mcp-server.mjs')]);
  execFileSync(join(activeRoot, 'dist/AgentMouse.Cli.exe'), ['--state-self-test'], { windowsHide: true });
  const client = new Client({ name: 'agentmouse-distribution-test', version: '0.6.0-beta.3' });
  try {
    await client.connect(new StdioClientTransport({ command: process.execPath, args: [join(activeRoot, 'mcp-server.mjs')], cwd: temp, stderr: 'pipe' }));
    assert.equal(client.getServerVersion().name, 'agentmouse');
    assert.equal((await client.listTools()).tools.length, 11);
  } finally { await client.close(); }
  console.log(`PASS distribution: ${native ? 'real Codex/ZCode native plugins' : 'five MCP layouts'} in isolated homes, repeat install, relocated runtime doctor, native checks and official MCP SDK`);
} finally {
  assert.equal(dirname(resolve(temp)), resolve(tmpdir())); assert.ok(basename(temp).startsWith('agentmouse-distribution-'));
  await rm(temp, { recursive: true, force: true });
}
