import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const temporary = await mkdtemp(join(tmpdir(), 'agentmouse-fusion-sdk-'));
const client = new Client({ name: 'fusion-sdk-acceptance', version: '1' });
const value = result => { assert.ok(!result.isError, JSON.stringify(result)); return result.structuredContent ?? JSON.parse(result.content.find(item => item.type === 'text').text); };
try {
  await writeFile(join(temporary, 'sample.txt'), 'fusion-file-read-fixture');
  await client.connect(new StdioClientTransport({ command: process.execPath, args: ['fusion-entry.mjs'], cwd: resolve('.'), stderr: 'pipe' }));
  const tools = (await client.listTools()).tools;
  assert.equal(tools.length, 27);
  for (const name of ['Snapshot','Click','Move','Scroll']) assert.ok(tools.some(tool => tool.name === name));
  assert.ok(tools.find(tool => tool.name === 'Click').inputSchema.required.includes('window_id'));
  assert.ok(tools.find(tool => tool.name === 'Click').inputSchema.required.includes('observation_id'));
  const inventory = value(await client.callTool({ name: 'App', arguments: { mode: 'find', query: 'node' } }));
  assert.ok(inventory.processes.length > 0);
  const file = await client.callTool({ name: 'FileSystem', arguments: { mode: 'read', path: join(temporary, 'sample.txt') } });
  assert.ok(!file.isError); assert.match(JSON.stringify(file.content), /fusion-file-read-fixture/);
  const displays = await client.callTool({ name: 'DisplayInventory', arguments: {} }); assert.ok(!displays.isError, JSON.stringify(displays));
  for (const [name, args] of [['Type',{text:'never type this'}],['Shortcut',{shortcut:'alt+tab'}],['MultiEdit',{}],['MultiSelect',{}]]) {
    const blocked = await client.callTool({ name, arguments: args }); assert.equal(blocked.isError, true);
  }
  const unfounded = await client.callTool({ name: 'Click', arguments: { window_id: '123', observation_id: 'unknown', loc: [1,1] } });
  assert.equal(unfounded.isError, true); assert.match(JSON.stringify(unfounded.content), /stale_observation/);
  console.log('PASS fusion real SDK: 27 tools, mandatory target/token, upstream file and display tools, running-process discovery, blocked foreground keyboard/batch calls and stale input; no GUI/input');
} finally {
  await client.close();
  assert.equal(dirname(resolve(temporary)), resolve(tmpdir())); assert.ok(basename(temporary).startsWith('agentmouse-fusion-sdk-'));
  await rm(temporary, { recursive: true, force: true });
}
