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
const nonce=randomUUID(), cli=resolve(process.env.AGENTMOUSE_TEST_CLI||join(root,'dist/AgentMouse.Cli.exe')), pipe='agentmouse-background-'+nonce;
const qt=process.argv.includes('--qt');
const foreground=()=>JSON.parse(execFileSync(cli,['--foreground-state'],{encoding:'utf8',windowsHide:true}));
const before=foreground();assert.notEqual(before.windowId,'0');
const client=new Client({name:'fusion-windows-acceptance',version:'1'});let fixture,qtFixture;
const value=result=>{assert.ok(!result.isError,JSON.stringify(result));return JSON.parse(result.content.find(item=>item.type==='text').text);};
async function readyLine(child){
  const lines=createInterface({input:child.stdout});
  try{return await new Promise((done,fail)=>{const timer=setTimeout(()=>fail(new Error('Native fixture did not start')),8000);lines.once('line',line=>{clearTimeout(timer);try{done(JSON.parse(line));}catch(error){fail(error);}});child.once('error',fail);});}finally{lines.close();}
}
try{
  let qtReady;
  if(qt){
    assert.ok(process.env.AGENTMOUSE_QT_PYTHON,'Set AGENTMOUSE_QT_PYTHON to a test interpreter with PyQt5==5.15.11');
    qtFixture=spawn(process.env.AGENTMOUSE_QT_PYTHON,[join(root,'scripts/fixtures/qt-window.py'),'APCOMPAT-background-'+nonce,join(temporary,'qt-state.json')],{windowsHide:true,stdio:['pipe','pipe','pipe']});
    qtFixture.stderr.on('data',data=>process.stderr.write(data));qtReady=await readyLine(qtFixture);
  }
  fixture=spawn(cli,qt?['--native-fixture-server','APCOMPAT-background-'+nonce,pipe,String(qtFixture.pid)]:['--fusion-native-fixture',pipe],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  const ready=qt?{...await readyLine(fixture),...qtReady}:await readyLine(fixture);
  assert.equal(ready.foregroundUnchanged,true);assert.ok(ready.windowId);
  await mkdir(join(temporary,'fusion'));await copyFile(join(root,'fusion/controls.py'),join(temporary,'fusion/controls.py'));await copyFile(join(root,'fusion/policy.py'),join(temporary,'fusion/policy.py'));
  let worker=await readFile(join(root,'fusion-worker.mjs'),'utf8');worker=worker.replace("'./mcp-server.mjs'",JSON.stringify(pathToFileURL(join(root,'mcp-server.mjs')).href)).replace("'./browser-router.mjs'",JSON.stringify(pathToFileURL(join(root,'browser-router.mjs')).href)).replace('browserRouter(callApp)','browserRouter(request=>callApp(request,'+JSON.stringify(pipe)+'))');await writeFile(join(temporary,'fusion-worker.mjs'),worker);
  const config=JSON.parse(await readFile(join(process.env.AGENTMOUSE_HOME||join(homedir(),'.agentmouse'),'fusion/config.json'),'utf8'));
  await client.connect(new StdioClientTransport({command:config.python,args:[join(root,'fusion/server.py'),'--runtime',temporary,'--node',process.execPath,'--version','integration-test'],stderr:'pipe',env:{...process.env,AGENTMOUSE_HOME:join(temporary,'state'),ANONYMIZED_TELEMETRY:'false',WINDOWS_MCP_DISABLE_FLASH:'1',PYTHONIOENCODING:'utf-8'},maxBufferSize:32*1024*1024}));
  const image=await client.callTool({name:'Snapshot',arguments:{window_id:ready.windowId,use_ui_tree:!qt}}), observed=value(image);
  if(!qt)assert.equal(observed.controls.source,'Windows-MCP.UIAutomation');
  const button=qt?null:observed.controls.elements.find(node=>node.name==='fusion fixture received 0');if(!qt)assert.ok(button,JSON.stringify(observed.controls));
  const location=qt?{loc:ready.loc}:{label:button.label};
  assert.ok(image.content.some(item=>item.type==='image'));assert.ok(observed.window.overlappingWindowsAbove.length>0);
  const wrong=await client.callTool({name:'Click',arguments:{window_id:'wrong-app',observation_id:observed.observationId,...location}});assert.equal(wrong.isError,true);assert.match(JSON.stringify(wrong.content),/wrong_target/);
  const clicked=value(await client.callTool({name:'Click',arguments:{window_id:ready.windowId,observation_id:observed.observationId,...location}}));assert.equal(clicked.windowId,ready.windowId);assert.equal(clicked.backgroundInterferenceFree,true);
  const stale=await client.callTool({name:'Click',arguments:{window_id:ready.windowId,observation_id:observed.observationId,...location}});assert.equal(stale.isError,true);assert.match(JSON.stringify(stale.content),/stale_observation/);
  const updatedImage=await client.callTool({name:'Snapshot',arguments:{window_id:ready.windowId,use_ui_tree:!qt}}), updated=value(updatedImage);
  if(qt){assert.equal(JSON.parse(await readFile(join(temporary,'qt-state.json'),'utf8')).businessClicks,1);assert.notEqual(clicked.receiverClass,'Static');}
  else assert.ok(updated.controls.elements.some(node=>node.name==='fusion fixture received 1'));
  assert.notEqual(updatedImage.content.find(x=>x.type==='image').data,image.content.find(x=>x.type==='image').data);
  const same=value(await client.callTool({name:'Screenshot',arguments:{window_id:ready.windowId}}));assert.equal(same.screenshot.contentHash,updated.screenshot.contentHash);assert.equal(same.screenshot.frameChanged,false);
  if(!qt){
    await writeFile(join(temporary,'fusion/controls.py'),'import json\nprint(json.dumps({"elements":[{"name":"unsafe_partial","enabled":True}],"providerSucceeded":False,"error":"owned provider failure fixture"}))\n');
    const failed=await client.callTool({name:'WaitFor',arguments:{window_id:ready.windowId,text:'unsafe_partial',timeout:30}});assert.equal(failed.isError,true);assert.match(JSON.stringify(failed.content),/accessibility_unavailable/);
  }
  assert.equal(foreground().windowId,before.windowId);const after=foreground();assert.deepEqual(after.pointer,before.pointer);
  await client.callTool({name:'pointer_pause',arguments:{}});
  console.log(qt?'PASS Qt '+ready.qt+': real QPushButton.clicked exactly once through Full MCP, transparent/layered child skipped, new WGC PNG, wrong-target/stale rejection, foreground and physical cursor unchanged; owned fixture only, no UIA.':'PASS fusion real Windows: Windows-MCP target control labels + real WGC PNG + AgentMouse covered-window input + new control/image receipt, wrong-target and stale-token rejection, foreground and physical pointer unchanged. Own fixture only.');
}finally{
  await client.close();if(fixture){fixture.stdin.end('done\n');await Promise.race([new Promise(done=>fixture.once('exit',done)),new Promise(done=>setTimeout(done,2000))]);if(fixture.pid&&fixture.exitCode===null){fixture.kill();await new Promise(done=>fixture.once('exit',done));}}
  if(qtFixture?.pid&&qtFixture.exitCode===null){qtFixture.kill();await new Promise(done=>qtFixture.once('exit',done));}
  assert.equal(dirname(resolve(temporary)),resolve(tmpdir()));assert.ok(basename(temporary).startsWith('agentmouse-fusion-windows-'));await rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
