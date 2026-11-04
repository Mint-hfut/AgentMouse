import { browserCall, bridgeConfig, bridgeRequest } from './bridge-client.mjs';

export function browserRouter(nativeCall) {
  const browserObservations = new Map();
  return async request => {
    try {
      if (request.method === 'status') {
        const result = await nativeCall(request), config = await bridgeConfig();
        if (result.ok) { let state = { configured: !!config, connected: false }; if (config) { try { state = { configured: true, ...await bridgeRequest(config, '/health') }; } catch {} } result.result = { ...result.result, browser: state }; }
        return result;
      }
      if (request.method === 'list_windows') {
        const native = await nativeCall(request);
        if (!native.ok || !await bridgeConfig()) return native;
        try { const tabs = await browserCall({ method: 'list_tabs' }); return { ok: true, result: [...native.result, ...tabs.map(tab => ({ windowId: 'browser:' + tab.id, title: tab.title, process: 'Chrome (background pixel channel)' }))] }; }
        catch { return native; }
      }
      if (request.method === 'observe' && /^browser:\d+$/.test(request.windowId || '')) {
        if (request.screenshot === false) return { ok: false, error: { code: 'invalid_argument', message: 'A real browser image is required.' } };
        const tabId = Number(request.windowId.slice(8)), result = await browserCall({ method: 'observe', tabId });
        browserObservations.clear(); browserObservations.set(result.observationId, tabId); return { ok: true, result };
      }
      if (request.observationId?.startsWith('browser-')) {
        const tabId = browserObservations.get(request.observationId);
        if (!tabId) return { ok: false, error: { code: 'stale_observation', message: 'Observe the browser tab again.' } };
        browserObservations.delete(request.observationId);
        return { ok: true, result: await browserCall({ ...request, tabId }) };
      }
      if (request.method === 'move' && request.observationId) return { ok: false, error: { code: 'unsupported', message: 'Window-message hover is not supported. Use a browser: observation or move the overlay without observationId.' } };
      if (request.method === 'pause') {
        const native = nativeCall(request);
        let browserError;
        try { if (await bridgeConfig()) await browserCall({ method: 'pause' }); } catch (error) { browserError = error.message; }
        const result = await native;
        if (result.ok && browserError) result.result = { ...result.result, browserReleaseError: browserError };
        return result;
      }
      if (request.method === 'release_capture') {
        const native = nativeCall(request);
        try { if (await bridgeConfig()) await browserCall({ method: 'release_capture' }); } catch {}
        return native;
      }
      return nativeCall(request);
    } catch (error) { return { ok: false, error: { code: error.code || 'browser_error', message: error.message } }; }
  };
}
