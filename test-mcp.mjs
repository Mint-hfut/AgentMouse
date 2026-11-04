import assert from 'node:assert/strict';
import { createHandler } from './mcp-server.mjs';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

let calls = [];
const handler = createHandler(async request => {
  calls.push(request);
  if (request.method === 'status') return { ok: true, result: { backgroundWindowCapture: true } };
  return request.method === 'observe'
    ? { ok: true, result: { scope: request.capture === 'window' ? 'window' : 'desktop', observationId: 'token', screenshot: { data: 'iVBORw==', mimeType: 'image/png', x: 10, y: 20, captureMethod: request.capture === 'window' ? 'Windows.Graphics.Capture' : 'desktop' } } }
    : { ok: false, error: { code: 'paused', message: 'paused' } };
});
let id = 0;
const ask = (method, params) => handler({ jsonrpc: '2.0', id: ++id, method, params });
assert.equal((await ask('tools/list')).error.code, -32000);
assert.equal((await ask('initialize', { protocolVersion: '2025-06-18' })).result.protocolVersion, '2025-06-18');
assert.equal(await handler({ jsonrpc: '2.0', method: 'notifications/initialized' }), null);
assert.equal((await ask('tools/list')).result.tools.length, 9);
assert.equal((await ask('tools/call', { name: 'pointer_click', arguments: { x: 1, y: 2 } })).error.code, -32602);
assert.equal((await ask('tools/call', { name: 'pointer_move', arguments: { x: 1.5, y: 2 } })).error.code, -32602);
assert.equal((await ask('tools/call', { name: 'pointer_move', arguments: { x: 1, y: 2, arm: true } })).error.code, -32602);
assert.equal(calls.length, 0);
for (const path of [[{ x: 1.2, y: 3 }], [{ x: 1, y: 2, extra: true }], Array.from({ length: 129 }, () => ({ x: 1, y: 2 })), [{ x: 100001, y: 2 }], [null], 'not-an-array']) {
  assert.equal((await ask('tools/call', { name: 'pointer_drag', arguments: { observationId: 'token', x: 1, y: 2, toX: 4, toY: 5, path } })).error.code, -32602);
}
assert.equal(calls.length, 0);
assert.equal((await ask('tools/call', { name: 'pointer_observe', arguments: { capture: 'screen-magic' } })).error.code, -32602);
assert.equal(calls.length, 0);
const image = (await ask('tools/call', { name: 'pointer_observe' })).result.content;
assert.equal(image[1].type, 'image');
assert.equal(image[1].data, 'iVBORw==');
assert.equal(JSON.parse(image[0].text).screenshot.data, undefined);
assert.equal((await ask('tools/call', { name: 'pointer_click', arguments: { observationId: 'token', x: 1, y: 2 } })).result.isError, true);
assert.deepEqual(calls[1], { method: 'click', observationId: 'token', x: 1, y: 2 });
await ask('tools/call', { name: 'pointer_observe', arguments: { windowId: '1234', capture: 'window' } });
assert.deepEqual(calls.at(-1), { method: 'observe', windowId: '1234', capture: 'window' });
for (const claimed of [false, true]) {
  const bad = createHandler(async request => request.method === 'status' ? { ok: true, result: { backgroundWindowCapture: claimed } } : { ok: true, result: { scope: 'desktop', screenshot: { data: 'iVBORw==', captureMethod: 'desktop' } } });
  await bad({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18' } });
  const rejected = await bad({ jsonrpc: '2.0', id: 2, method: 'tools/call', params: { name: 'pointer_observe', arguments: { capture: 'window', windowId: '1234' } } });
  assert.equal(rejected.result.isError, true);
  assert.equal(JSON.parse(rejected.result.content[0].text).code, claimed ? 'background_capture_missing' : 'upgrade_required');
}

const child = spawn(process.execPath, [fileURLToPath(new URL('./mcp-server.mjs', import.meta.url))], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
const responses = createInterface({ input: child.stdout });
let sequence = 0;
const pending = new Map();
responses.on('line', line => { const response = JSON.parse(line); pending.get(response.id)?.(response); pending.delete(response.id); });
function live(method, params) {
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`Timed out: ${method}`)); }, 20000);
    pending.set(id, response => { clearTimeout(timer); resolve(response); });
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}
try {
  assert.equal((await live('initialize', { protocolVersion: '2025-06-18' })).result.serverInfo.name, 'agentmouse');
  assert.equal((await live('tools/list')).result.tools.length, 9);
  if (process.argv.includes('--live')) {
    const status = await live('tools/call', { name: 'pointer_status' });
    assert.equal(status.result.isError, undefined);
    assert.equal(JSON.parse(status.result.content[0].text).armed, false);
    console.log('PASS live MCP -> CLI -> named pipe -> Windows app');
  }
} finally { child.stdin.end(); responses.close(); child.kill(); }
console.log('PASS MCP validation, initialization, image results, errors and stdio transport');
