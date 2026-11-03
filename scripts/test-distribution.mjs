import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { resolve, join, dirname, basename } from 'node:path';
import { tmpdir } from 'node:os';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const payload = resolve(process.argv[2]);
const temp = await mkdtemp(join(tmpdir(), 'agentmouse-distribution-'));
try {
  const home = join(temp, '用户 home'), runtime = join(temp, '独立 runtime');
  // The payload must work without node_modules or relative paths from the checkout.
  const options = [join(payload, 'install.mjs'), '--agent', 'all', '--home', home, '--install-dir', runtime];
  execFileSync(process.execPath, options, { cwd: temp, windowsHide: true });
  const again = execFileSync(process.execPath, options, { cwd: temp, windowsHide: true, encoding: 'utf8' });
  assert.match(again, /"changes": \[\]/);
  const doctor = JSON.parse(execFileSync(process.execPath, [join(runtime, 'agentmouse.mjs'), 'doctor'], { cwd: temp, windowsHide: true, encoding: 'utf8' }));
  assert.equal(doctor.ready, true);
  assert.deepEqual(JSON.parse(await readFile(join(home, '.workbuddy/mcp.json'), 'utf8')).mcpServers.agentmouse.args, [join(runtime, 'mcp-server.mjs')]);
  execFileSync(join(runtime, 'dist/AgentMouse.Cli.exe'), ['--state-self-test'], { windowsHide: true });
  const client = new Client({ name: 'agentmouse-distribution-test', version: '0.5.0' });
  try {
    await client.connect(new StdioClientTransport({ command: process.execPath, args: [join(runtime, 'mcp-server.mjs')], cwd: temp, stderr: 'pipe' }));
    assert.equal(client.getServerVersion().name, 'agentmouse');
    assert.equal((await client.listTools()).tools.length, 9);
  } finally { await client.close(); }
  console.log('PASS distribution: all five isolated hosts, repeat install, relocated runtime doctor, native checks and official MCP SDK');
} finally {
  assert.equal(dirname(resolve(temp)), resolve(tmpdir())); assert.ok(basename(temp).startsWith('agentmouse-distribution-'));
  await rm(temp, { recursive: true, force: true });
}
