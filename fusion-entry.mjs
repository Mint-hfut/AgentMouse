import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
try {
  const directory = join(process.env.AGENTMOUSE_HOME || join(homedir(), '.agentmouse'), 'fusion');
  const config = JSON.parse(await readFile(join(directory, 'config.json'), 'utf8'));
  const version = JSON.parse(await readFile(join(root, 'package.json'), 'utf8')).version;
  const child = spawn(config.python, ['-B', '-X', 'utf8', join(root, 'fusion/server.py'), '--runtime', root, '--node', process.execPath, '--version', version], { cwd: directory, stdio: 'inherit', windowsHide: true, env: { ...process.env, PYTHONIOENCODING: 'utf-8', ANONYMIZED_TELEMETRY: 'false', WINDOWS_MCP_DISABLE_FLASH: '1', WINDOWS_MCP_MAX_TREE_ELEMENTS: '200' } });
  process.exitCode = await new Promise((done, fail) => { child.once('error', fail); child.once('exit', code => done(code ?? 1)); });
} catch (error) { process.stderr.write('AgentMouse fusion could not start. Run the installer with --engine fusion first. ' + error.message + '\n'); process.exitCode = 1; }
