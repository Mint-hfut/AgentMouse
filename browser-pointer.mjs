import { randomUUID, createHash } from 'node:crypto';

const fault = (code, message) => Object.assign(new Error(message), { code });
const delay = ms => new Promise(done => setTimeout(done, ms));
const sameViewport = (a, b) => ['clientWidth', 'clientHeight', 'pageX', 'pageY', 'scale'].every(key => a[key] === b[key]);

// The transport can be chrome.debugger or CDP; the mouse engine uses pixels, never DOM selectors.
export class BrowserPointer {
  constructor({ send, checkPermission = async () => ({}), now = Date.now }) {
    this.transport = send; this.checkPermission = checkPermission; this.now = now;
    this.queue = Promise.resolve(); this.generation = 0; this.held = null; this.releasing = null; this.uncertain = false; this.observation = null;
  }
  send(method, params = {}) {
    const task = this.queue.catch(() => {}).then(() => this.transport(method, params));
    this.queue = task; return task;
  }
  event(method, params) {
    if (method === 'Input.dragIntercepted' && this.dragActive) this.dragData = params.data;
    if (method === 'AgentMouse.leaseExpired' && params.released === true) { this.generation++; this.observation = null; this.held = null; }
  }
  async viewport() {
    const metrics = await this.send('Page.getLayoutMetrics');
    const viewport = metrics.cssVisualViewport || metrics.visualViewport;
    if (!viewport || viewport.clientWidth <= 0 || viewport.clientHeight <= 0 || (viewport.scale && viewport.scale !== 1)) throw fault('unsupported_viewport', 'A valid, unpinched browser viewport is required.');
    return viewport;
  }
  async observe(windowId, attempt = 0) {
    this.observation = null;
    // Keep occluded tabs rendering without activating their Windows window; detach clears this override.
    await this.send('Emulation.setFocusEmulationEnabled', { enabled: true });
    let viewport = await this.viewport();
    for (let i = 0; i < 5; i++) { await delay(100); const next = await this.viewport(); if (sameViewport(viewport, next)) { viewport = next; break; } viewport = next; }
    const clip = { x: viewport.pageX || 0, y: viewport.pageY || 0, width: viewport.clientWidth, height: viewport.clientHeight, scale: 1 };
    let picture = await this.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false, clip });
    const dimensions = data => {
      const bytes = Buffer.from(data, 'base64');
      if (bytes.length < 24 || !bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) throw fault('invalid_capture', 'The browser did not return a real PNG image.');
      return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
    };
    let size = dimensions(picture.data);
    if (size.width * size.height > 4000000) {
      picture = await this.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false, clip: { x: viewport.pageX || 0, y: viewport.pageY || 0, width: viewport.clientWidth, height: viewport.clientHeight, scale: Math.sqrt(4000000 / (size.width * size.height)) } });
      size = dimensions(picture.data);
      if (size.width * size.height > 4100000) throw fault('capture_too_large', 'The browser could not reduce the captured viewport safely.');
    }
    const afterCapture = await this.viewport();
    if (!sameViewport(viewport, afterCapture)) {
      if (attempt < 2) { await delay(200); return this.observe(windowId, attempt + 1); }
      throw fault('window_changed', `The browser viewport changed while capturing from ${JSON.stringify(viewport)} to ${JSON.stringify(afterCapture)}; observe again.`);
    }
    const token = 'browser-' + randomUUID(), at = this.now();
    this.observation = { token, at, viewport };
    const hash = createHash('sha256').update(picture.data).digest('hex'), changed = hash !== this.lastHash; this.lastHash = hash;
    return { scope: 'window', capture: 'window', coordinateSpace: 'browser-viewport-css', observationId: token, validForMs: 15000, window: { windowId }, screenshot: { mimeType: 'image/png', data: picture.data, x: 0, y: 0, ...size, sourceWidth: viewport.clientWidth, sourceHeight: viewport.clientHeight, scaleX: viewport.clientWidth / size.width, scaleY: viewport.clientHeight / size.height, captureMethod: 'Chrome.Debugger', captureTimestampMs: at, frameChanged: changed } };
  }
  point(point, viewport) {
    if (!Number.isSafeInteger(point.x) || !Number.isSafeInteger(point.y) || point.x < 0 || point.y < 0 || point.x >= viewport.clientWidth || point.y >= viewport.clientHeight) throw fault('offscreen', 'Point is outside the captured browser viewport.');
    return { x: point.x, y: point.y };
  }
  async gate(generation, baseline) {
    if (generation !== this.generation) throw fault('paused', 'The browser gesture was paused.');
    const status = await this.checkPermission();
    if (generation !== this.generation) throw fault('paused', 'The browser gesture was paused.');
    if (status?.armed === false) throw fault('paused', 'Enable input in the local controller first.');
    if (baseline?.foregroundWindowId && status?.foregroundWindowId !== baseline.foregroundWindowId) throw fault('foreground_interference', 'Foreground changed during the browser gesture.');
    if (baseline?.windowZOrder && status?.windowZOrder) {
      const before = baseline.windowZOrder.indexOf(baseline.foregroundWindowId), after = status.windowZOrder.indexOf(baseline.foregroundWindowId);
      const behind = new Set(baseline.windowZOrder.slice(before + 1));
      if (before >= 0 && after >= 0 && status.windowZOrder.slice(0, after).some(window => behind.has(window))) throw fault('foreground_interference', 'A background window rose above the user foreground window.');
    }
    return status;
  }
  async release() {
    if (!this.held) return;
    if (this.releasing) return this.releasing;
    const held = this.held;
    this.releasing = this.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: held.x, y: held.y, button: held.button, buttons: 0, clickCount: held.clickCount, modifiers: 0 })
      .then(() => { this.held = null; }).catch(error => { this.uncertain = true; throw fault('button_release_unconfirmed', 'Browser button release was not confirmed: ' + error.message); })
      .finally(() => { this.releasing = null; });
    return this.releasing;
  }
  async pause() { this.generation++; this.observation = null; await this.release(); if (this.dragActive) await this.send('Input.cancelDragging'); return { armed: false, backgroundButtonHeld: !!this.held }; }
  async act(request) {
    if (!['click', 'drag', 'scroll', 'move'].includes(request.method) || (request.path !== undefined && !Array.isArray(request.path))) throw fault('invalid_argument', 'Invalid browser gesture.');
    if (request.method === 'scroll' && (!Number.isSafeInteger(request.delta) || request.delta === 0 || Math.abs(request.delta) > 1200)) throw fault('invalid_argument', 'Invalid wheel delta.');
    const observation = this.observation;
    if (!observation || request.observationId !== observation.token || this.now() - observation.at > 15000) throw fault('stale_observation', 'Use a fresh browser observation.');
    if (this.uncertain || this.held) throw fault('contact_unreleased', 'A previous browser button release is unconfirmed.');
    const currentViewport = await this.viewport();
    if (!sameViewport(observation.viewport, currentViewport)) throw fault('window_changed', `The browser viewport changed from ${JSON.stringify(observation.viewport)} to ${JSON.stringify(currentViewport)}; observe again.`);
    const from = this.point(request, observation.viewport), drag = request.method === 'drag';
    const to = drag ? this.point({ x: request.toX, y: request.toY }, observation.viewport) : from;
    const path = [from, ...(request.path || []).map(point => this.point(point, observation.viewport)), to];
    if (path.length > 130) throw fault('invalid_argument', 'Too many gesture waypoints.');
    const count = request.clickCount ?? 1, hold = request.holdMs ?? 0, duration = request.durationMs ?? 400;
    if (![1, 2].includes(count) || !Number.isInteger(hold) || hold < 0 || hold > 2000 || !Number.isInteger(duration) || duration < 100 || duration > 5000 || (count > 1 && hold)) throw fault('invalid_argument', 'Invalid gesture count or duration.');
    const generation = this.generation, status = await this.gate(generation), foreground = status;
    this.observation = null;
    await this.send('Input.dispatchMouseEvent', { type: 'mouseMoved', ...from, button: 'none', buttons: 0, modifiers: 0 });
    if (request.method === 'move') {
      await this.gate(generation, foreground);
    } else if (request.method === 'scroll') {
      await this.gate(generation, foreground);
      await this.send('Input.dispatchMouseEvent', { type: 'mouseWheel', ...from, deltaX: 0, deltaY: -request.delta, button: 'none', buttons: 0, modifiers: 0 });
    } else {
      const button = request.button || 'left'; if (!['left', 'right'].includes(button)) throw fault('invalid_argument', 'Invalid browser button.');
      const pressed = button === 'left' ? 1 : 2;
      const cumulative = [0]; for (let i = 1; i < path.length; i++) cumulative.push(cumulative[i - 1] + Math.hypot(path[i].x - path[i - 1].x, path[i].y - path[i - 1].y));
      this.dragData = null; this.dragActive = drag;
      let dropped = false, entered = false;
      try {
      if (drag) await this.send('Input.setInterceptDrags', { enabled: true });
      for (let click = 1; click <= count; click++) {
        await this.gate(generation, foreground);
        this.held = { ...from, button, clickCount: click };
        try {
          await this.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...from, button, buttons: pressed, clickCount: click, modifiers: 0 });
          const span = drag ? duration : hold, started = this.now(); let previousDistance = 0;
          while (span > 0) {
            await this.gate(generation, foreground);
            const total = cumulative.at(-1);
            // Slow permission checks must not skip path corners or collapse a drag into a jump.
            const distance = drag && total ? Math.min(total * (this.now() - started) / span, previousDistance + total / 20, cumulative.find(value => value > previousDistance) ?? total) : total;
            const fraction = drag && total ? distance / total : Math.min(1, (this.now() - started) / span);
            previousDistance = distance;
            let segment = 1; while (segment < cumulative.length - 1 && cumulative[segment] < distance) segment++;
            const length = cumulative[segment] - cumulative[segment - 1], t = length ? (distance - cumulative[segment - 1]) / length : 1;
            if (drag) {
              const location = { x: path[segment - 1].x + (path[segment].x - path[segment - 1].x) * t, y: path[segment - 1].y + (path[segment].y - path[segment - 1].y) * t };
              Object.assign(this.held, location);
              await this.send('Input.dispatchMouseEvent', { type: 'mouseMoved', ...location, button, buttons: pressed, modifiers: 0 });
              if (this.dragData) {
                if (!entered) { await this.send('Input.dispatchDragEvent', { type: 'dragEnter', ...location, data: this.dragData, modifiers: 0 }); entered = true; }
                await this.send('Input.dispatchDragEvent', { type: 'dragOver', ...location, data: this.dragData, modifiers: 0 });
              }
            }
            if (fraction >= 1) break; await delay(12);
          }
          if (drag) {
            await delay(80); await this.gate(generation, foreground);
            if (this.dragData) {
              if (!entered) await this.send('Input.dispatchDragEvent', { type: 'dragEnter', ...to, data: this.dragData, modifiers: 0 });
              await this.send('Input.dispatchDragEvent', { type: 'dragOver', ...to, data: this.dragData, modifiers: 0 });
              await this.send('Input.dispatchDragEvent', { type: 'drop', ...to, data: this.dragData, modifiers: 0 }); dropped = true;
            }
          }
        } finally { await this.release(); }
        if (click < count) await delay(40);
      }
      } finally {
        if (drag) { if (!dropped) await this.send('Input.cancelDragging').catch(() => {}); await this.send('Input.setInterceptDrags', { enabled: false }).catch(() => {}); }
        this.dragActive = false;
      }
    }
    await delay(60); const after = await this.gate(generation, foreground);
    const cursorChanged = status?.systemPointer && after?.systemPointer && status.humanInputSequence === after.humanInputSequence && (status.systemPointer.x !== after.systemPointer.x || status.systemPointer.y !== after.systemPointer.y);
    if (cursorChanged) throw fault('cursor_interference', 'Unattributed system cursor movement was observed.');
    return { inputDelivered: true, businessResultVerified: false, backend: 'browser-pixels', backgroundTarget: true, buttonReleased: !this.held, dragCompleted: drag, focusChanged: false, raisedAboveForeground: false, cursorChangedWithoutHumanInput: false, backgroundInterferenceFree: true };
  }
}
