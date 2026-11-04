import { execFile, spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { existsSync } from 'node:fs';

const root = dirname(fileURLToPath(import.meta.url));
const integer = { type: 'integer', minimum: -100000, maximum: 100000 };
const observation = { type: 'string', description: 'Fresh observationId returned by pointer_observe. Single use, valid for 15 seconds.' };
const windowAddress = { type: 'string', description: 'Exact intended windowId from pointer_list_windows; must match the observed target. Never substitute the foreground app.' };
const pathPoint = { type: 'object', properties: { x: integer, y: integer }, required: ['x', 'y'], additionalProperties: false };
const tools = [
  ['open', 'Open the local pointer app if needed, without requesting foreground activation. New instances start paused; an existing instance keeps its state. The user enables whole-desktop input once in its window; this tool cannot arm input.', {}, []],
  ['status', 'Read arming state, backend and compatibility limits.', {}, []],
  ['list_windows', 'Read window inventory, including minimized/hidden app windows: HWND, PID, owner, class, physical bounds, foreground, front-to-back zOrder and overlapping windows above. A running tray app may have no listed window; check pointer_list_apps before launching anything.', {}, []],
  ['list_apps', 'Find running app processes by name/title, even when minimized, hidden or in the tray. Check before launching/relaunching. WeChat/Weixin/微信 aliases are supported. Read-only; does not start or activate apps.', { query: { type: 'string' } }, ['query']],
  ['inspect_window', 'Read the named native window accessibility tree (control names, roles and bounds) and window layers. Bounded read-only UIAutomation in an isolated process; never focuses, invokes or types. Some custom apps expose little/no tree. These nodes cannot authorize a mouse action; observe a real image before input.', { windowId: windowAddress }, ['windowId']],
  ['observe', 'Observe a real background window with windowId (defaults to capture=window), or the whole visible desktop without a windowId. Returns target identity/layers and inputAllowed. Native coordinates are physical screen pixels; browser coordinates follow coordinateSpace. Reobserve after input. If inputAllowed=false or the native frame repeats after input, stop: do not click again or switch to the foreground app. Never raise a background window as a fallback.', { screenshot: { type: 'boolean', default: true }, windowId: { type: 'string', description: 'Window ID from pointer_list_windows.' }, capture: { type: 'string', enum: ['desktop', 'window'], default: 'desktop' } }, []],
  ['move', 'Without observationId, move only the Agent overlay. With a browser observationId and matching windowId, move/hover the browser virtual pointer. Neither moves the system mouse. Native window hover is unsupported.', { x: integer, y: integer, observationId: observation, windowId: windowAddress }, ['x', 'y']],
  ['click', 'Click using a fresh observation; capture=window addresses the covered HWND. Background mode supports clickCount=2 for double-click or holdMs for a long press (not both). No foreground activation request or real cursor movement. Reobserve and verify; app compatibility is not guaranteed.', { windowId: windowAddress, observationId: observation, x: integer, y: integer, button: { type: 'string', enum: ['left', 'right'], default: 'left' }, clickCount: { type: 'integer', minimum: 1, maximum: 2, default: 1 }, holdMs: { type: 'integer', minimum: 0, maximum: 2000, default: 0 } }, ['windowId', 'observationId', 'x', 'y']],
  ['drag', 'Background window-message drag inside one window: press, move along the optional intermediate path, then release. Uses physical screen pixels and a fresh observation; does not move the real pointer. Releases on cancellation. No cross-window system drag-and-drop. Touch mode remains experimental, limited to 2 seconds and no waypoints; never use it as a covered-window fallback.', { windowId: windowAddress, observationId: observation, x: integer, y: integer, toX: integer, toY: integer, durationMs: { type: 'integer', minimum: 100, maximum: 5000, default: 400 }, path: { type: 'array', items: pathPoint, maxItems: 128, description: 'Optional intermediate physical screen points between x/y and toX/toY.' } }, ['windowId', 'observationId', 'x', 'y', 'toX', 'toY']],
  ['scroll', 'Wheel at visible desktop screen coordinates. Negative delta scrolls down, positive up; 120 is one notch. Background window observations reject wheels because apps may reroute them using the foreground hit-test. For covered windows use pointer_click on scrollbars/page buttons shown in the background capture. Touch mode is experimental and is not a background fallback.', { windowId: windowAddress, observationId: observation, x: integer, y: integer, delta: { type: 'integer', minimum: -1200, maximum: 1200 } }, ['windowId', 'observationId', 'x', 'y', 'delta']],
  ['pause', 'Immediately request input pause. Cannot be used to enable input.', {}, []],
].map(([method, description, properties, required]) => ({
  name: `pointer_${method}`, description,
  inputSchema: { type: 'object', properties, required, additionalProperties: false },
}));

function validArgument(schema, value) {
  if (schema.type === 'array') return Array.isArray(value) && (schema.maxItems === undefined || value.length <= schema.maxItems) && value.every(item => validArgument(schema.items, item));
  if (schema.type === 'object') return !!value && typeof value === 'object' && !Array.isArray(value) && schema.required.every(key => Object.hasOwn(value, key)) && Object.keys(value).every(key => Object.hasOwn(schema.properties, key) && validArgument(schema.properties[key], value[key]));
  const type = schema.type === 'integer' ? Number.isSafeInteger(value) : typeof value === schema.type;
  return type && (!schema.enum || schema.enum.includes(value)) && (schema.minimum === undefined || value >= schema.minimum) && (schema.maximum === undefined || value <= schema.maximum);
}

export function createHandler(call) {
  let initialized = false;
  const observations = new Map();
  return async function handle(request) {
    const id = request?.id ?? null;
    const error = (code, message) => ({ jsonrpc: '2.0', id, error: { code, message } });
    if (!request || request.jsonrpc !== '2.0' || typeof request.method !== 'string' || Array.isArray(request)) return error(-32600, 'Invalid Request');
    if (!Object.hasOwn(request, 'id')) return null;
    if (typeof id !== 'number' && typeof id !== 'string') return error(-32600, 'Invalid request id');
    const reply = result => ({ jsonrpc: '2.0', id, result });
    if (request.method === 'initialize') {
      if (initialized) return error(-32600, 'Already initialized');
      const versions = ['2024-11-05', '2025-03-26', '2025-06-18', '2025-11-25'];
      const requested = request.params?.protocolVersion;
      if (typeof requested !== 'string') return error(-32602, 'Missing protocolVersion');
      initialized = true;
      return reply({ protocolVersion: versions.includes(requested) ? requested : '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'agentmouse', version: '0.6.0-beta.3' } });
    }
    if (!initialized) return error(-32000, 'Initialize first');
    if (request.method === 'ping') return reply({});
    if (request.method === 'tools/list') return reply({ tools });
    if (request.method !== 'tools/call') return error(-32601, 'Method not found');
    const tool = tools.find(tool => tool.name === request.params?.name);
    if (!tool) return error(-32602, 'Unknown tool');
    let args = request.params.arguments ?? {};
    if (!args || Array.isArray(args) || typeof args !== 'object') return error(-32602, 'arguments must be an object');
    if (!validArgument(tool.inputSchema, args)) return error(-32602, 'Invalid arguments: check required fields, types, coordinate ranges and path length.');
    try {
      if (tool.name === 'pointer_observe') args = { ...args, capture: args.capture ?? (args.windowId ? 'window' : 'desktop') };
      if (['pointer_click','pointer_drag','pointer_scroll'].includes(tool.name)) {
        const observed = observations.get(args.observationId);
        if (observed?.windowId && observed.windowId !== args.windowId) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify({ code: 'wrong_target', message: 'The intended windowId differs from the observed window. No input was sent.' }) }] });
        if (observed?.inputAllowed === false) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify({ code: 'unverified_frame', message: 'No new target frame after previous input. Stop and inspect; do not retry or click the foreground app.' }) }] });
      }
      if (tool.name === 'pointer_observe') observations.clear();
      if (tool.name === 'pointer_drag' || (tool.name === 'pointer_click' && (args.clickCount > 1 || args.holdMs > 0))) {
        const controller = await call({ method: 'status' });
        if (!controller.ok) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify(controller.error) }] });
        if (controller.result?.windowMessageGestures !== true) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify({ code: 'upgrade_required', message: 'Restart the updated controller before using drag/double-click/hold gestures.' }) }] });
      }
      if (tool.name === 'pointer_observe' && args.capture === 'window') {
        const controller = await call({ method: 'status' });
        if (!controller.ok) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify(controller.error) }] });
        if (!args.windowId?.startsWith('browser:') && (controller.result?.windowIdentityGuard !== true || controller.result?.postActionFrameGuard !== true)) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify({ code: 'upgrade_required', message: 'Close the older AgentMouse controller and open beta.3 or newer for window identity/frozen-frame guards.' }) }] });
        if (controller.result?.backgroundWindowCapture !== true) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify({ code: 'upgrade_required', message: 'The running controller does not support real background capture. Close the old AgentMouse controller and reopen the updated version; no desktop-crop fallback was used.' }) }] });
      }
      const response = await call({ method: tool.name.slice('pointer_'.length), ...args });
      if (!response.ok) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify(response.error) }] });
      const value = response.result;
      if (tool.name === 'pointer_observe' && value?.observationId) observations.set(value.observationId, { windowId: value.window?.windowId || args.windowId, inputAllowed: value.inputAllowed });
      const screenshot = value?.screenshot;
      if (tool.name === 'pointer_observe' && args.capture === 'window' && (value?.scope !== 'window' || !['Windows.Graphics.Capture', 'Chrome.Debugger'].includes(screenshot?.captureMethod) || !screenshot?.data)) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify({ code: 'background_capture_missing', message: 'A real background image was required; no screen-crop or image-free fallback is accepted.' }) }] });
      if (!screenshot) return reply({ content: [{ type: 'text', text: JSON.stringify(value) }] });
      const { data, ...metadata } = screenshot;
      return reply({ content: [{ type: 'text', text: JSON.stringify({ ...value, screenshot: metadata }) }, { type: 'image', data, mimeType: screenshot.mimeType }] });
    } catch (error) {
      return reply({ isError: true, content: [{ type: 'text', text: `Connection failed; action outcome may be unknown. Reobserve before retrying. ${error.message}` }] });
    }
  };
}

let nativeReady;
export async function callApp(request, pipe) {
  if (!nativeReady) nativeReady = Promise.resolve().then(async () => {
    if (!existsSync(join(root, 'dist/AgentMouse.Cli.exe')) || !existsSync(join(root, 'dist/AgentMouse.exe'))) (await import('./scripts/build-native.mjs')).buildNative(root, ['ignore', process.stderr, process.stderr]);
  }).catch(error => { nativeReady = null; throw error; });
  await nativeReady;
  if (request.method === 'open') { if (pipe) throw new Error('Opening a GUI is not supported by an isolated fixture transport.'); return openApp(); }
  return new Promise((resolve, reject) => {
    execFile(join(root, 'dist', 'AgentMouse.Cli.exe'), [...(pipe ? ['--pipe', pipe] : []), ['list_windows','list_apps','inspect_window'].includes(request.method) ? '--desktop-query' : '--call', JSON.stringify(request)], { windowsHide: true, timeout: 15000, maxBuffer: 32 * 1024 * 1024 }, (error, stdout) => {
      try { resolve(JSON.parse(stdout.trim())); } catch { reject(new Error(error?.message || 'Invalid app response')); }
    });
  });
}

async function openApp() {
  const existing = await callApp({ method: 'status' });
  if (existing.ok || existing.error?.code !== 'not_running') return existing;
  await new Promise((resolve, reject) => {
    const child = spawn(join(root, 'dist', 'AgentMouse.exe'), [], { detached: true, stdio: 'ignore', windowsHide: false });
    child.once('error', reject);
    child.once('spawn', () => { child.unref(); resolve(); });
  });
  return await callApp({ method: 'status' });
}

export function serve({ input = process.stdin, output = process.stdout, errorOutput = process.stderr, call = callApp } = {}) {
  let usedInput = false, usedCapture = false, closed = false, cleanup;
  const inputMethods = new Set(['move', 'click', 'scroll', 'drag']);
  const handle = createHandler(async request => {
    if (closed && (inputMethods.has(request.method)||request.method==='open'||(request.method==='observe'&&request.capture==='window'))) return { ok: false, error: { code: 'session_closed', message: 'MCP connection is closed.' } };
    if (inputMethods.has(request.method)) usedInput = true;
    if (request.method === 'observe' && request.capture === 'window') usedCapture = true;
    const response = await call(request);
    if (request.method === 'pause' && response.ok) { usedInput = false; usedCapture = false; }
    return response;
  });
  const lines = createInterface({ input, crlfDelay: Infinity });
  // shortcut: there is one shared desktop controller; run agent control sessions sequentially until input leases are implemented.
  function release() {
    closed = true;
    return cleanup ??= usedInput || usedCapture ? Promise.resolve().then(() => call({ method: usedInput ? 'pause' : 'release_capture' })).catch(() => {}) : Promise.resolve();
  }
  let queue = Promise.resolve();
  lines.on('line', line => {
    const respond = async () => {
      let response;
      try {
        if (line.length > 128 * 1024) throw new Error('Request too large');
        response = await handle(JSON.parse(line));
      } catch { response = { jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Invalid JSON or request too large' } }; }
      if (response) output.write(`${JSON.stringify(response)}\n`);
    };
    let priorityPause = false;
    try { const request = JSON.parse(line); priorityPause = request.method === 'tools/call' && request.params?.name === 'pointer_pause'; } catch {}
    if (priorityPause) respond().catch(error => { errorOutput.write(`${error.message}\n`); });
    else queue = queue.then(respond).catch(error => { errorOutput.write(`${error.message}\n`); });
  });
  const finished = new Promise(resolve => { lines.once('close', async () => { await release(); await queue; resolve(); }); });
  return { release, finished };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { browserRouter } = await import('./browser-router.mjs');
  const server = serve({ call: browserRouter(callApp) });
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { server.release().finally(() => process.exit(0)); });
}
