import { readFile, writeFile, mkdir, cp, realpath } from 'node:fs/promises';
import { createServer } from 'node:net';
import { createHash, randomBytes } from 'node:crypto';
import { join, resolve } from 'node:path';
import { homedir } from 'node:os';

export const browserHome = (home = homedir(), env = process.env) => resolve(env.AGENTMOUSE_HOME || join(home, '.agentmouse'), 'browser');
export async function setupBrowserBridge({ sourceRoot, home = homedir(), env = process.env, dryRun = false } = {}) {
  let stateRoot = browserHome(home, env);
  const manifest = JSON.parse(await readFile(join(sourceRoot, 'browser-extension/manifest.json'), 'utf8'));
  const digest = createHash('sha256').update(Buffer.from(manifest.key, 'base64')).digest('hex').slice(0, 32);
  const extensionId = [...digest].map(value => String.fromCharCode(97 + parseInt(value, 16))).join('');
  if (dryRun) return { stateRoot, extensionDirectory: join(stateRoot, 'extension'), extensionId, manualChromeLoad: true, dryRun: true };
  await mkdir(stateRoot, { recursive: true }); stateRoot = await realpath(stateRoot);
  const configFile = join(stateRoot, 'config.json'); let previous;
  try { previous = JSON.parse(await readFile(configFile, 'utf8')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (previous && (!/^[a-f0-9]{64}$/.test(previous.token) || previous.extensionId !== extensionId || !Number.isInteger(previous.port) || previous.port < 1 || previous.port > 65535)) throw new Error('Existing browser pairing is invalid or belongs to another extension; it was not overwritten.');
  let port = previous?.port;
  if (!port) { const probe = createServer(); await new Promise((done, fail) => { probe.once('error', fail); probe.listen(0, '127.0.0.1', done); }); port = probe.address().port; await new Promise(done => probe.close(done)); }
  const controllerRoot = join(stateRoot, 'controller'); await mkdir(join(controllerRoot, 'dist'), { recursive: true });
  for (const file of ['mcp-server.mjs', 'dist/AgentMouse.exe', 'dist/AgentMouse.Cli.exe']) await cp(join(sourceRoot, file), join(controllerRoot, file));
  for (const file of ['bridge-server.mjs', 'browser-pointer.mjs', 'scripts/bridge-daemon.mjs']) await cp(join(sourceRoot, file), join(stateRoot, file.split('/').at(-1)));
  await cp(join(sourceRoot, 'browser-extension'), join(stateRoot, 'extension'), { recursive: true });
  const config = { version: manifest.version_name || manifest.version, port, token: previous?.token || randomBytes(32).toString('hex'), extensionId, controllerRoot, node: process.execPath };
  await writeFile(configFile, JSON.stringify(config, null, 2) + '\n', { mode: 0o600 });
  await writeFile(join(stateRoot, 'extension/config.js'), `export const BASE_URL = ${JSON.stringify('http://127.0.0.1:' + port)};\nexport const TOKEN = ${JSON.stringify(config.token)};\n`, { mode: 0o600 });
  return { stateRoot, extensionDirectory: join(stateRoot, 'extension'), extensionId, manualChromeLoad: true, note: 'Load this generated directory in Chrome extensions. No browser settings, profiles, or enterprise policies were changed.' };
}
