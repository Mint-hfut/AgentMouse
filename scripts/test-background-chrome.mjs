import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { mkdtemp, readFile, mkdir, writeFile, rm, cp } from 'node:fs/promises';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, resolve, dirname, basename } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { randomUUID, randomBytes, createHash } from 'node:crypto';
import { createInterface } from 'node:readline';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { serve, callApp } from '../mcp-server.mjs';
import { createBrowserBridge } from '../bridge-server.mjs';
import { bridgeRequest } from '../bridge-client.mjs';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
if (process.argv[2] === '--serve') {
  await serve({ call: request => callApp(request, process.argv[3]) }).finished;
} else {
  const headless = process.argv.includes('--headless-test');
  const extensionTest = process.argv.includes('--extension-bridge-test');
  if (typeof WebSocket !== 'function') throw new Error('This optional Chrome harness requires Node 22+ (the product still supports Node 18).');
  const temp = await mkdtemp(join(tmpdir(), 'agentmouse-background-'));
  const cli = join(root, 'dist/AgentMouse.Cli.exe');
  const native = args => JSON.parse(execFileSync(cli, args, { encoding: 'utf8', windowsHide: true, maxBuffer: 32 * 1024 * 1024 }));
  const before = headless ? { windowId: 'headless-test', process: 'headless-test', bounds: { x: 0, y: 0, width: 1000, height: 800 }, dpiScale: 1 } : native(['--foreground-state']);
  const foreground = () => headless ? before : native(['--foreground-state']);
  if (!headless && (before.windowId === '0' || before.dpiScale <= 0)) throw new Error('An interactive desktop with a foreground window is required for covered-window acceptance. Use --headless-test only for separate component validation.');
  const marker = 'background-' + randomUUID(), pipe = 'agentmouse-' + marker;
  const output = join(root, 'compatibility-results', marker);
  const nativeMessages = process.argv.includes('--native-messages');
  const compatibilityFlags = nativeMessages && !process.argv.includes('--stock-chrome') ? ['--disable-backgrounding-occluded-windows','--disable-renderer-backgrounding','--disable-background-timer-throttling','--disable-gpu','--disable-features=CalculateNativeWinOcclusion,ApplyNativeOcclusionToCompositor'] : [];
  let chrome, socket, fixture, client, extensionBridge, labServer, lastReport, testConfig, extensionFolder;
  const pending = new Map(); let sequence = 0;
  const delay = ms => new Promise(done => setTimeout(done, ms));
  const waitFor = async test => { for (let i = 0; i < 300; i++) { const value = await test(); if (value) return value; await delay(100); } throw new Error('Fixture startup timed out'); };
  function send(method, params = {}, sessionId) {
    return new Promise((done, fail) => {
      const id = ++sequence, timer = setTimeout(() => { pending.delete(id); fail(new Error('CDP timed out: ' + method)); }, 10000);
      pending.set(id, value => { clearTimeout(timer); value.error ? fail(new Error(JSON.stringify(value.error))) : done(value.result); });
      socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
  }
  try {
    let fixtureUrl = pathToFileURL(join(root, 'browser-fixture.html')).href + '?id=' + marker;
    if (extensionTest) {
      const html = await readFile(join(root, 'browser-fixture.html'));
      labServer = createServer(async (request, response) => { if (request.method === 'POST' && request.url === '/report') { const chunks=[];for await(const chunk of request)chunks.push(chunk);lastReport=JSON.parse(Buffer.concat(chunks));response.end('ok'); } else { response.setHeader('Content-Type','text/html; charset=utf-8');response.end(html); } });
      await new Promise(done=>labServer.listen(0,'127.0.0.1',done));fixtureUrl='http://127.0.0.1:'+labServer.address().port+'/?id='+marker+'&report=1';
      const manifest=JSON.parse(await readFile(join(root,'browser-extension/manifest.json'),'utf8'));
      const extensionId=[...createHash('sha256').update(Buffer.from(manifest.key,'base64')).digest('hex').slice(0,32)].map(value=>String.fromCharCode(97+parseInt(value,16))).join('');
      const token=randomBytes(32).toString('hex');let armed=true;
      extensionBridge=createBrowserBridge({token,extensionId,checkPermission:async()=>headless?{armed,foregroundWindowId:'headless-test'}:(await callApp({method:'status'},pipe)).result,pauseController:async()=>{armed=false;if(!headless)await callApp({method:'pause'},pipe);}});
      await new Promise(done=>extensionBridge.server.listen(0,'127.0.0.1',done));testConfig={token,port:extensionBridge.server.address().port};
      extensionFolder=join(temp,'extension');await cp(join(root,'browser-extension'),extensionFolder,{recursive:true});
      await writeFile(join(extensionFolder,'config.js'),`export const BASE_URL=${JSON.stringify('http://127.0.0.1:'+testConfig.port)};\nexport const TOKEN=${JSON.stringify(token)};\n`);
      await mkdir(join(temp,'state/browser'),{recursive:true});await writeFile(join(temp,'state/browser/config.json'),JSON.stringify(testConfig));
    }
    chrome = spawn(process.env.AGENTMOUSE_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', ['--user-data-dir=' + temp, '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', '--no-first-run', '--no-default-browser-check', '--no-startup-window', ...(headless ? ['--headless=new'] : []), ...(extensionTest?['--load-extension='+extensionFolder,'--disable-extensions-except='+extensionFolder]:[]), ...compatibilityFlags], { windowsHide: true, stdio: 'ignore' });
    let launchError; chrome.on('error', error => { launchError = error; });
    const portFile = await waitFor(async () => { if (launchError) throw launchError; try { return await readFile(join(temp, 'DevToolsActivePort'), 'utf8'); } catch { return null; } });
    const [port, endpoint] = portFile.trim().split(/\r?\n/);
    socket = new WebSocket('ws://127.0.0.1:' + port + endpoint);
    await new Promise((done, fail) => { socket.addEventListener('open', done, { once: true }); socket.addEventListener('error', fail, { once: true }); });
    socket.addEventListener('message', event => { const value = JSON.parse(event.data); pending.get(value.id)?.(value); pending.delete(value.id); });
    const browser = await send('Browser.getVersion'); console.log(JSON.stringify({ browser, compatibilityFlags }));
    const dpi = before.dpiScale;
    const { targetId } = await send('Target.createTarget', { url: fixtureUrl, newWindow: true, background: true, left: Math.ceil((before.bounds.x + 32) / dpi), top: Math.ceil((before.bounds.y + 48) / dpi), width: Math.floor(Math.min(900, before.bounds.width - 64) / dpi), height: Math.floor(Math.min(650, before.bounds.height - 96) / dpi) });
    assert.equal(foreground().windowId, before.windowId, 'Chrome setup activated its window; no input test was run');
    const sessionId = extensionTest ? null : (await send('Target.attachToTarget', { targetId, flatten: true })).sessionId;
    if (extensionTest) {
      const setupSession=(await send('Target.attachToTarget',{targetId,flatten:true})).sessionId;
      await send('Runtime.runIfWaitingForDebugger',{},setupSession);
      const navigation=await send('Page.navigate',{url:fixtureUrl},setupSession);
      if(navigation.errorText)throw new Error('Fixture navigation failed: '+navigation.errorText);
      await send('Target.detachFromTarget',{sessionId:setupSession});
    }
    try { await waitFor(async () => { const info = await send('Target.getTargetInfo', { targetId }); return info.targetInfo.title.startsWith('APCOMPAT-' + marker) ? info : null; }); }
    catch(error){
      console.log(JSON.stringify({startupTarget:await send('Target.getTargetInfo',{targetId})}));
      const diagnostic=(await send('Target.attachToTarget',{targetId,flatten:true})).sessionId;
      console.log(JSON.stringify({startupDocument:await send('Runtime.evaluate',{expression:'JSON.stringify({url:location.href,ready:document.readyState,text:document.body?.innerText.slice(0,1000)})',returnByValue:true},diagnostic)}));throw error;
    }
    await delay(500);
    let ready = { windowId: 'browser:' + targetId };
    if (!headless) {
      fixture = spawn(cli, ['--background-fixture-server', 'APCOMPAT-' + marker, pipe], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      const lines = createInterface({ input: fixture.stdout });
      ready = await new Promise((done, fail) => { const timer = setTimeout(() => fail(new Error('Fixture controller did not start')), 10000); lines.once('line', line => { clearTimeout(timer); try { done(JSON.parse(line)); } catch (error) { fail(error); } }); fixture.once('error', fail); }); lines.close();
    }
    if(extensionTest){
      await waitFor(async()=> (await bridgeRequest(testConfig,'/health')).connected);
      const tabs=(await bridgeRequest(testConfig,'/call',{method:'list_tabs'})).result;
      const tab=tabs.find(tab=>tab.title.startsWith('APCOMPAT-'+marker));assert.ok(tab,'The real extension did not list the fixture tab');ready.windowId='browser:'+tab.id;
    }
    client = new Client({ name: 'agentmouse-background-chrome', version: '0.6.0-beta.1' });
    const serverArgs = extensionTest ? [join(root,'scripts/extension-test-server.mjs'),headless?'headless':pipe] : nativeMessages ? [fileURLToPath(import.meta.url), '--serve', pipe] : [join(root, 'scripts/browser-test-server.mjs'), 'ws://127.0.0.1:' + port + endpoint, targetId, headless ? 'headless' : pipe];
    await client.connect(new StdioClientTransport({ command: process.execPath, args: serverArgs, cwd: root, env:{...process.env,...(extensionTest?{AGENTMOUSE_HOME:join(temp,'state')}:{})},stderr: 'pipe', maxBufferSize: 32 * 1024 * 1024 }));
    const text = result => { assert.ok(!result.isError, JSON.stringify(result)); return JSON.parse(result.content.find(item => item.type === 'text').text); };
    async function observe(name) {
      const result = await client.callTool({ name: 'pointer_observe', arguments: { windowId: ready.windowId, capture: 'window' } });
      const value = text(result); assert.equal(value.scope, 'window'); assert.equal(value.screenshot.captureMethod, nativeMessages ? 'Windows.Graphics.Capture' : 'Chrome.Debugger');
      const file = join(output, name + '.png'); await mkdir(output, { recursive: true }); await writeFile(file, Buffer.from(result.content.find(item => item.type === 'image').data, 'base64'));
      return { value, file };
    }
    const first = await observe('before');
    if (!headless) {
      const probe = native(['--probe-background-capture', 'chrome', output, 'APCOMPAT-' + marker]);
      assert.equal(probe.background, true); assert.equal(probe.centerCovered, true, 'Fixture is not actually covered by the foreground window');
      assert.equal(probe.foregroundUnchanged, true); assert.equal(probe.fullyCoveredByForeground, true, 'Chrome is not fully behind the original foreground window');
    }
    function point(observation, rgb) {
      const pixel = native(['--image-color-point', observation.file, rgb]); const image = observation.value.screenshot;
      return { x: Math.round(image.x + pixel.x * image.scaleX), y: Math.round(image.y + pixel.y * image.scaleY) };
    }
    const checks = [];
    const captureTime = image => image.frameTimestamp100ns ?? image.captureTimestampMs;
    for (const [name, button] of [['left', 'left'], ['right', 'right'], [nativeMessages ? 'scrollbar' : 'wheel', null]]) {
      let observed = name === 'left' ? first : await observe(name + '-before');
      let coordinates = point(observed, button ? '237,108,2' : '41,121,255');
      if (!button && nativeMessages) {
        const blocked = await client.callTool({ name: 'pointer_scroll', arguments: { observationId: observed.value.observationId, ...coordinates, delta: -120 } });
        assert.equal(blocked.isError, true); assert.equal(JSON.parse(blocked.content[0].text).code, 'unsupported');
        observed = await observe('scrollbar-fresh');
        const pane = native(['--image-color-point', observed.file, '41,121,255']), image = observed.value.screenshot;
        coordinates = { x: Math.round(image.x + (pane.maxX + 10) * image.scaleX), y: Math.round(image.y + (pane.maxY - 8) * image.scaleY) };
      }
      const result = text(await client.callTool({ name: !button && !nativeMessages ? 'pointer_scroll' : 'pointer_click', arguments: { observationId: observed.value.observationId, ...coordinates, ...(!button && !nativeMessages ? { delta: -120 } : { button: button || 'left' }) } }));
      console.log(JSON.stringify({ delivery: name, result }));
      assert.equal(result.backgroundTarget, true); assert.equal(result.backgroundInterferenceFree, true); assert.equal(result.focusChanged, false); assert.equal(result.cursorChangedWithoutHumanInput, false);
      if (nativeMessages) assert.equal(result.targetOccludedAtPoint, true);
      assert.equal(foreground().windowId, before.windowId);
      await delay(200);
      assert.equal(foreground().windowId, before.windowId);
      const actual = extensionTest ? lastReport.state : JSON.parse((await send('Runtime.evaluate', { expression: 'JSON.stringify(state)', returnByValue: true }, sessionId)).result.value);
      console.log(JSON.stringify({ action: name, state: actual }));
      if (name === 'left') assert.equal(actual.c, 1); if (name === 'right') assert.equal(actual.r, 1); if (!button) assert.ok(actual.s > 0);
      await delay(1500);
      const updated = await observe(name + '-after');
      point(updated, name === 'left' ? '22,163,74' : name === 'right' ? '6,182,212' : '250,204,21');
      assert.ok(captureTime(updated.value.screenshot) > captureTime(observed.value.screenshot));
      checks.push({ action: name, state: actual, capturePixelsVerified: true, frameAdvanced: true, targetOccludedAtPoint: result.targetOccludedAtPoint, raisedAboveForeground: result.raisedAboveForeground, focusChanged: result.focusChanged, cursorChanged: result.cursorChangedWithoutHumanInput });
    }
    const gestures = async () => extensionTest ? lastReport.gestureState : JSON.parse((await send('Runtime.evaluate', { expression: 'JSON.stringify(gestureState)', returnByValue: true }, sessionId)).result.value);
    function verifyDelivery(result) {
      assert.equal(result.backend, nativeMessages ? 'background' : 'browser-pixels'); assert.equal(result.backgroundInterferenceFree, true); assert.equal(result.buttonReleased, true);
      assert.equal(result.raisedAboveForeground, false); assert.equal(result.focusChanged, false); if (nativeMessages) assert.equal(result.targetOccludedAtPoint, true);
      assert.equal(foreground().windowId, before.windowId);
    }
    async function receipt(name, observed, rgb, predicate, delivery) {
      verifyDelivery(delivery); await delay(300);
      const state = await gestures(); assert.ok(predicate(state), JSON.stringify({ name, state }));
      const updated = await observe(name + '-after'); point(updated, rgb);
      assert.ok(captureTime(updated.value.screenshot) > captureTime(observed.value.screenshot));
      checks.push({ action: name, capturePixelsVerified: true, frameAdvanced: true, buttonReleased: true, focusChanged: false, raisedAboveForeground: false, cursorChanged: delivery.cursorChangedWithoutHumanInput });
      console.log('PASS gesture ' + name);
    }
    let observed = await observe('slider-before');
    const thumb = native(['--image-color-point', observed.file, '244,63,94']);
    const dpiScale = (thumb.maxX - thumb.minX + 1) / 36;
    let from = point(observed, '244,63,94');
    let delivery = text(await client.callTool({ name: 'pointer_drag', arguments: { observationId: observed.value.observationId, ...from, toX: Math.round(from.x + 264 * dpiScale * observed.value.screenshot.scaleX), toY: from.y, durationMs: 650 } }));
    await receipt('slider', observed, '0,255,255', state => state.sliderPassed, delivery);

    observed = await observe('path-drag-before'); from = point(observed, '142,36,170');
    const destination = point(observed, '146,64,14');
    delivery = text(await client.callTool({ name: 'pointer_drag', arguments: { observationId: observed.value.observationId, ...from, toX: destination.x, toY: destination.y, durationMs: 650, path: [{ x: Math.round(from.x + 55 * dpiScale), y: Math.round(from.y - 45 * dpiScale) }] } }));
    await receipt('path-drag', observed, '255,0,255', state => state.dragPassed && state.dragPath.length > 5 && state.dragPath.every(p => (p.buttons & 1) === 1) && state.dragPath.some(p => p.y < state.dragPath[0].y - 20), delivery);

    observed = await observe('hold-before'); from = point(observed, '100,116,139');
    delivery = text(await client.callTool({ name: 'pointer_click', arguments: { observationId: observed.value.observationId, ...from, holdMs: 950 } }));
    await receipt('long-press', observed, '255,0,0', state => state.longPressed, delivery);

    observed = await observe('double-before'); from = point(observed, '67,56,202');
    delivery = text(await client.callTool({ name: 'pointer_click', arguments: { observationId: observed.value.observationId, ...from, clickCount: 2 } }));
    await receipt('double-click', observed, '0,0,255', state => state.doubleClicks === 1, delivery);

    for (const index of [0, 2, 1]) {
      observed = await observe('point-' + index + '-before');
      const group = native(['--image-color-point', observed.file, '124,45,18']), image = observed.value.screenshot;
      const location = { x: Math.round(image.x + (group.minX + (index * 60 + 30) / 180 * (group.maxX - group.minX + 1)) * image.scaleX), y: Math.round(image.y + (group.minY + (group.maxY - group.minY) / 2) * image.scaleY) };
      delivery = text(await client.callTool({ name: 'pointer_click', arguments: { observationId: observed.value.observationId, ...location } })); verifyDelivery(delivery);
    }
    await receipt('ordered-point-selection', observed, '0,255,0', state => state.pointPassed && state.pointSequence === 'ACB', delivery);

    if(!nativeMessages){
      observed=await observe('html5-before');from=point(observed,'69,10,10');const end=point(observed,'77,124,15');
      delivery=text(await client.callTool({name:'pointer_drag',arguments:{observationId:observed.value.observationId,...from,toX:end.x,toY:end.y,durationMs:650}}));
      await receipt('html5-drag-drop',observed,'170,0,255',state=>state.htmlDropPassed,delivery);
    }

    observed = await observe('cancel-before'); from = point(observed, '142,36,170');
    const pointerUps = (await gestures()).pointerUps;
    const interrupted = client.callTool({ name: 'pointer_drag', arguments: { observationId: observed.value.observationId, ...from, toX: from.x + 55, toY: from.y + 55, durationMs: 1500 } });
    await waitFor(async () => (await gestures()).dragging);
    const paused = text(await client.callTool({ name: 'pointer_pause', arguments: {} }));
    assert.equal(paused.armed, false); assert.equal(paused.backgroundButtonHeld, false);
    const cancelled = await interrupted; assert.equal(cancelled.isError, true); assert.equal(JSON.parse(cancelled.content[0].text).code, 'paused');
    const released = await gestures(); assert.equal(released.dragging, false); assert.ok(released.pointerUps > pointerUps);
    checks.push({ action: 'cancel-during-drag', buttonReleased: true, focusChanged: false });
    assert.equal(foreground().windowId, before.windowId);
    await observe('after'); await client.callTool({ name: 'pointer_pause', arguments: {} });
    await writeFile(join(output, 'result.json'), JSON.stringify({ passed: true, desktopAcceptance: !headless, extensionTransport:extensionTest,browserVersion: browser.product, foregroundProcess: before.process, chromeCompatibilityFlags: compatibilityFlags, capture: nativeMessages ? 'Windows.Graphics.Capture' : 'Chrome.Debugger', realChrome: true, fullyCoveredByForeground: !headless, centerCovered: !headless, checks }, null, 2));
    console.log(`PASS ${headless ? 'headless component integration (desktop acceptance NOT run)' : 'real covered Chrome'}: gestures and captured page updates. Report: ${output}`);
  } finally {
    if (client) await client.close();
    if(extensionBridge)await extensionBridge.close();
    if (fixture) { fixture.stdin.end('done\n'); await Promise.race([new Promise(done => fixture.once('exit', done)), delay(3000)]); }
    if (socket?.readyState === WebSocket.OPEN) { await send('Browser.close').catch(() => {}); socket.close(); }
    if (chrome) await Promise.race([new Promise(done => chrome.once('exit', done)), delay(3000)]);
    if(labServer){labServer.closeAllConnections?.();await new Promise(done=>labServer.close(done));}
    assert.equal(dirname(resolve(temp)), resolve(tmpdir())); assert.ok(basename(temp).startsWith('agentmouse-background-'));
    await rm(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  }
}
