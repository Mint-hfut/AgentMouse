import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { mkdtemp, cp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { serve } from './mcp-server.mjs';
import { hostConfig, prepareHost } from './agentmouse.mjs';

const metadata = JSON.parse(await readFile(new URL('./package.json', import.meta.url), 'utf8'));
for (const file of ['plugin.json', '.claude-plugin/plugin.json', '.codex-plugin/plugin.json', '.zcode-plugin/plugin.json']) {
  const manifest = JSON.parse(await readFile(new URL(file, import.meta.url), 'utf8'));
  assert.equal(manifest.name, 'agentmouse'); assert.equal(manifest.version, metadata.version);
  if (manifest.mcpServers) assert.ok(JSON.parse(await readFile(new URL(manifest.mcpServers, import.meta.url), 'utf8')).mcpServers.agentmouse);
}
for (const name of ['agentmouse', 'agentmouse-start', 'agentmouse-status', 'agentmouse-stop', 'agentmouse-doctor']) {
  assert.match(await readFile(new URL(`skills/${name}/SKILL.md`, import.meta.url), 'utf8'), new RegExp(`^---\\r?\\nname: ${name}\\r?\\ndescription: .+\\r?\\n---`));
}

const folder = 'C:/A Folder/桌面控制';
for (const host of ['claude-code', 'workbuddy', 'mcp']) {
  const config=hostConfig(host,folder,'C:/Node Runtime/node.exe');
  assert.deepEqual(config.mcpServers.agentmouse.args,[join(folder,'mcp-server.mjs')]);
}
assert.deepEqual(hostConfig('zcode',folder).mcp.servers.agentmouse.args,[join(folder,'mcp-server.mjs')]);
const dsh=hostConfig('dsh',folder);
assert.equal(dsh[0].insert[0].config.serverName,'agentmouse');
assert.deepEqual(dsh[0].insert[1].config.customSkillDirs,[join(folder,'skills')]);
assert.equal(dsh[0].insert[1].config.includeDefaultRoots,false);
assert.match(hostConfig('codex',folder),/^\[mcp_servers\.agentmouse\]/);
assert.throws(()=>hostConfig('unknown'));

async function lifecycle(action,lateOpen=false,capture=false){
  const input=new PassThrough(),output=new PassThrough(),errors=new PassThrough(),calls=[];
  const server=serve({input,output,errorOutput:errors,call:async request=>{calls.push(request.method);return {ok:true,result:request.method==='status'?{windowIdentityGuard:true,targetQuarantine:true,postActionFrameGuard:true,backgroundWindowCapture:true}:request.capture==='window'?{scope:'window',screenshot:{data:'iVBORw==',mimeType:'image/png',captureMethod:'Windows.Graphics.Capture'}}:{}};}});
  let id=0,buffer='';const waiting=new Map();
  output.on('data',chunk=>{buffer+=chunk;let end;while((end=buffer.indexOf('\n'))>=0){const value=JSON.parse(buffer.slice(0,end));buffer=buffer.slice(end+1);waiting.get(value.id)?.(value);waiting.delete(value.id);}});
  function send(method,params){const current=++id;return new Promise(resolve=>{waiting.set(current,resolve);input.write(JSON.stringify({jsonrpc:'2.0',id:current,method,params})+'\n');});}
  await send('initialize',{protocolVersion:'2025-06-18'});
  await send('tools/list');
  if(lateOpen){await server.release();const result=await send('tools/call',{name:'pointer_open',arguments:{}});assert.equal(result.result.isError,true);}
  if(action)await send('tools/call',{name:'pointer_move',arguments:{x:10,y:20}});
  if(capture)await send('tools/call',{name:'pointer_observe',arguments:{capture:'window',windowId:'1234'}});
  input.end();await server.finished;
  assert.deepEqual(calls,action?['move','pause']:capture?['status','observe','release_capture']:[]);
}
await lifecycle(false);
await lifecycle(true);
await lifecycle(false,true);
await lifecycle(false,false,true);

// Verify the distributed CLI/server resolve their own root after relocation, including spaces and Unicode.
const temp=await mkdtemp(join(tmpdir(),'computer-use plugin '));
try{
  const destination=join(temp,'共享插件');
  for(const file of ['mcp-server.mjs','agentmouse.mjs'])await cp(resolve(file),join(destination,file),{recursive:true});
  const run=await new Promise((resolve,reject)=>{
    const child=spawn(process.execPath,[join(destination,'agentmouse.mjs'),'config','mcp'],{windowsHide:true});let output='',error='';
    child.stdout.on('data',chunk=>output+=chunk);child.stderr.on('data',chunk=>error+=chunk);child.on('error',reject);child.on('exit',code=>code===0?resolve(output):reject(new Error(error)));
  });
  assert.deepEqual(JSON.parse(run).mcpServers.agentmouse.args,[join(destination,'mcp-server.mjs')]);
  for(const host of ['dsh','workbuddy']){
    const exported=join(temp,host+' 接入包');await prepareHost(host,exported);
    const config=JSON.parse(await readFile(join(exported,host==='dsh'?'cordis.patch.yml':'mcp.json'),'utf8'));
    const entry=host==='dsh'?config[0].insert[0].config.args[0]:config.mcpServers.agentmouse.args[0];
    assert.equal(entry,join(exported,'mcp-server.mjs'));
    assert.ok((await readFile(join(exported,'skills/agentmouse/SKILL.md'),'utf8')).startsWith('---'));
    await assert.rejects(()=>prepareHost(host,exported),{code:'EEXIST'});
    const moved=join(temp,host+' 移动后');await cp(exported,moved,{recursive:true});
    await new Promise((resolve,reject)=>{const child=spawn(process.execPath,[join(moved,'setup.mjs')],{windowsHide:true,stdio:'ignore'});child.on('error',reject);child.on('exit',code=>code===0?resolve():reject(new Error('Relocation setup failed')));});
    const reset=JSON.parse(await readFile(join(moved,host==='dsh'?'cordis.patch.yml':'mcp.json'),'utf8'));
    assert.equal(host==='dsh'?reset[0].insert[0].config.args[0]:reset.mcpServers.agentmouse.args[0],join(moved,'mcp-server.mjs'));
  }
}finally{
  assert.equal(dirname(resolve(temp)),resolve(tmpdir()));assert.ok(basename(temp).startsWith('computer-use plugin '));
  await rm(temp,{recursive:true,force:true});
}
console.log('PASS five host config adapters, probe-safe disconnect cleanup, and relocated paths with spaces/Unicode');
