import { BASE_URL, TOKEN } from './config.js';

const attached = new Set(), held = new Map();
const allowed = new Set(['Emulation.setFocusEmulationEnabled', 'Page.getLayoutMetrics', 'Page.captureScreenshot', 'Input.dispatchMouseEvent', 'Input.setInterceptDrags', 'Input.dispatchDragEvent', 'Input.cancelDragging']);
let connected = false, lastError = '', stopped = false, clientId;
const delay = ms => new Promise(done => setTimeout(done, ms));
async function http(path, body) {
  const response = await fetch(BASE_URL + path, { method: body ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + TOKEN, 'Content-Type': 'application/json', 'X-AgentMouse-Client': clientId }, ...(body ? { body: JSON.stringify(body) } : {}), cache: 'no-store' });
  if (!response.ok) throw new Error('Bridge HTTP ' + response.status);
  return response.json();
}
async function release(tabId) {
  const state = held.get(tabId);
  if (state) {
    await chrome.debugger.sendCommand({ tabId }, 'Input.dispatchMouseEvent', { type: 'mouseReleased', x: state.x, y: state.y, button: state.button, buttons: 0, clickCount: state.clickCount || 1, modifiers: 0 });
    held.delete(tabId);
  }
}
async function releaseAll() {
  await Promise.allSettled([...held.keys()].map(release));
  await Promise.allSettled([...attached].map(async tabId => {await chrome.debugger.detach({tabId});attached.delete(tabId);held.delete(tabId);}));
  if(attached.size||held.size)throw new Error('Browser release/detach unconfirmed; stopped without forgetting held state');
}
chrome.debugger.onDetach.addListener(source => { attached.delete(source.tabId); held.delete(source.tabId); });
chrome.debugger.onEvent.addListener((source, method, params) => { if (method === 'Input.dragIntercepted') http('/event', { tabId: source.tabId, method, params }).catch(error => { lastError = error.message; }); });
setInterval(() => { for (const [tabId, state] of held) if (Date.now() - state.at > 3000) release(tabId).then(()=>http('/event',{tabId,method:'AgentMouse.leaseExpired',params:{released:true}})).catch(error => { lastError = error.message; }); }, 250);
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id) return;
  if (message.type === 'status') { respond({ connected, stopped, attachedTabs: attached.size, lastError }); return; }
  if (message.type === 'stop') { stopped = true; Promise.allSettled([releaseAll(),http('/call',{method:'pause'})]).then(results=>{const failed=results.find(x=>x.status==='rejected');if(failed)lastError=failed.reason.message;respond({ok:!failed,error:lastError});});return true; }
  if (message.type === 'resume') { stopped = false; respond({ ok: true }); }
});
async function command(item) {
  if (item.op === 'tabs') return (await chrome.tabs.query({})).filter(tab => tab.id && /^https?:|^file:/.test(tab.url || '')).map(tab => ({ id: tab.id, title: tab.title || 'Browser tab', windowId: tab.windowId }));
  if (item.op === 'release') { await releaseAll(); return {}; }
  if (item.op !== 'debugger' || !Number.isSafeInteger(item.tabId) || !allowed.has(item.method)) throw new Error('Unsupported bridge command');
  const target = { tabId: item.tabId };
  if (!attached.has(item.tabId)) { await chrome.debugger.attach(target, '1.3'); attached.add(item.tabId); }
  if (item.method === 'Input.dispatchMouseEvent') {
    const event = item.params;
    if (event.type === 'mousePressed') held.set(item.tabId, { ...event, at: Date.now() });
    if (event.type === 'mouseMoved' && held.has(item.tabId)) Object.assign(held.get(item.tabId), event, { at: Date.now() });
    const result = await chrome.debugger.sendCommand(target, item.method, item.params);
    if (event.type === 'mouseReleased') held.delete(item.tabId);
    return result;
  }
  return chrome.debugger.sendCommand(target, item.method, item.params);
}
async function run() {
const saved = await chrome.storage.local.get('clientId');clientId = saved.clientId || crypto.randomUUID();await chrome.storage.local.set({ clientId });
if (!BASE_URL || !TOKEN) lastError = 'Run AgentMouse browser-setup, then load the generated extension directory.';
while (BASE_URL && TOKEN) {
  try {
    const item = await http('/next'); connected = true; lastError = '';
    if (item.op === 'idle') continue;
    let result; try {
      const cleanup = item.op === 'release' || (item.op === 'debugger' && (item.method === 'Input.cancelDragging' || (item.method === 'Input.dispatchMouseEvent' && item.params?.type === 'mouseReleased')));
      if (stopped && !cleanup) throw new Error('Browser bridge is stopped by the user.');
      result = { id: item.id, result: await command(item) };
    } catch (error) { result = { id: item.id, error: error.message }; }
    await http('/reply', result);
  } catch (error) { connected = false; lastError = error.message;try{await releaseAll();}catch(cleanup){stopped=true;lastError+='; '+cleanup.message;}await delay(1000); }
}
}
run().catch(error => { lastError = error.message; });
