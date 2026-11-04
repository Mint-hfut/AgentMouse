import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
const {version}=JSON.parse(await readFile('package.json','utf8'));
for(const [label,edition,entry] of [['Core','core','mcp-server.mjs'],['Full','full','fusion-entry.mjs']]){
  const archive=resolve(`release/AgentMouse-${label}-${version}-windows-x64.zip`);
  const names=execFileSync('tar.exe',['-tf',archive],{encoding:'utf8',windowsHide:true,maxBuffer:8*1024*1024}).trim().split(/\r?\n/);
  const metadata=JSON.parse(execFileSync('tar.exe',['-xOf',archive,'package.json'],{encoding:'utf8',windowsHide:true}));
  assert.equal(metadata.agentmouseEdition,edition);assert.equal(metadata.version,version);
  for(const file of ['mcp.json','.mcp.json','adapters/codex/mcp.json'])assert.ok(JSON.parse(execFileSync('tar.exe',['-xOf',archive,file],{encoding:'utf8',windowsHide:true})).mcpServers.agentmouse.args.some(arg=>arg.endsWith(entry)));
  const fullFiles=names.filter(name=>/^(fusion\/|vendor\/|python-runtime\/|fusion-(entry|worker)\.mjs)/.test(name));
  if(edition==='core'){assert.equal(fullFiles.length,0);assert.ok(!names.some(name=>/\.py$|\.pyd$|python\.exe$/i.test(name)));assert.ok(!Object.keys(metadata.scripts).some(name=>name.includes('fusion')));}
  else{assert.ok(fullFiles.length>1000);for(const file of ['fusion/server.py','vendor/windows-mcp/LICENSE.md','python-runtime/python.exe','python-runtime/python314._pth','python-runtime/LICENSE.txt','python-runtime/RUNTIME.json'])assert.ok(names.includes(file),file);}
}
console.log('PASS edition boundaries: Core contains no Python/Windows-MCP/fusion code; Full contains adapter, isolated runtime and upstream licenses; each package declares its own default MCP entry');
