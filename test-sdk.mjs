import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const root = dirname(fileURLToPath(import.meta.url));
const transport = new StdioClientTransport({ command: process.execPath, args: [join(root, 'mcp-server.mjs')], cwd: root, stderr: 'pipe', maxBufferSize: 32 * 1024 * 1024 });
const client = new Client({ name: 'agentmouse-official-sdk-check', version: '0.3.0' });
const checks = [];
let reportDirectory=join(root,'compatibility-results');
const reportIndex=process.argv.indexOf('--report-dir');
if(reportIndex>=0)reportDirectory=process.argv[reportIndex+1];
let crossApp=false;
function text(result) { assert.ok(!result.isError, JSON.stringify(result)); return JSON.parse(result.content.find(item => item.type === 'text').text); }
try {
  await client.connect(transport);
  assert.equal(client.getServerVersion().name, 'agentmouse');
  const catalogue = await client.listTools();
  assert.equal(catalogue.tools.length, 9);
  assert.ok(!catalogue.tools.some(tool => tool.name === 'pointer_select_window'));
  checks.push('official SDK initialization and tools/list');
  if (process.argv.includes('--open')) {
    const opened = text(await client.callTool({ name: 'pointer_open', arguments: {} }));
    assert.equal(opened.armed, false);
    checks.push('open local app paused');
    const desktop=text(await client.callTool({name:'pointer_observe',arguments:{screenshot:true}}));
    assert.equal(desktop.scope,'desktop');assert.equal(desktop.window,null);
    assert.equal(desktop.screenshot.sourceWidth,desktop.bounds.width);
    assert.ok(desktop.screenshot.scaleX>=1&&desktop.screenshot.scaleY>=1);
    checks.push('full desktop screenshot without selecting any window');
  }
  const fixtureIndex = process.argv.indexOf('--fixture');
  if (fixtureIndex >= 0) {
    const scenario = JSON.parse(await readFile(process.argv[fixtureIndex + 1], 'utf8'));
    reportDirectory=dirname(process.argv[fixtureIndex+1]);
    const status = text(await client.callTool({ name: 'pointer_status' }));
    assert.equal(status.scope, 'desktop');
    assert.equal(status.armed, true);
    const observationResult = await client.callTool({ name: 'pointer_observe', arguments: { screenshot: true, windowId: scenario.windowId } });
    const observation = text(observationResult);
    assert.equal(observation.scope, 'desktop');
    assert.equal(observation.window.title, scenario.target);
    const png = observationResult.content.find(item => item.type === 'image');
    assert.equal(png.mimeType, 'image/png');
    assert.deepEqual([...Buffer.from(png.data, 'base64').subarray(0, 8)], [137,80,78,71,13,10,26,10]);
    checks.push('real PNG image through MCP and named pipe');
    const moved = text(await client.callTool({ name: 'pointer_move', arguments: { x: scenario.x, y: scenario.y } }));
    assert.equal(moved.x, scenario.x);
    const click = text(await client.callTool({ name: 'pointer_click', arguments: { observationId: observation.observationId, x: scenario.x, y: scenario.y } }));
    assert.equal(click.inputDelivered, true);
    assert.equal(click.focusChanged, false);
    assert.equal(click.cursorChangedWithoutHumanInput, false);
    checks.push('real fixture input through official SDK -> MCP -> CLI -> named pipe');
    if(scenario.second){
      crossApp=true;
      const armed=text(await client.callTool({name:'pointer_status'}));assert.equal(armed.armed,true);
      const fresh=text(await client.callTool({name:'pointer_observe',arguments:{screenshot:false}}));
      assert.equal(fresh.scope,'desktop');assert.equal(fresh.window,null);
      const result=text(await client.callTool({name:'pointer_click',arguments:{observationId:fresh.observationId,x:scenario.second.x,y:scenario.second.y}}));
      assert.equal(result.inputDelivered,true);assert.equal(result.focusChanged,false);assert.equal(result.cursorChangedWithoutHumanInput,false);
      assert.equal(text(await client.callTool({name:'pointer_status'})).armed,true);
      checks.push('one enable crosses Chrome and Notepad without select_window or rearming');
    }
    const paused = text(await client.callTool({ name: 'pointer_pause', arguments: {} }));
    assert.equal(paused.armed, false);
    const fresh = text(await client.callTool({ name: 'pointer_observe', arguments: { screenshot: false } }));
    const blocked = await client.callTool({ name: 'pointer_click', arguments: { observationId: fresh.observationId, x: scenario.x, y: scenario.y } });
    assert.equal(blocked.isError, true);
    assert.equal(JSON.parse(blocked.content[0].text).code, 'paused');
    assert.match(JSON.parse(blocked.content[0].text).message, /暂停/);
    checks.push('pause and paused-action rejection');
  }
  const report = process.argv.includes('--open') ? 'mcp-sdk-open.json' : crossApp ? 'mcp-sdk-cross-app.json' : fixtureIndex >= 0 ? 'mcp-sdk-fixture.json' : 'mcp-sdk.json';
  await mkdir(reportDirectory,{recursive:true});
  await writeFile(join(reportDirectory, report), JSON.stringify({ sdk: '@modelcontextprotocol/sdk', server: client.getServerVersion(), passed: true, checks }, null, 2));
  console.log(`PASS official MCP SDK: ${checks.join('; ')}`);
} finally { await client.close(); }
