import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, cp, writeFile, readFile, realpath } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';

export async function setupFusion({ sourceRoot, home = homedir(), env = process.env, dryRun = false }) {
  let directory = resolve(env.AGENTMOUSE_HOME || join(home, '.agentmouse'), 'fusion');
  if (dryRun) return { engine: 'fusion', directory, requires: 'uv; Python 3.14 is installed automatically', dryRun: true };
  let uv = join(home, '.local/bin/uv.exe');
  if (!existsSync(uv)) {
    try { uv = execFileSync('where.exe', ['uv.exe'], { env, encoding: 'utf8', windowsHide: true }).trim().split(/\r?\n/)[0]; }
    catch { throw new Error('Fusion requires uv. Install uv from https://docs.astral.sh/uv/getting-started/installation/ then rerun this installer.'); }
  }
  await mkdir(directory, { recursive: true }); directory = await realpath(directory);
  const project = join(directory, 'windows-mcp');
  await mkdir(project, { recursive: true });
  for (const entry of ['src', 'pyproject.toml', 'uv.lock', 'README.md', 'LICENSE.md']) await cp(join(sourceRoot, 'vendor/windows-mcp', entry), join(project, entry), { recursive: true, filter: file => !/(__pycache__|\.pyc$|\.egg-info)/.test(file) });
  const python = join(project, '.venv/Scripts/python.exe');
  const run = (command, args) => execFileSync(command, args, { cwd: project, env: { ...env, UV_LINK_MODE: 'copy', PYTHONIOENCODING: 'utf-8' }, encoding: 'utf8', windowsHide: true, timeout: 300000, stdio: ['ignore', 'pipe', 'pipe'] });
  for (let attempt = 0; ; attempt++) {
    try { run(uv, ['sync', '--python', '3.14', '--frozen', '--no-dev']); break; }
    catch (error) {
      const message = String(error.stderr || '');
      const wheel = message.match(/Failed to install: ([\w.]+)-([^-\s]+)-[^\s]*\.whl/);
      if (attempt >= 3 || !message.includes('Failed to update Windows PE resources') || !wheel || !existsSync(python)) throw new Error('Windows-MCP dependency setup failed: ' + message.slice(-2000));
      // Repair the same locked wheel with standard pip when uv cannot write a Windows CLI launcher.
      run(python, ['-m', 'ensurepip']);
      run(python, ['-m', 'pip', 'install', '--force-reinstall', '--no-deps', wheel[1] + '==' + wheel[2]]);
    }
  }
  run(python, ['-c', 'import fastmcp, windows_mcp, win32gui; print("OK")']);
  await writeFile(join(directory, 'config.json'), JSON.stringify({ python, project, windowsMcpVersion: '0.8.8' }, null, 2));
  return { engine: 'fusion', directory, python, windowsMcpVersion: '0.8.8', mouse: 'AgentMouse background engine', independentKeyboard: false };
}

export async function configureFusion(root) {
  for (const file of ['mcp.json', '.mcp.json', 'adapters/codex/mcp.json']) {
    const path = join(root, file), config = JSON.parse(await readFile(path, 'utf8'));
    config.mcpServers.agentmouse.args = config.mcpServers.agentmouse.args.map(value => value.replace(/mcp-server\.mjs$/, 'fusion-entry.mjs'));
    await writeFile(path, JSON.stringify(config, null, 2) + '\n');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const sourceRoot = resolve(fileURLToPath(new URL('..', import.meta.url)));
  console.log(JSON.stringify(await setupFusion({ sourceRoot }), null, 2));
}
