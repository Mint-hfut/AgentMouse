import assert from 'node:assert/strict';
import { mkdtemp, rm, access, readFile } from 'node:fs/promises';
import { join, resolve, dirname, basename } from 'node:path';
import { tmpdir } from 'node:os';
import { installPlugins, installedPlugin } from './scripts/install-plugins.mjs';
const temp = await mkdtemp(join(tmpdir(), 'agentmouse-plugin-test-'));
try {
  const calls = [];
  const clients = { codex: { executable: 'fixture-codex', args: [] }, zcode: { executable: 'fixture-zcode', args: [] } };
  const run = (exe, args, options) => {
    assert.ok(options.env.USERPROFILE.startsWith(temp));
    calls.push({ exe, args });
    if (args.includes('list')) return JSON.stringify(exe.includes('codex') ? { installed: [{ name: 'agentmouse', enabled: true, installed: true }] } : [{ name: 'agentmouse', enabled: true, skillCount: 5, mcpServerNames: ['plugin:agentmouse:agentmouse'], diagnostics: [] }]);
    return '{}';
  };
  const options = { agents: ['codex', 'zcode'], sourceRoot: resolve('.'), runtimeRoot: join(temp, 'runtime'), home: join(temp, 'home'), env: {}, clients, run };
  const preview = await installPlugins({ ...options, dryRun: true });
  assert.equal(preview.length, 2); assert.equal(calls.length, 0);
  await assert.rejects(access(options.runtimeRoot), { code: 'ENOENT' });
  const result = await installPlugins(options);
  assert.ok(result.every(item => item.installed.enabled));
  assert.ok(calls.some(item => item.exe === 'fixture-codex' && item.args.includes('add') && item.args.includes('agentmouse@agentmouse')));
  assert.ok(calls.some(item => item.exe === 'fixture-zcode' && item.args.includes('install') && item.args.includes('agentmouse@agentmouse')));
  const catalog = JSON.parse(await readFile(join(options.runtimeRoot, '.agents/plugins/marketplace.json'), 'utf8'));
  assert.equal(catalog.plugins[0].source.path, './plugin');
  assert.ok(!catalog.plugins[0].source.path.includes('backups'));
  await access(join(options.runtimeRoot, 'plugin/.zcode-plugin/plugin.json'));
  assert.equal(installedPlugin('codex', { installed: [] }), undefined);
  await assert.rejects(installPlugins({ ...options, run: () => { throw new Error('fixture install failed'); } }), /No MCP-only success/);
  await assert.rejects(installPlugins({ ...options, agents: ['codex'], run: (_exe, args) => args.includes('list') ? '{"installed":[{"name":"agentmouse","enabled":false,"installed":true}]}' : '{}' }), /not reported installed/);
  console.log('PASS native plugin installer: catalog, host commands, enabled-state verification, dry run and explicit failure; real client acceptance is separate');
} finally {
  assert.equal(dirname(resolve(temp)), resolve(tmpdir())); assert.ok(basename(temp).startsWith('agentmouse-plugin-test-'));
  await rm(temp, { recursive: true, force: true });
}
