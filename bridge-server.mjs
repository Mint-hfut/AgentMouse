import { createServer } from 'node:http';
import { randomUUID, timingSafeEqual } from 'node:crypto';
import { BrowserPointer } from './browser-pointer.mjs';

const allowed = new Set(['Emulation.setFocusEmulationEnabled', 'Page.getLayoutMetrics', 'Page.captureScreenshot', 'Input.dispatchMouseEvent', 'Input.setInterceptDrags', 'Input.dispatchDragEvent', 'Input.cancelDragging']);
const failure = (code, message) => Object.assign(new Error(message), { code });
export function createBrowserBridge({ token, extensionId, checkPermission, pauseController = async () => {}, version = '0.6.0' }) {
  if (!/^[a-f0-9]{64}$/.test(token) || !/^[a-p]{32}$/.test(extensionId)) throw new Error('Invalid bridge pairing configuration.');
  const queue = [], pending = new Map(), pointers = new Map(); let waiter, seen = 0, browserClient, busy = false;
  const origin = 'chrome-extension://' + extensionId;
  const connected = () => !!waiter || Date.now() - seen < 5000;
  function command(op, details = {}) {
    if (!connected()) return Promise.reject(failure('browser_not_connected', 'Load the paired AgentMouse browser extension first.'));
    return new Promise((done, fail) => {
      const id = randomUUID(), timer = setTimeout(() => { pending.delete(id); const index = queue.findIndex(item => item.id === id); if (index >= 0) queue.splice(index, 1); fail(failure('browser_timeout', 'The browser bridge did not respond: ' + (details.method || op))); }, 15000);
      pending.set(id, value => { clearTimeout(timer); value.error ? fail(failure('browser_error', value.error)) : done(value.result); });
      const item = { id, op, ...details };
      if (waiter) { const send = waiter; waiter = null; send(item); } else queue.push(item);
    });
  }
  function pointer(tabId) {
    if (!Number.isSafeInteger(tabId) || tabId <= 0) throw failure('invalid_argument', 'Invalid browser tab ID.');
    if (!pointers.has(tabId)) pointers.set(tabId, new BrowserPointer({ send: (method, params) => { if (!allowed.has(method)) throw failure('unsupported', 'Browser command is not allowed.'); return command('debugger', { tabId, method, params }); }, checkPermission }));
    return pointers.get(tabId);
  }
  async function pause() {
    const results = await Promise.allSettled([...pointers.values()].map(item => item.pause()));
    await command('release').catch(() => {}); await pauseController();
    if (results.some(item => item.status === 'rejected')) throw failure('button_release_unconfirmed', 'At least one browser release was not confirmed.');
    return { armed: false, backgroundButtonHeld: false };
  }
  async function call(body, response) {
    if (body.method === 'status') return { connected: connected(), busy, version };
    if (body.method === 'pause') return pause();
    if (body.method === 'release_capture') { for (const value of pointers.values()) value.observation = null; if (!busy) await command('release').catch(() => {}); return {}; }
    if (body.method === 'list_tabs') return command('tabs');
    if (busy) throw failure('input_busy', 'Another browser gesture is active. Use one controlling agent at a time.');
    const target = pointer(body.tabId);
    if (body.method === 'observe') return target.observe('browser:' + body.tabId);
    if (!['click', 'drag', 'scroll', 'move'].includes(body.method)) throw failure('unsupported', 'Unsupported browser bridge method.');
    busy = true;
    response.once('close', () => { if (!response.writableEnded) target.pause().catch(() => {}); });
    try { return await target.act(body); } finally { busy = false; }
  }
  const server = createServer(async (request, response) => {
    const reply = (status, value) => { if (!response.destroyed) { response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', 'Access-Control-Allow-Origin': origin }); response.end(JSON.stringify(value)); } };
    const requestOrigin = request.headers.origin;
    if (request.headers.host !== '127.0.0.1:' + server.address().port || (requestOrigin && requestOrigin !== origin)) { reply(403, { error: 'Forbidden origin/host' }); return; }
    if (request.method === 'OPTIONS') { response.writeHead(204, { 'Access-Control-Allow-Origin': origin, 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', 'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-AgentMouse-Client' }); response.end(); return; }
    const supplied = Buffer.from(String(request.headers.authorization || '').replace(/^Bearer /, ''));
    if (supplied.length !== token.length || !timingSafeEqual(supplied, Buffer.from(token))) { reply(403, { error: 'Pairing token required' }); return; }
    const url = new URL(request.url, 'http://127.0.0.1');
    try {
      if (request.method === 'GET' && url.pathname === '/health') { reply(200, { version, connected: connected() }); return; }
      if (request.method === 'GET' && url.pathname === '/next') {
        const client = String(request.headers['x-agentmouse-client'] || '');
        if (!/^[a-f0-9-]{36}$/.test(client)) throw failure('invalid_client', 'Missing browser instance identity.');
        if (browserClient && browserClient !== client && connected()) throw failure('browser_busy', 'Only one paired browser profile can connect at a time.');
        if (browserClient && browserClient !== client) {
          await Promise.allSettled([...pointers.values()].map(value => value.pause())); pointers.clear();
          for (const callback of pending.values()) callback({ error: 'Paired browser profile changed' }); pending.clear(); queue.length = 0;
        }
        browserClient = client; seen = Date.now();
        if (waiter) throw failure('browser_busy', 'A browser poll is already active.');
        if (queue.length) { reply(200, queue.shift()); return; }
        const send = value => { clearTimeout(timer); reply(200, value); };
        const timer = setTimeout(() => { if (waiter === send) waiter = null; send({ op: 'idle' }); }, 20000);
        waiter = send; response.once('close', () => { clearTimeout(timer); if (waiter === send) waiter = null; }); return;
      }
      let size = 0; const chunks = [];
      const limit = url.pathname === '/reply' ? 32 * 1024 * 1024 : 128 * 1024;
      for await (const chunk of request) { size += chunk.length; if (size > limit) throw failure('request_too_large', 'Bridge request exceeded its limit.'); chunks.push(chunk); }
      const body = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
      if (request.method === 'POST' && url.pathname === '/reply') { seen = Date.now(); pending.get(body.id)?.(body); pending.delete(body.id); reply(200, { ok: true }); return; }
      if (request.method === 'POST' && url.pathname === '/event') { if (['Input.dragIntercepted', 'AgentMouse.leaseExpired'].includes(body.method)) pointers.get(body.tabId)?.event(body.method, body.params); reply(200, { ok: true }); return; }
      if (request.method === 'POST' && url.pathname === '/call') { reply(200, { ok: true, result: await call(body, response) }); return; }
      reply(404, { error: 'Unknown bridge endpoint' });
    } catch (error) { reply(400, { ok: false, error: { code: error.code || 'bridge_error', message: error.message } }); }
  });
  return { server, async close() { await pause().catch(() => {}); if (waiter) { const send = waiter; waiter = null; send({ op: 'idle' }); } for (const callback of pending.values()) callback({ error: 'Bridge stopped' }); pending.clear(); server.closeAllConnections?.(); await new Promise(done => server.close(done)); } };
}
