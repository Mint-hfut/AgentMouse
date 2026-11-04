import { execFile, spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { existsSync } from 'node:fs';

const root = dirname(fileURLToPath(import.meta.url));
const integer = { type: 'integer', minimum: -100000, maximum: 100000 };
const observation = { type: 'string', description: 'Fresh observationId returned by pointer_observe. Single use, valid for 15 seconds.' };
const tools = [
  ['open', 'Open the local pointer app if needed, without requesting foreground activation. New instances start paused; an existing instance keeps its state. The user enables whole-desktop input once in its window; this tool cannot arm input.', {}, []],
  ['status', 'Read arming state, backend and compatibility limits.', {}, []],
  ['list_windows', 'List visible, non-minimized windows.', {}, []],
  ['observe', 'Observe the entire desktop. Optional windowId crops only the screenshot and does NOT restrict subsequent input. Actions use physical screen coordinates. Convert image points using screenX=screenshot.x+imageX*scaleX and screenY=screenshot.y+imageY*scaleY.', { screenshot: { type: 'boolean', default: true }, windowId: { type: 'string', description: 'Optional screenshot crop; never an input binding.' } }, []],
  ['move', 'Move only the Agent overlay. Does not move the system mouse or hover controls.', { x: integer, y: integer }, ['x', 'y']],
  ['click', 'Click anywhere on the desktop: automatically routes to the window under physical screen coordinates, with no app selection or binding. May wait for external input to stop. Delivery is not task success. Reobserve after actions/errors; do not automatically retry.', { observationId: observation, x: integer, y: integer, button: { type: 'string', enum: ['left', 'right'], default: 'left' } }, ['observationId', 'x', 'y']],
  ['drag', 'Experimental touch gesture; requires Touch mode selected by the user. May move the real system pointer and activate the window. Not an independent mouse drag. Reobserve afterward.', { observationId: observation, x: integer, y: integer, toX: integer, toY: integer, durationMs: { type: 'integer', minimum: 100, maximum: 2000, default: 400 } }, ['observationId', 'x', 'y', 'toX', 'toY']],
  ['scroll', 'Scroll the window under desktop screen coordinates, without selecting an app. Negative delta scrolls down, positive up; 120 is one wheel notch. Touch mode uses a swipe of delta pixels. Reobserve afterward.', { observationId: observation, x: integer, y: integer, delta: { type: 'integer', minimum: -1200, maximum: 1200 } }, ['observationId', 'x', 'y', 'delta']],
  ['pause', 'Immediately request input pause. Cannot be used to enable input.', {}, []],
].map(([method, description, properties, required]) => ({
  name: `pointer_${method}`, description,
  inputSchema: { type: 'object', properties, required, additionalProperties: false },
}));

export function createHandler(call) {
  let initialized = false;
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
      return reply({ protocolVersion: versions.includes(requested) ? requested : '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'agentmouse', version: '0.5.1' } });
    }
    if (!initialized) return error(-32000, 'Initialize first');
    if (request.method === 'ping') return reply({});
    if (request.method === 'tools/list') return reply({ tools });
    if (request.method !== 'tools/call') return error(-32601, 'Method not found');
    const tool = tools.find(tool => tool.name === request.params?.name);
    if (!tool) return error(-32602, 'Unknown tool');
    const args = request.params.arguments ?? {};
    if (!args || Array.isArray(args) || typeof args !== 'object') return error(-32602, 'arguments must be an object');
    for (const key of Object.keys(args)) {
      const schema = tool.inputSchema.properties[key];
      if (!schema) return error(-32602, `Unknown argument: ${key}`);
      const value = args[key];
      const validType = schema.type === 'integer' ? Number.isSafeInteger(value) : typeof value === schema.type;
      if (!validType || (schema.enum && !schema.enum.includes(value)) || (schema.minimum !== undefined && value < schema.minimum) || (schema.maximum !== undefined && value > schema.maximum)) return error(-32602, `Invalid argument: ${key}`);
    }
    for (const key of tool.inputSchema.required) if (!Object.hasOwn(args, key)) return error(-32602, `Missing argument: ${key}`);
    try {
      const response = await call({ method: tool.name.slice('pointer_'.length), ...args });
      if (!response.ok) return reply({ isError: true, content: [{ type: 'text', text: JSON.stringify(response.error) }] });
      const value = response.result;
      const screenshot = value?.screenshot;
      if (!screenshot) return reply({ content: [{ type: 'text', text: JSON.stringify(value) }] });
      const { data, ...metadata } = screenshot;
      return reply({ content: [{ type: 'text', text: JSON.stringify({ ...value, screenshot: metadata }) }, { type: 'image', data, mimeType: screenshot.mimeType }] });
    } catch (error) {
      return reply({ isError: true, content: [{ type: 'text', text: `Connection failed; action outcome may be unknown. Reobserve before retrying. ${error.message}` }] });
    }
  };
}

let nativeReady;
export async function callApp(request) {
  if (!nativeReady) nativeReady = Promise.resolve().then(async () => {
    if (!existsSync(join(root, 'dist/AgentMouse.Cli.exe')) || !existsSync(join(root, 'dist/AgentMouse.exe'))) (await import('./scripts/build-native.mjs')).buildNative(root, ['ignore', process.stderr, process.stderr]);
  }).catch(error => { nativeReady = null; throw error; });
  await nativeReady;
  if (request.method === 'open') return openApp();
  return new Promise((resolve, reject) => {
    execFile(join(root, 'dist', 'AgentMouse.Cli.exe'), ['--call', JSON.stringify(request)], { windowsHide: true, timeout: 15000, maxBuffer: 32 * 1024 * 1024 }, (error, stdout) => {
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
  let usedInput = false, closed = false, cleanup;
  const inputMethods = new Set(['move', 'click', 'scroll', 'drag']);
  const handle = createHandler(async request => {
    if (closed && (inputMethods.has(request.method)||request.method==='open')) return { ok: false, error: { code: 'session_closed', message: 'MCP connection is closed.' } };
    if (inputMethods.has(request.method)) usedInput = true;
    const response = await call(request);
    if (request.method === 'pause' && response.ok) usedInput = false;
    return response;
  });
  const lines = createInterface({ input, crlfDelay: Infinity });
  // shortcut: there is one shared desktop controller; run agent control sessions sequentially until input leases are implemented.
  function release() {
    closed = true;
    return cleanup ??= usedInput ? Promise.resolve().then(() => call({ method: 'pause' })).catch(() => {}) : Promise.resolve();
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
  const server = serve();
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { server.release().finally(() => process.exit(0)); });
}
