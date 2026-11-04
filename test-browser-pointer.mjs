import assert from 'node:assert/strict';
import { BrowserPointer } from './browser-pointer.mjs';

const header = Buffer.alloc(24); Buffer.from([137,80,78,71,13,10,26,10]).copy(header); header.writeUInt32BE(100,16);header.writeUInt32BE(100,20);
const commands = []; let foreground = 'code', failMove = false;
const pointer = new BrowserPointer({ send: async (method, params) => {
  commands.push({ method, params });
  if (method === 'Page.getLayoutMetrics') return { cssVisualViewport: { clientWidth: 100, clientHeight: 100, pageX: 0, pageY: 0, scale: 1 } };
  if (method === 'Page.captureScreenshot') return { data: header.toString('base64') };
  if (method === 'Input.dispatchMouseEvent' && params.type === 'mouseMoved' && params.buttons === 1 && failMove) throw new Error('fixture move failure');
  return {};
}, checkPermission: async () => ({ armed: true, foregroundWindowId: foreground }) });
let observed = await pointer.observe('browser:1');
assert.equal(observed.screenshot.captureMethod, 'Chrome.Debugger');assert.equal(observed.coordinateSpace, 'browser-viewport-css');
await pointer.act({ method: 'click', observationId: observed.observationId, x: 20, y: 30, clickCount: 2 });
assert.deepEqual(commands.filter(item => item.params.type === 'mousePressed').map(item => item.params.clickCount), [1, 2]);
await assert.rejects(pointer.act({ method: 'click', observationId: observed.observationId, x: 20, y: 30 }), { code: 'stale_observation' });
observed = await pointer.observe('browser:1');
await assert.rejects(pointer.act({ method: 'click', observationId: observed.observationId, x: 101, y: 30 }), { code: 'offscreen' });
observed = await pointer.observe('browser:1');failMove = true;
await assert.rejects(pointer.act({ method: 'drag', observationId: observed.observationId, x: 10, y: 10, toX: 60, toY: 60, durationMs: 100 }), /fixture move failure/);
assert.equal(pointer.held, null);assert.equal(commands.filter(item => item.method === 'Input.dispatchMouseEvent').at(-1).params.type, 'mouseReleased');failMove = false;
observed = await pointer.observe('browser:1');
const drag = pointer.act({ method: 'drag', observationId: observed.observationId, x: 10, y: 10, toX: 60, toY: 60, durationMs: 500 });
while (!pointer.held) await new Promise(done => setTimeout(done, 1));
await pointer.pause();await assert.rejects(drag, { code: 'paused' });assert.equal(pointer.held, null);
assert.ok(commands.some(item => item.method === 'Input.setInterceptDrags' && item.params.enabled));
console.log('PASS browser pointer: pixel coordinates, double-click, single-use tokens, bounds, drag guard and release after failure/pause; transport is mocked here');
