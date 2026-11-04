export async function connectCdp(url) {
  if (typeof WebSocket !== 'function') throw new Error('The optional CDP test transport needs Node 22+.');
  const parsed = new URL(url);
  if (!['ws:', 'wss:'].includes(parsed.protocol) || !['127.0.0.1', 'localhost', '[::1]'].includes(parsed.hostname)) throw new Error('Only a local test browser endpoint is accepted.');
  const socket = new WebSocket(url), pending = new Map(), listeners = new Set(); let sequence = 0;
  await new Promise((done, fail) => { socket.addEventListener('open', done, { once: true }); socket.addEventListener('error', fail, { once: true }); });
  socket.addEventListener('message', event => {
    const value = JSON.parse(event.data);
    if (value.id !== undefined) { pending.get(value.id)?.(value); pending.delete(value.id); }
    else for (const listener of listeners) listener(value);
  });
  socket.addEventListener('close', () => { for (const callback of pending.values()) callback({ error: { message: 'Browser disconnected' } }); pending.clear(); });
  return {
    send(method, params = {}, sessionId) {
      return new Promise((done, fail) => {
        const id = ++sequence, timer = setTimeout(() => { pending.delete(id); fail(new Error('Browser command timed out: ' + method)); }, 15000);
        pending.set(id, value => { clearTimeout(timer); value.error ? fail(new Error(JSON.stringify(value.error))) : done(value.result); });
        socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
      });
    },
    onEvent(listener) { listeners.add(listener); return () => listeners.delete(listener); },
    close() { socket.close(); },
  };
}
