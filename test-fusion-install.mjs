import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, dirname, basename } from 'node:path';
import { installAgents } from './scripts/install-lib.mjs';
import { configureFusion, setupFusion } from './scripts/fusion-setup.mjs';
const temporary=await mkdtemp(join(tmpdir(),'agentmouse-fusion-install-'));
try {
  const root=resolve('.'), runtime=join(temporary,'runtime'), home=join(temporary,'home');
  const before=await readFile('mcp.json','utf8');
  const preview=await setupFusion({sourceRoot:root,home,env:{},dryRun:true});assert.equal(preview.engine,'fusion');await assert.rejects(access(preview.directory));
  const installed=await installAgents({agents:['claude-code','codex','zcode','workbuddy','dsh'],sourceRoot:root,runtimeRoot:runtime,home,engine:'fusion'});
  for(const changed of installed.changes.filter(item=>!item.path.includes('SKILL.md')&&!item.path.endsWith('.md'))){const text=await readFile(changed.path,'utf8');assert.match(text,/fusion-entry\.mjs/);}
  await access(join(runtime,'fusion/server.py')); await access(join(runtime,'fusion-worker.mjs'));
  await configureFusion(runtime);
  for(const file of ['mcp.json','.mcp.json','adapters/codex/mcp.json']) assert.ok(JSON.parse(await readFile(join(runtime,file),'utf8')).mcpServers.agentmouse.args.some(arg=>arg.endsWith('fusion-entry.mjs')));
  assert.equal(await readFile('mcp.json','utf8'),before);
  console.log('PASS fusion installer: all five MCP host configs, native-plugin payload entry points, bundled adapter files, dry-run and source preservation; dependency/runtime tests are separate');
} finally {
  assert.equal(dirname(resolve(temporary)),resolve(tmpdir()));assert.ok(basename(temporary).startsWith('agentmouse-fusion-install-'));await rm(temporary,{recursive:true,force:true});
}
