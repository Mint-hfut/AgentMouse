import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createBrowserBridge } from './bridge-server.mjs';

const stateRoot = process.argv[2];
const config = JSON.parse(await readFile(join(stateRoot, 'config.json'), 'utf8'));
const { callApp } = await import(pathToFileURL(join(config.controllerRoot, 'mcp-server.mjs')).href);
const bridge = createBrowserBridge({ token: config.token, extensionId: config.extensionId, version: config.version,
  checkPermission: async () => { const status = await callApp({ method: 'status' }); if (!status.ok) throw Object.assign(new Error('Open the AgentMouse controller first.'), { code: 'paused' }); return status.result; },
  pauseController: async () => { const result = await callApp({ method: 'pause' }); if (!result.ok && result.error?.code !== 'not_running') throw new Error(result.error.message); }
});
bridge.server.listen(config.port, '127.0.0.1');
bridge.server.on('error', error => { process.stderr.write(error.code + '\n'); process.exitCode = 1; });
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { bridge.close().finally(() => process.exit(0)); });
