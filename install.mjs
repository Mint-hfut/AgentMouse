#!/usr/bin/env node
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createInterface } from 'node:readline/promises';

const root = dirname(fileURLToPath(import.meta.url));
try {
  const options = {};
  for (let i = 2; i < process.argv.length; i++) {
    const key = process.argv[i];
    if (key === '--dry-run') options.dryRun = true;
    else if (['--agent', '--home', '--install-dir', '--profile'].includes(key) && process.argv[i + 1] && !process.argv[i + 1].startsWith('--')) options[key.slice(2)] = process.argv[++i];
    else throw new Error('Usage: node install.mjs [--agent claude-code|codex|zcode|workbuddy|dsh|all] [--profile web] [--dry-run] [--home path] [--install-dir path]');
  }
  if (process.platform !== 'win32' || process.arch !== 'x64' || Number(process.versions.node.split('.')[0]) < 18) throw new Error('AgentMouse requires Windows x64 and Node.js 18+.');
  if (!options.agent) {
    if (!process.stdin.isTTY) throw new Error('Use --agent codex (or another supported agent) for unattended installation.');
    const terminal = createInterface({ input: process.stdin, output: process.stdout });
    try {
      console.log('AgentMouse / 选择要接入的 Agent\n1 Claude Code\n2 Codex\n3 ZCode\n4 WorkBuddy\n5 DSH\n6 All / 全部');
      const answer = await terminal.question('Select names or numbers, separated by commas / 输入名称或编号: ');
      options.agent = answer.trim() === '6' ? 'all' : answer.split(',').map(value => ({ 1: 'claude-code', 2: 'codex', 3: 'zcode', 4: 'workbuddy', 5: 'dsh' })[value.trim()] || value.trim()).join(',');
    } finally { terminal.close(); }
  }
  if (!existsSync(join(root, 'node_modules/smol-toml')) || !existsSync(join(root, 'node_modules/yaml'))) {
    if (options.dryRun) throw new Error('Run npm ci --omit=dev --ignore-scripts before a source-checkout dry run. No files were written.');
    const candidates = [process.env.npm_execpath, join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js'), ...String(process.env.PATH || '').split(';').map(path => join(path, 'node_modules/npm/bin/npm-cli.js'))].filter(Boolean);
    const npm = candidates.find(path => existsSync(path));
    if (!npm) throw new Error('npm was not found. Install Node.js with npm, or use the complete release ZIP.');
    execFileSync(process.execPath, [npm, 'ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund'], { cwd: root, stdio: 'inherit', windowsHide: true });
  }
  const { installAgents, normalizeAgents } = await import('./scripts/install-lib.mjs');
  const agents = normalizeAgents(options.agent);
  if (!options.dryRun && (!existsSync(join(root, 'dist/AgentMouse.exe')) || !existsSync(join(root, 'dist/AgentMouse.Cli.exe')))) (await import('./scripts/build-native.mjs')).buildNative(root);
  const home = options.home ? resolve(options.home) : homedir();
  const runtimeRoot = resolve(options['install-dir'] || join(options.home ? home : (process.env.LOCALAPPDATA || join(home, 'AppData/Local')), 'AgentMouse/0.5.0'));
  const result = await installAgents({ agents, sourceRoot: root, runtimeRoot, home, profile: options.profile || 'web', dryRun: !!options.dryRun, env: options.home ? {} : process.env });
  console.log(JSON.stringify(result, null, 2));
  console.log(options.dryRun ? 'Dry run: no settings or runtime files were changed.' : 'Installed. Restart the selected agent(s). Ask it to open AgentMouse, then enable desktop input once in the controller.');
} catch (error) { console.error('AgentMouse: ' + error.message); process.exitCode = 1; }
