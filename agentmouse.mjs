#!/usr/bin/env node
import { existsSync } from 'node:fs';
import { cp, mkdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { callApp } from './mcp-server.mjs';

export const root = dirname(fileURLToPath(import.meta.url));
export async function prepareHost(host, directory) {
  if (!['dsh', 'workbuddy'].includes(host) || !directory) throw new Error('Usage: prepare dsh|workbuddy <new-directory>');
  const destination=resolve(directory);
  await mkdir(destination); // Refuse to overwrite an existing installation or user directory.
  const files=['mcp-server.mjs','agentmouse.mjs','dist/AgentMouse.exe','dist/AgentMouse.Cli.exe'];
  for(const file of files){const target=join(destination,file);await mkdir(dirname(target),{recursive:true});await cp(join(root,file),target);}
  await cp(join(root,'skills'),join(destination,'skills'),{recursive:true});
  if(host==='workbuddy'){
    await cp(join(root,'adapters/workbuddy/connector-meta.json'),join(destination,'connector-meta.json'));
    await cp(join(root,'assets/icon.svg'),join(destination,'icon.svg'));
    await writeFile(join(destination,'mcp.json'),JSON.stringify(hostConfig(host,destination),null,2));
  }else{
    await writeFile(join(destination,'cordis.patch.yml'),JSON.stringify(hostConfig(host,destination),null,2));
    await writeFile(join(destination,'package.json'),JSON.stringify({name:'dsh-agentmouse',version:'0.5.1',private:true,dsh:{bundle:{patch:'./cordis.patch.yml'}}},null,2));
  }
  await writeFile(join(destination,'setup.mjs'),`import {writeFile,rename} from 'node:fs/promises';\nimport {dirname,join} from 'node:path';\nimport {fileURLToPath} from 'node:url';\nimport {hostConfig} from './agentmouse.mjs';\nconst root=dirname(fileURLToPath(import.meta.url));\nconst file=join(root,${JSON.stringify(host==='dsh'?'cordis.patch.yml':'mcp.json')});\nawait writeFile(file+'.tmp',JSON.stringify(hostConfig(${JSON.stringify(host)},root),null,2));\nawait rename(file+'.tmp',file);\nconsole.log('Updated '+file);\n`);
  return {host,directory:destination,note:'After extraction or relocation, run node setup.mjs in this directory to regenerate local paths.'};
}
export function hostConfig(host, pluginRoot = root, executable = process.execPath) {
  const entry = join(pluginRoot, 'mcp-server.mjs');
  const server = { command: executable, args: [entry] };
  if (host === 'dsh') return [{ insert: [
    { id: 'agentmouse-mcp', name: '@deepseek-ai/dsh-mcp-client', config: { serverName: 'agentmouse', transport: 'stdio', ...server, failOnStartupError: true } },
    { id: 'agentmouse-skills', name: '@deepseek-ai/dsh-skill-filesystem', config: { providerName: 'agentmouse', includeDefaultRoots: false, customSkillDirs: [join(pluginRoot, 'skills')], watch: true } },
  ] }];
  if (host === 'codex') return `[mcp_servers.agentmouse]\ncommand = ${JSON.stringify(executable)}\nargs = [${JSON.stringify(entry)}]\n`;
  if (host === 'zcode') return { mcp: { servers: { agentmouse: server } } };
  if (['claude-code', 'workbuddy', 'mcp'].includes(host)) return { mcpServers: { agentmouse: server } };
  throw new Error('Host must be claude-code, codex, zcode, workbuddy, dsh, or mcp.');
}

export async function run(args) {
  const [command, host] = args;
  if (command === 'prepare') { process.stdout.write(`${JSON.stringify(await prepareHost(host,args[2]),null,2)}\n`); return 0; }
  if (command === 'config') { const value = hostConfig(host); process.stdout.write(typeof value === 'string' ? value : `${JSON.stringify(value, null, 2)}\n`); return 0; }
  if (command === 'doctor') {
    const files = ['mcp-server.mjs', 'dist/AgentMouse.exe', 'dist/AgentMouse.Cli.exe', 'skills/agentmouse/SKILL.md'];
    const missing = files.filter(file => !existsSync(join(root, file)));
    const supported = process.platform === 'win32' && process.arch === 'x64' && Number(process.versions.node.split('.')[0]) >= 18;
    process.stdout.write(`${JSON.stringify({ ready: supported && !missing.length, platform: process.platform, arch: process.arch, node: process.versions.node, missing, root }, null, 2)}\n`);
    return supported && !missing.length ? 0 : 1;
  }
  const method = { start: 'open', status: 'status', stop: 'pause' }[command];
  if (!method) { process.stderr.write('Usage: node agentmouse.mjs start|status|stop|doctor|config <host>|prepare dsh|workbuddy <new-directory>\n'); return 2; }
  const response = await callApp({ method });
  if (command === 'stop' && response.error?.code === 'not_running') { process.stdout.write('{"ok":true,"alreadyStopped":true}\n'); return 0; }
  process.stdout.write(`${JSON.stringify(response)}\n`);
  return response.ok ? 0 : 1;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { process.exitCode = await run(process.argv.slice(2)); }
  catch (error) { process.stderr.write(`${error.message}\n`); process.exitCode = 1; }
}
