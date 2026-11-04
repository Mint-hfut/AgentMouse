import { request } from 'node:http';
import { readFile, open } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { join, resolve } from 'node:path';
import { homedir } from 'node:os';

export const bridgeDirectory = (env = process.env) => resolve(env.AGENTMOUSE_HOME || join(homedir(), '.agentmouse'), 'browser');
export async function bridgeConfig(env = process.env) {
  try {
    const directory = bridgeDirectory(env), config = JSON.parse(await readFile(join(directory, 'config.json'), 'utf8'));
    if (!Number.isInteger(config.port) || config.port < 1 || config.port > 65535 || !/^[a-f0-9]{64}$/.test(config.token)) throw new Error('Invalid browser bridge pairing.');
    return { ...config, directory };
  } catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}
export function bridgeRequest(config, path, body) {
  return new Promise((done, fail) => {
    const bytes = body === undefined ? null : Buffer.from(JSON.stringify(body));
    const req = request({ host: '127.0.0.1', port: config.port, path, method: bytes ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + config.token, ...(bytes ? { 'Content-Type': 'application/json', 'Content-Length': bytes.length } : {}) } }, response => {
      const chunks = []; let size = 0;
      response.on('error', fail); response.on('aborted', () => fail(new Error('Browser bridge disconnected')));
      response.on('data', chunk => { size += chunk.length; if (size > 32 * 1024 * 1024) { req.destroy(new Error('Browser response too large')); return; } chunks.push(chunk); });
      response.on('end', () => { try { const result = JSON.parse(Buffer.concat(chunks).toString('utf8')); if (response.statusCode !== 200 || result.ok === false) fail(Object.assign(new Error(result.error?.message || 'Browser bridge error'), { code: result.error?.code || 'browser_error' })); else done(result); } catch (error) { fail(error); } });
    });
    req.setTimeout(20000, () => req.destroy(new Error('Browser bridge timed out'))); req.on('error', fail); if (bytes) req.write(bytes); req.end();
  });
}
let starting;
export async function ensureBridge(config) {
  let health;
  try { health = await bridgeRequest(config, '/health'); } catch {}
  if (health) {
    if (config.version && health.version !== config.version) throw Object.assign(new Error('An older AgentMouse browser service is still running. Restart Windows, then reload the generated extension after upgrading.'), { code: 'upgrade_required' });
    return;
  }
  if (!starting) starting = (async () => {
    const log = await open(join(config.directory, 'bridge.log'), 'a', 0o600);
    try {
      const child = spawn(config.node || process.execPath, [join(config.directory, 'bridge-daemon.mjs'), config.directory], { detached: true, windowsHide: true, stdio: ['ignore', 'ignore', log.fd] });
      await new Promise((done, fail) => { child.once('error', fail); child.once('spawn', () => { child.unref(); done(); }); });
    } finally { await log.close(); }
    for (let i = 0; i < 40; i++) { try { await bridgeRequest(config, '/health'); return; } catch {} await new Promise(done => setTimeout(done, 100)); }
    throw Object.assign(new Error('Browser bridge could not start. Re-run browser-setup or inspect its local log.'), { code: 'browser_unavailable' });
  })().finally(() => { starting = null; });
  return starting;
}
export async function browserCall(body, env = process.env) {
  const config = await bridgeConfig(env);
  if (!config) throw Object.assign(new Error('Run node agentmouse.mjs browser-setup and load the generated Chrome extension.'), { code: 'browser_not_configured' });
  await ensureBridge(config); return (await bridgeRequest(config, '/call', body)).result;
}
