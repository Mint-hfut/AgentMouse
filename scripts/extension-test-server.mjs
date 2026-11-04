import { browserRouter } from '../browser-router.mjs';
import { serve, callApp } from '../mcp-server.mjs';
let armed = true;
const pipe = process.argv[2];
const native = request => {
  if (pipe !== 'headless') return callApp(request, pipe);
  if (request.method === 'pause') armed = false;
  return Promise.resolve({ ok: true, result: request.method === 'list_windows' ? [] : { armed, foregroundWindowId: 'headless-test', backgroundWindowCapture: true, windowMessageGestures: true, backgroundButtonHeld: false } });
};
await serve({ call: browserRouter(native) }).finished;
