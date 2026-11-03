import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, writeFile, access, rm } from 'node:fs/promises';
import { join, dirname, resolve, basename } from 'node:path';
import { tmpdir } from 'node:os';
import { execFileSync } from 'node:child_process';
import { parse as parseToml } from 'smol-toml';
import { parseDocument } from 'yaml';
import { AGENTS, hostPaths, normalizeAgents, mergeJson, mergeCodex, mergeDsh, installAgents } from './scripts/install-lib.mjs';

const temp = await mkdtemp(join(tmpdir(), 'agentmouse-installer-'));
const sourceRoot = resolve('.');
async function put(file, text) { await mkdir(dirname(file), { recursive: true }); await writeFile(file, text); }
async function missing(file) { await assert.rejects(access(file), { code: 'ENOENT' }); }
const server = { command: 'C:\\Node Runtime\\node.exe', args: ['C:\\用户\\AgentMouse\\mcp-server.mjs'] };
let checks = 0;
try {
  assert.deepEqual(normalizeAgents('claude,codex,claude-code'), ['claude-code', 'codex']);
  assert.deepEqual(normalizeAgents('all'), AGENTS);
  assert.throws(() => normalizeAgents('unknown'));
  assert.throws(() => hostPaths('dsh', temp, '../private'));
  const custom = hostPaths('codex', temp, 'web', { CODEX_HOME: join(temp, 'custom') });
  assert.equal(custom.config, join(temp, 'custom/config.toml')); checks++;

  const json = '{"mcpServers":{"existing":{"command":"keep"}},"apiKey":"fixture-only"}';
  const mergedJson = mergeJson(json, ['mcpServers'], server);
  assert.equal(JSON.parse(mergedJson).mcpServers.existing.command, 'keep');
  assert.equal(JSON.parse(mergedJson).apiKey, 'fixture-only');
  assert.equal(mergeJson(mergedJson, ['mcpServers'], server), mergedJson);
  assert.throws(() => mergeJson('{broken', ['mcpServers'], server));
  assert.throws(() => mergeJson('{"mcpServers":[]}', ['mcpServers'], server)); checks++;

  const toml = '# keep comment\nmodel = "fixture"\n[mcp_servers.existing]\ncommand="keep"\n';
  const mergedToml = mergeCodex(toml, server);
  assert.equal(parseToml(mergedToml).model, 'fixture');
  assert.equal(parseToml(mergedToml).mcp_servers.existing.command, 'keep');
  assert.equal(mergeCodex(mergedToml, server), mergedToml);
  assert.ok(mergedToml.startsWith(toml.trimEnd()));
  const updatedToml = mergeCodex(mergedToml, { ...server, args: ['new path'] });
  assert.deepEqual(parseToml(updatedToml).mcp_servers.agentmouse.args, ['new path']);
  assert.throws(() => mergeCodex('[mcp_servers.agentmouse]\ncommand="custom"\n', server), /unmanaged/);
  const embedded = 'prompt = """\n# BEGIN AgentMouse MCP (managed by installer)\nhello\n# END AgentMouse MCP\n"""\n';
  const embeddedMerged = mergeCodex(embedded, server);
  const embeddedUpdated = mergeCodex(embeddedMerged, { ...server, args: ['updated'] });
  assert.equal(parseToml(embeddedUpdated).prompt, parseToml(embedded).prompt); checks++;

  const yaml = '# retain comment\n- modify: existing\n  config:\n    provider: !!js process.env.PROVIDER\n- insert:\n    - id: keep-plugin\n      name: unrelated\n';
  const entries = [{ id: 'agentmouse-mcp', name: 'test', config: server }];
  const mergedYaml = mergeDsh(yaml, entries);
  assert.match(mergedYaml, /retain comment/); assert.match(mergedYaml, /!!js process.env.PROVIDER/); assert.match(mergedYaml, /keep-plugin/);
  assert.equal(mergeDsh(mergedYaml, entries), mergedYaml);
  assert.equal(parseDocument(mergedYaml, { customTags: [{ tag: 'tag:yaml.org,2002:js', resolve: value => value }] }).errors.length, 0);
  assert.throws(() => mergeDsh('not: a-list', entries)); checks++;

  // Every integration uses a throwaway home; never read or mutate the real user's host configs.
  const home = join(temp, '用户 home'), runtimeRoot = join(temp, '持久 runtime');
  await put(hostPaths('claude-code', home).config, json);
  await put(hostPaths('codex', home).config, toml);
  await put(hostPaths('zcode', home).config, '{"mcp":{"servers":{"keep":{"command":"x"}}},"theme":"dark"}');
  await put(hostPaths('workbuddy', home).config, json);
  await put(hostPaths('dsh', home).config, yaml);
  const originals = new Map(await Promise.all(AGENTS.map(async name => { const file = hostPaths(name, home).config; return [file, await readFile(file)]; })));
  const options = { agents: 'all', sourceRoot, runtimeRoot, home, env: {} };
  const preview = await installAgents({ ...options, dryRun: true });
  assert.equal(preview.agents.length, 5); await missing(runtimeRoot);
  for (const [file, bytes] of originals) assert.deepEqual(await readFile(file), bytes); checks++;

  const installed = await installAgents(options);
  assert.equal(installed.agents.length, 5); assert.ok(installed.backupRoot);
  const backups = JSON.parse(await readFile(join(installed.backupRoot, 'index.json'), 'utf8'));
  assert.equal(backups.length, 5);
  for (const { path, backup } of backups) assert.deepEqual(await readFile(backup), originals.get(path));
  for (const name of AGENTS) assert.match(await readFile(join(hostPaths(name, home).skills, 'agentmouse/SKILL.md'), 'utf8'), /name: agentmouse/);
  assert.equal(JSON.parse(await readFile(hostPaths('zcode', home).config, 'utf8')).mcp.servers.agentmouse.command, process.execPath);
  assert.equal(parseToml(await readFile(hostPaths('codex', home).config, 'utf8')).mcp_servers.agentmouse.args[0], join(runtimeRoot, 'mcp-server.mjs'));
  const again = await installAgents(options);
  assert.equal(again.changes.length, 0); assert.equal(again.backupRoot, null); checks++;

  const singleHome = join(temp, 'single-home');
  await installAgents({ ...options, agents: 'codex', home: singleHome });
  await missing(join(singleHome, '.claude.json')); await missing(join(singleHome, '.workbuddy'));
  await access(join(singleHome, '.agents/skills/agentmouse/SKILL.md')); checks++;

  const brokenHome = join(temp, 'broken-home'), brokenRuntime = join(temp, 'broken-runtime');
  await put(hostPaths('workbuddy', brokenHome).config, '{invalid');
  await assert.rejects(installAgents({ ...options, home: brokenHome, runtimeRoot: brokenRuntime }));
  await missing(hostPaths('claude-code', brokenHome).config); await missing(brokenRuntime); checks++;

  const rollbackHome = join(temp, 'rollback-home');
  await put(hostPaths('claude-code', rollbackHome).config, json);
  let count = 0;
  await assert.rejects(installAgents({ ...options, home: rollbackHome, beforeWrite() { if (++count === 3) throw new Error('injected write failure'); } }), /rolled back/);
  assert.equal(await readFile(hostPaths('claude-code', rollbackHome).config, 'utf8'), json);
  await missing(join(hostPaths('claude-code', rollbackHome).skills, 'agentmouse/SKILL.md')); checks++;

  const conflictHome = join(temp, 'concurrent-home');
  await put(hostPaths('claude-code', conflictHome).config, json);
  await assert.rejects(installAgents({ ...options, agents: 'claude-code', home: conflictHome, async beforeWrite(file) { await writeFile(file, 'changed by user'); } }), /changed during installation/);
  assert.equal(await readFile(hostPaths('claude-code', conflictHome).config, 'utf8'), 'changed by user'); checks++;
  const cliHome = join(temp, 'cli 用户'), cliRuntime = join(temp, 'cli runtime');
  const cliArgs = [join(sourceRoot, 'install.mjs'), '--agent', 'codex', '--home', cliHome, '--install-dir', cliRuntime];
  execFileSync(process.execPath, [...cliArgs, '--dry-run'], { windowsHide: true });
  await missing(cliHome); await missing(cliRuntime);
  execFileSync(process.execPath, cliArgs, { windowsHide: true });
  const cliAgain = execFileSync(process.execPath, cliArgs, { windowsHide: true, encoding: 'utf8' });
  assert.match(cliAgain, /"changes": \[\]/);
  assert.equal(parseToml(await readFile(hostPaths('codex', cliHome).config, 'utf8')).mcp_servers.agentmouse.args[0], join(cliRuntime, 'mcp-server.mjs')); checks++;
  console.log(`PASS installer: ${checks} groups; all five host layouts, preservation, backups, repeat install, dry run, rollback, concurrent edit detection, Unicode paths`);
} finally {
  assert.equal(dirname(resolve(temp)), resolve(tmpdir())); assert.ok(basename(temp).startsWith('agentmouse-installer-'));
  await rm(temp, { recursive: true, force: true });
}
