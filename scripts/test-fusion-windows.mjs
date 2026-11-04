import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, readFile, writeFile, copyFile, rm } from 'node:fs/promises';
import { tmpdir, homedir } from 'node:os';
import { join, resolve, dirname, basename } from 'node:path';
import { pathToFileURL } from 'node:url';
import { randomUUID } from 'node:crypto';
import { createInterface } from 'node:readline';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const root=resolve('.'), temporary=await mkdtemp(join(tmpdir(),'agentmouse-fusion-windows-'));
const cli=join(root,'dist/AgentMouse.Cli.exe'), pipe='agentmouse-background-'+randomUUID();
const foreground=()=>JSON.parse(execFileSync(cli,['--foreground-state'],{encoding:'utf8',windowsHide:true}));
const before=foreground();assert.notEqual(before.windowId,'0');
const client=new Client({name:'fusion-windows-acceptance',version:'1'});let fixture;
const value=result=>{assert.ok(!result.isError,JSON.stringify(result));return JSON.parse(result.content.find(item=>item.type==='text').text);};
try{
  fixture=spawn(cli,['--fusion-native-fixture',pipe],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  const lines=createInterface({input:fixture.stdout});
  const ready=await new Promise((done,fail)=>{const timer=setTimeout(()=>fail(new Error('Native fixture did not start')),8000);lines.once('line',line=>{clearTimeout(timer);try{done(JSON.parse(line));}catch(error){fail(error);}});fixture.once('error',fail);});lines.close();
  assert.equal(ready.foregroundUnchanged,true);assert.ok(ready.windowId);
  await mkdir(join(temporary,'fusion'));await copyFile(join(root,'fusion/controls.py'),join(temporary,'fusion/controls.py'));
  let worker=await readFile(join(root,'fusion-worker.mjs'),'utf8');worker=worker.replace("'./mcp-server.mjs'",JSON.stringify(pathToFileURL(join(root,'mcp-server.mjs')).href)).replace("'./browser-router.mjs'",JSON.stringify(pathToFileURL(join(root,'browser-router.mjs')).href)).replace('browserRouter(callApp)','browserRouter(request=>callApp(request,'+JSON.stringify(pipe)+'))');await writeFile(join(temporary,'fusion-worker.mjs'),worker);
  const config=JSON.parse(await readFile(join(process.env.AGENTMOUSE_HOME||join(homedir(),'.agentmouse'),'fusion/config.json'),'utf8'));
  await client.connect(new StdioClientTransport({command:config.python,args:[join(root,'fusion/server.py'),'--runtime',temporary,'--node',process.execPath,'--version','integration-test'],stderr:'pipe',env:{...process.env,ANONYMIZED_TELEMETRY:'false',WINDOWS_MCP_DISABLE_FLASH:'1',PYTHONIOENCODING:'utf-8'},maxBufferSize:32*1024*1024}));
  const image=await client.callTool({name:'Snapshot',arguments:{window_id:ready.windowId}}), observed=value(image);
  assert.equal(observed.controls.source,'Windows-MCP.UIAutomation');
  const button=observed.controls.elements.find(node=>node.name==='fusion fixture received 0');assert.ok(button,JSON.stringify(observed.controls));
  assert.ok(image.content.some(item=>item.type==='image'));assert.ok(observed.window.overlappingWindowsAbove.length>0);
  const wrong=await client.callTool({name:'Click',arguments:{window_id:'wrong-app',observation_id:observed.observationId,label:button.label}});assert.equal(wrong.isError,true);assert.match(JSON.stringify(wrong.content),/wrong_target/);
  const clicked=value(await client.callTool({name:'Click',arguments:{window_id:ready.windowId,observation_id:observed.observationId,label:button.label}}));assert.equal(clicked.windowId,ready.windowId);assert.equal(clicked.backgroundInterferenceFree,true);
  const stale=await client.callTool({name:'Click',arguments:{window_id:ready.windowId,observation_id:observed.observationId,label:button.label}});assert.equal(stale.isError,true);assert.match(JSON.stringify(stale.content),/stale_observation/);
  const updatedImage=await client.callTool({name:'Snapshot',arguments:{window_id:ready.windowId}}), updated=value(updatedImage);
  assert.ok(updated.controls.elements.some(node=>node.name==='fusion fixture received 1'));assert.notEqual(updatedImage.content.find(x=>x.type==='image').data,image.content.find(x=>x.type==='image').data);
  assert.equal(foreground().windowId,before.windowId);const after=foreground();assert.deepEqual(after.pointer,before.pointer);
  await client.callTool({name:'pointer_pause',arguments:{}});
  console.log('PASS fusion real Windows: Windows-MCP target control labels + real WGC PNG + AgentMouse covered-window input + new control/image receipt, wrong-target and stale-token rejection, foreground and physical pointer unchanged. Own fixture only.');
}finally{
  await client.close();if(fixture){fixture.stdin.end('done\n');await Promise.race([new Promise(done=>fixture.once('exit',done)),new Promise(done=>setTimeout(done,2000))]);}
  assert.equal(dirname(resolve(temporary)),resolve(tmpdir()));assert.ok(basename(temporary).startsWith('agentmouse-fusion-windows-'));await rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
