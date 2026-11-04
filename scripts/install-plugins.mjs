import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { copyRuntime } from './install-lib.mjs';

export function findClient(agent, env = process.env) {
  const pathDirs = String(env.PATH || '').split(';').filter(Boolean);
  const candidates = agent === 'codex'
    ? [join(env.APPDATA || join(homedir(), 'AppData/Roaming'), 'npm/node_modules/@openai/codex/bin/codex.js'), ...pathDirs.map(dir => join(dir, 'node_modules/@openai/codex/bin/codex.js'))]
    : [join(env.ProgramFiles || 'C:/Program Files', 'ZCode/resources/glm/zcode.cjs'), join(env.LOCALAPPDATA || join(homedir(), 'AppData/Local'), 'Programs/ZCode/resources/glm/zcode.cjs')];
  const entry = candidates.find(file => existsSync(file));
  if (entry) return { executable: process.execPath, args: [entry] };
  try {
    const native = execFileSync('where.exe', [agent + '.exe'], { encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] }).trim().split(/\r?\n/).find(file => existsSync(file));
    if (native) return { executable: native, args: [] };
  } catch {}
  throw new Error(`${agent} plugin CLI was not found. Install its CLI/client, or explicitly choose --mode mcp (MCP + skills only; no plugin card).`);
}

export function installedPlugin(agent, listing) {
  const records = agent === 'codex' ? listing.installed : listing;
  if (!Array.isArray(records)) throw new Error('Unexpected plugin list response from ' + agent);
  return records.find(item => (item.name || item.plugin?.name) === 'agentmouse' || (item.id || item.pluginId || '').startsWith('agentmouse@'));
}

export async function installPlugins({ agents, sourceRoot, runtimeRoot, home = homedir(), env = process.env, dryRun = false, clients, run = execFileSync } = {}) {
  const names = agents.filter(name => ['codex', 'zcode'].includes(name));
  if (!names.length) return [];
  // Resolve every selected client before installing either one; never silently downgrade to MCP-only.
  clients ??= Object.fromEntries(names.map(name => [name, findClient(name, env)]));
  runtimeRoot = resolve(runtimeRoot); home = resolve(home);
  const results = names.map(agent => ({ agent, mode: 'plugin', plugin: 'agentmouse@agentmouse', marketplace: runtimeRoot, dryRun }));
  if (dryRun) return results;
  const pluginRoot = join(runtimeRoot, 'plugin');
  await copyRuntime(sourceRoot, pluginRoot);
  const version = JSON.parse(await readFile(join(sourceRoot, 'plugin.json'), 'utf8')).version;
  const catalog = { name: 'agentmouse', interface: { displayName: 'AgentMouse' }, plugins: [{ name: 'agentmouse', version, source: { source: 'local', path: './plugin' }, policy: { installation: 'AVAILABLE', authentication: 'ON_INSTALL' }, category: 'Productivity' }] };
  await mkdir(join(runtimeRoot, '.agents/plugins'), { recursive: true });
  await writeFile(join(runtimeRoot, '.agents/plugins/marketplace.json'), JSON.stringify(catalog, null, 2));
  await writeFile(join(runtimeRoot, 'marketplace.json'), JSON.stringify({ name: 'agentmouse', plugins: [{ name: 'agentmouse', version, source: './plugin', description: 'Background Windows computer use with an independent Agent pointer.' }] }, null, 2));
  for (const result of results) {
    const { agent } = result;
    const client = clients[agent];
    const clientEnv = { ...env, USERPROFILE: home, HOME: home, ...(agent === 'codex' ? { CODEX_HOME: env.CODEX_HOME || join(home, '.codex') } : { ZCODE_STORAGE_DIR: env.ZCODE_STORAGE_DIR || join(home, '.zcode'), ZCODE_DATA_BASE_DIR: home }) };
    await mkdir(agent === 'codex' ? clientEnv.CODEX_HOME : clientEnv.ZCODE_STORAGE_DIR, { recursive: true });
    const stateFiles = agent === 'codex' ? [join(clientEnv.CODEX_HOME, 'config.toml')] : ['config.json', 'plugins/known_marketplaces.json', 'plugins/installed_plugins.json'].map(file => join(clientEnv.ZCODE_STORAGE_DIR, 'cli', file));
    const backups = [];
    const backupRoot = join(runtimeRoot, 'backups', 'native-' + agent + '-' + Date.now());
    for (const file of stateFiles) {
      if (!existsSync(file)) continue;
      await mkdir(backupRoot, { recursive: true });
      const backup = join(backupRoot, backups.length + '.bak');
      await writeFile(backup, await readFile(file), { mode: 0o600 }); backups.push({ path: file, backup });
    }
    if (backups.length) { await writeFile(join(backupRoot, 'index.json'), JSON.stringify(backups, null, 2), { mode: 0o600 }); result.backupRoot = backupRoot; }
    const call = args => run(client.executable, [...client.args, ...args], { cwd: runtimeRoot, env: clientEnv, encoding: 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 8 * 1024 * 1024 });
    try {
      call(agent === 'codex' ? ['plugin', 'marketplace', 'add', runtimeRoot, '--json'] : ['plugins', 'marketplace', 'add', runtimeRoot]);
      call(agent === 'codex' ? ['plugin', 'add', result.plugin, '--json'] : ['plugins', 'install', result.plugin]);
      if (agent === 'zcode') call(['plugins', 'enable', result.plugin]);
      const listing = JSON.parse(call(agent === 'codex' ? ['plugin', 'list', '--marketplace', 'agentmouse', '--json'] : ['plugins', 'list', '--json']));
      const installed = installedPlugin(agent, listing);
      if (!installed || installed.enabled !== true || (agent === 'codex' && installed.installed !== true) || installed.diagnostics?.some(item => item.severity === 'error')) throw new Error('Plugin was not reported installed/enabled without errors.');
      result.installed = installed;
    } catch (error) {
      throw new Error(`${agent} plugin installation failed: ${error.message}. Some registration steps may have completed; inspect ${agent}'s plugin list. No MCP-only success was reported.`);
    }
  }
  return results;
}
