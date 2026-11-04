import { createInterface } from 'node:readline';
import { callApp } from './mcp-server.mjs';
import { browserRouter } from './browser-router.mjs';

const call = browserRouter(callApp);
let owned = false, closed = false;
const tasks = new Set();
async function cleanup() { if (closed) return; closed = true; if (owned) await call({ method: 'pause' }); }
const lines = createInterface({ input: process.stdin });
lines.on('line', line => {
  const task = (async () => {
    let id;
    try {
      if (Buffer.byteLength(line) > 128 * 1024) throw new Error('Request too large');
      const request = JSON.parse(line); id = request.id;
      if (closed) throw new Error('Background worker is closed');
      if (['click','drag','move','scroll'].includes(request.method) || (request.method === 'observe' && request.capture === 'window') || (request.method==='open'&&request.allowInput===true)) owned = true;
      const result = await call(request);
      if(closed&&request.method==='open'&&request.allowInput===true){await call({method:'pause'});throw new Error('Session closed during enable; controller was paused.');}
      if (request.method === 'pause' && result.ok) owned = false;
      process.stdout.write(JSON.stringify({ id, ...result }) + '\n');
    } catch (error) { process.stdout.write(JSON.stringify({ id, ok: false, error: { code: 'worker_error', message: error.message } }) + '\n'); }
  })();
  tasks.add(task); task.finally(() => tasks.delete(task));
});
lines.once('close', async () => { await cleanup(); await Promise.allSettled(tasks); });
for (const signal of ['SIGINT','SIGTERM']) process.once(signal, () => cleanup().finally(() => process.exit(0)));
