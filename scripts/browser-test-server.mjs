import { BrowserPointer } from '../browser-pointer.mjs';
import { serve, callApp } from '../mcp-server.mjs';
import { connectCdp } from './cdp-client.mjs';

const [endpoint, targetId, pipe] = process.argv.slice(2);
const cdp = await connectCdp(endpoint);
const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
let headlessArmed = true;
const controller = request => {
  if (pipe !== 'headless') return callApp(request, pipe);
  if (request.method === 'pause') headlessArmed = false;
  return Promise.resolve({ ok: true, result: { armed: headlessArmed, foregroundWindowId: 'headless-test', backgroundWindowCapture: true, windowMessageGestures: true, backgroundButtonHeld: !!pointer?.held } });
};
const pointer = new BrowserPointer({ send: (method, params) => cdp.send(method, params, sessionId), checkPermission: async () => {
  const status = await controller({ method: 'status' });
  if (!status.ok) throw Object.assign(new Error('Controller is unavailable'), { code: 'paused' });
  return status.result;
} });
cdp.onEvent(event => { if (event.sessionId === sessionId) pointer.event(event.method, event.params); });
async function call(request) {
  try {
    if (request.method === 'observe') return { ok: true, result: await pointer.observe(request.windowId) };
    if (['click', 'drag', 'scroll'].includes(request.method)) return { ok: true, result: await pointer.act(request) };
    if (request.method === 'pause') { await pointer.pause(); return controller(request); }
    if (request.method === 'release_capture') { pointer.observation = null; return { ok: true, result: {} }; }
    return controller(request);
  } catch (error) { return { ok: false, error: { code: error.code || 'browser_error', message: error.message } }; }
}
try { await serve({ call }).finished; } finally { await pointer.pause().catch(() => {}); cdp.close(); }
