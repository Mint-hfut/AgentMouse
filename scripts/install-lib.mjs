import { readFile, writeFile, mkdir, rename, unlink, readdir, realpath, lstat } from 'node:fs/promises';
import { join, dirname, resolve } from 'node:path';
import { homedir } from 'node:os';
import { isDeepStrictEqual } from 'node:util';
import { randomUUID } from 'node:crypto';
import { parse as parseToml, stringify as stringifyToml } from 'smol-toml';
import { parseDocument, isSeq, isMap } from 'yaml';

export const AGENTS = ['claude-code', 'codex', 'zcode', 'workbuddy', 'dsh'];
const BEGIN = '# BEGIN AgentMouse MCP (managed by installer)';
const END = '# END AgentMouse MCP';
const yamlOptions = { customTags: [{ tag: 'tag:yaml.org,2002:js', resolve: value => value }] };
function object(value, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} must be an object; no changes were made.`);
  return value;
}
export function normalizeAgents(value) {
  const aliases = { claude: 'claude-code', 'claude-code': 'claude-code', codex: 'codex', zcode: 'zcode', workbuddy: 'workbuddy', dsh: 'dsh' };
  if (value === 'all') return [...AGENTS];
  const result = [...new Set(String(value).split(',').map(name => aliases[name.trim().toLowerCase()]))];
  if (result.includes(undefined) || !result.length) throw new Error(`Choose ${AGENTS.join(', ')}, or all.`);
  return result;
}
export function mergeJson(text, keys, server) {
  const root = text.trim() ? object(JSON.parse(text.replace(/^\uFEFF/, '')), 'Existing JSON configuration') : {};
  let current = root;
  for (const key of keys) { current[key] ??= {}; current = object(current[key], key); }
  const previous = current.agentmouse === undefined ? {} : object(current.agentmouse, 'Existing AgentMouse entry');
  const entry = { ...previous, ...server };
  if (isDeepStrictEqual(previous, entry)) return text;
  current.agentmouse = entry;
  return JSON.stringify(root, null, 2) + '\n';
}
function withoutAgentMouse(value) {
  const root = { ...value }, servers = { ...(value.mcp_servers ?? {}) }; delete servers.agentmouse;
  if (Object.keys(servers).length) root.mcp_servers = servers; else delete root.mcp_servers;
  return root;
}
export function mergeCodex(text, server) {
  const before = parseToml(text.replace(/^\uFEFF/, ''));
  if (before.mcp_servers !== undefined) object(before.mcp_servers, 'mcp_servers');
  const previous = before.mcp_servers?.agentmouse;
  if (previous !== undefined) object(previous, 'Existing AgentMouse entry');
  const entry = { ...(previous ?? {}), ...server };
  const expected = parseToml(stringifyToml({ entry })).entry;
  if (isDeepStrictEqual(previous, expected)) return text;
  const block = BEGIN + '\n' + stringifyToml({ mcp_servers: { agentmouse: entry } }).trimEnd() + '\n' + END;
  if (previous === undefined) return text.trimEnd() + (text.trim() ? '\n\n' : '') + block + '\n';
  const matches = [...text.matchAll(/^# BEGIN AgentMouse MCP \(managed by installer\)\r?\n[\s\S]*?^# END AgentMouse MCP[^\r\n]*/gm)];
  for (const match of matches) {
    const candidate = text.slice(0, match.index) + block + text.slice(match.index + match[0].length);
    try {
      const parsed = parseToml(candidate);
      if (isDeepStrictEqual(parsed.mcp_servers?.agentmouse, expected) && isDeepStrictEqual(withoutAgentMouse(parsed), withoutAgentMouse(before))) return candidate;
    } catch {}
  }
  throw new Error('An unmanaged AgentMouse TOML entry already exists. Remove that entry or use the manual config command; other settings were not changed.');
}
export function mergeDsh(text, entries) {
  const doc = parseDocument(text.trim() ? text : '[]', yamlOptions);
  if (doc.errors.length || !isSeq(doc.contents)) throw new Error('DSH patch must be a valid YAML list; existing configuration was not changed.');
  const managedIds = new Set(entries.map(entry => entry.id));
  for (const item of doc.contents.items) {
    if (!isMap(item)) continue;
    const inserted = item.get('insert', true);
    if (isSeq(inserted)) inserted.items = inserted.items.filter(node => !isMap(node) || !managedIds.has(node.get('id')));
  }
  // Retain every unrelated YAML node and !!js tag without evaluating expressions.
  doc.contents.items = doc.contents.items.filter(item => !(isMap(item) && item.items.length === 1 && isSeq(item.get('insert', true)) && !item.get('insert', true).items.length));
  doc.contents.flow = false;
  doc.contents.add(doc.createNode({ insert: entries }));
  const output = doc.toString();
  if (parseDocument(output, yamlOptions).errors.length) throw new Error('Generated DSH patch did not validate.');
  return output;
}
export function hostPaths(agent, home, profile = 'web', env = {}) {
  if (!/^[a-zA-Z0-9_-]+$/.test(profile)) throw new Error('Invalid DSH profile name.');
  const paths = {
    'claude-code': { config: join(home, '.claude.json'), skills: join(env.CLAUDE_CONFIG_DIR || join(home, '.claude'), 'skills'), commands: join(env.CLAUDE_CONFIG_DIR || join(home, '.claude'), 'commands', 'agentmouse'), format: 'json', keys: ['mcpServers'] },
    codex: { config: join(env.CODEX_HOME || join(home, '.codex'), 'config.toml'), skills: join(home, '.agents', 'skills'), format: 'toml' },
    zcode: { config: join(home, '.zcode', 'cli', 'config.json'), skills: join(home, '.zcode', 'skills'), commands: join(home, '.zcode', 'commands', 'agentmouse'), format: 'json', keys: ['mcp', 'servers'] },
    workbuddy: { config: join(home, '.workbuddy', 'mcp.json'), skills: join(home, '.codebuddy', 'skills'), commands: join(home, '.codebuddy', 'commands', 'agentmouse'), format: 'json', keys: ['mcpServers'] },
    dsh: { config: join(env.DSH_HOME || join(home, '.dsh'), 'profiles', profile, 'cordis.patch.yml'), skills: join(env.DSH_HOME || join(home, '.dsh'), 'skills'), format: 'yaml' },
  };
  if (!paths[agent]) throw new Error('Unknown agent.');
  return paths[agent];
}
async function optionalRead(path) { try { return await readFile(path); } catch (error) { if (error.code === 'ENOENT') return null; throw error; } }
async function filesUnder(path, prefix = '') {
  const result = [];
  for (const entry of await readdir(join(path, prefix), { withFileTypes: true })) {
    if (entry.isSymbolicLink()) throw new Error('Unexpected symlink in installation payload.');
    const relative = join(prefix, entry.name);
    if (entry.isDirectory()) result.push(...await filesUnder(path, relative)); else if (entry.isFile()) result.push(relative);
  }
  return result;
}
async function atomicWrite(path, bytes) {
  await mkdir(dirname(path), { recursive: true }); const temp = path + '.agentmouse-' + randomUUID() + '.tmp';
  try { await writeFile(temp, bytes, { mode: 0o600, flag: 'wx' }); await rename(temp, path); }
  finally { await unlink(temp).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
}
async function destinationPath(path) {
  try { const info = await lstat(path); return info.isSymbolicLink() ? await realpath(path) : path; }
  catch (error) { if (error.code === 'ENOENT') return path; throw error; }
}
export async function installAgents({ agents, sourceRoot, runtimeRoot, home = homedir(), profile = 'web', dryRun = false, env = process.env, beforeWrite } = {}) {
  const names = normalizeAgents(Array.isArray(agents) ? agents.join(',') : agents);
  sourceRoot = resolve(sourceRoot); runtimeRoot = resolve(runtimeRoot); home = resolve(home);
  const server = { command: process.execPath, args: [join(runtimeRoot, 'mcp-server.mjs')] };
  const changes = [];
  async function stage(path, bytes) {
    path = await destinationPath(path); const before = await optionalRead(path); const after = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes);
    if (before?.equals(after)) return;
    if (changes.some(item => item.path === path)) throw new Error('Duplicate installation destination.');
    changes.push({ path, before, after });
  }
  // Preflight all configurations before copying anything or writing user settings.
  for (const name of names) {
    const paths = hostPaths(name, home, profile, env); const before = await optionalRead(paths.config); const text = before?.toString('utf8') ?? '';
    let output;
    if (paths.format === 'toml') output = mergeCodex(text, server);
    else if (paths.format === 'json') output = mergeJson(text, paths.keys, server);
    else output = mergeDsh(text, [{ id: 'agentmouse-mcp', name: '@deepseek-ai/dsh-mcp-client', config: { serverName: 'agentmouse', transport: 'stdio', ...server, failOnStartupError: true } }]);
    await stage(paths.config, output);
    for (const file of await filesUnder(join(sourceRoot, 'skills'))) await stage(join(paths.skills, file), await readFile(join(sourceRoot, 'skills', file)));
    if (paths.commands) for (const file of await filesUnder(join(sourceRoot, 'commands'))) await stage(join(paths.commands, file), await readFile(join(sourceRoot, 'commands', file)));
  }
  const summary = { agents: names, runtimeRoot, dryRun, changes: changes.map(item => ({ path: item.path, backup: item.before !== null })) };
  if (dryRun) return summary;
  const runtimeFiles = ['LICENSE', 'mcp-server.mjs', 'agentmouse.mjs', 'dist/AgentMouse.exe', 'dist/AgentMouse.Cli.exe'];
  for (const folder of ['skills', 'commands', 'assets', 'adapters']) for (const file of await filesUnder(join(sourceRoot, folder))) runtimeFiles.push(join(folder, file));
  for (const file of runtimeFiles) {
    const bytes = await readFile(join(sourceRoot, file)); const target = join(runtimeRoot, file);
    if (!(await optionalRead(target))?.equals(bytes)) await atomicWrite(target, bytes);
  }
  const backupRoot = join(runtimeRoot, 'backups', new Date().toISOString().replace(/[:.]/g, '-') + '-' + randomUUID().slice(0, 8));
  const applied = [], backups = [];
  try {
    for (let i = 0; i < changes.length; i++) {
      const change = changes[i];
      if (change.before !== null) { const backup = join(backupRoot, `${i}.bak`); await atomicWrite(backup, change.before); backups.push({ path: change.path, backup }); }
      if (beforeWrite) await beforeWrite(change.path);
      if (!isDeepStrictEqual(await optionalRead(change.path), change.before)) throw new Error('Configuration changed during installation: ' + change.path);
      await atomicWrite(change.path, change.after); applied.push(change);
    }
  } catch (error) {
    const rollbackErrors = [];
    for (const change of applied.reverse()) {
      try {
        if (!isDeepStrictEqual(await optionalRead(change.path), change.after)) throw new Error('File was edited externally; kept the newer contents');
        if (change.before === null) await unlink(change.path); else await atomicWrite(change.path, change.before);
      }
      catch (failure) { rollbackErrors.push(`${change.path}: ${failure.message}`); }
    }
    throw new Error(`Installation failed: ${error.message}. ${rollbackErrors.length ? 'Rollback incomplete: ' + rollbackErrors.join('; ') : 'Changed settings were rolled back.'}`);
  }
  if (backups.length) await atomicWrite(join(backupRoot, 'index.json'), JSON.stringify(backups, null, 2));
  return { ...summary, backupRoot: backups.length ? backupRoot : null };
}
