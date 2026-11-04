import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, dirname, basename } from 'node:path';
import { randomUUID } from 'node:crypto';
import { setupBrowserBridge } from './scripts/browser-setup.mjs';
import { createBrowserBridge } from './bridge-server.mjs';
import { bridgeRequest } from './bridge-client.mjs';

const temp = await mkdtemp(join(tmpdir(), 'agentmouse-pairing-')); let bridge, polling, closing = false;
try {
  const env = { AGENTMOUSE_HOME: join(temp, 'state') };
  const preview = await setupBrowserBridge({ sourceRoot: resolve('.'), home: temp, env, dryRun: true });
  await assert.rejects(access(preview.stateRoot), { code: 'ENOENT' });
  const installed = await setupBrowserBridge({ sourceRoot: resolve('.'), home: temp, env });
  const config = JSON.parse(await readFile(join(installed.stateRoot, 'config.json'), 'utf8'));
  assert.equal(config.token.length, 64); assert.match(installed.extensionId, /^[a-p]{32}$/);
  await setupBrowserBridge({ sourceRoot: resolve('.'), home: temp, env });
  assert.equal(JSON.parse(await readFile(join(installed.stateRoot, 'config.json'), 'utf8')).token, config.token);
  const commands = [], png = Buffer.alloc(24);Buffer.from([137,80,78,71,13,10,26,10]).copy(png);png.writeUInt32BE(100,16);png.writeUInt32BE(100,20);
  bridge = createBrowserBridge({ token: config.token, extensionId: installed.extensionId, checkPermission: async () => ({ armed: true, foregroundWindowId: 'code' }) });
  await new Promise(done => bridge.server.listen(0, '127.0.0.1', done));config.port = bridge.server.address().port;
  const base = 'http://127.0.0.1:' + config.port;
  assert.equal((await fetch(base + '/health')).status, 403);
  assert.equal((await fetch(base + '/health', { headers: { Authorization: 'Bearer ' + config.token, Origin: 'https://untrusted.example' } })).status, 403);
  assert.equal((await fetch(base + '/health', { headers: { Authorization: 'Bearer ' + 'ü'.repeat(64) } })).status, 403);
  const headers = { Authorization: 'Bearer ' + config.token, Origin: 'chrome-extension://' + installed.extensionId, 'Content-Type': 'application/json', 'X-AgentMouse-Client': randomUUID() };
  polling = (async () => {
    while (true) {
      try {
        const item = await (await fetch(base + '/next', { headers })).json();if (item.op === 'idle') continue;
        commands.push(item);let result = {};
        if (item.op === 'tabs') result = [{ id: 1, title: 'Local fixture' }];
        if (item.method === 'Page.getLayoutMetrics') result = { cssVisualViewport: { clientWidth: 100, clientHeight: 100, pageX: 0, pageY: 0, scale: 1 } };
        if (item.method === 'Page.captureScreenshot') result = { data: png.toString('base64') };
        await fetch(base + '/reply', { method: 'POST', headers, body: JSON.stringify({ id: item.id, result }) });
      } catch (error) { if (closing) return; throw error; }
    }
  })();
  for (let i = 0; i < 50 && !(await bridgeRequest(config, '/health')).connected; i++) await new Promise(done => setTimeout(done, 5));
  assert.equal((await bridgeRequest(config, '/call', { method: 'list_tabs' })).result[0].id, 1);
  const observed = (await bridgeRequest(config, '/call', { method: 'observe', tabId: 1 })).result;
  const clicked = (await bridgeRequest(config, '/call', { method: 'click', tabId: 1, observationId: observed.observationId, x: 20, y: 30 })).result;
  assert.equal(clicked.buttonReleased, true);assert.ok(commands.some(item => item.method === 'Input.dispatchMouseEvent' && item.params.type === 'mousePressed'));
  await bridgeRequest(config, '/call', { method: 'pause' });
  console.log('PASS browser bridge: private pairing, repeat setup, authenticated loopback, origin rejection, command routing and release; extension transport is simulated here');
} finally {
  closing = true;if (bridge) await bridge.close();if (polling) await polling;
  assert.equal(dirname(resolve(temp)), resolve(tmpdir()));assert.ok(basename(temp).startsWith('agentmouse-pairing-'));await rm(temp, { recursive: true, force: true });
}
