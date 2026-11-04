async function refresh() { const state = await chrome.runtime.sendMessage({ type: 'status' }); document.querySelector('#state').textContent = state.stopped ? '已停止' : state.connected ? `已连接，正在调试 ${state.attachedTabs} 个标签页` : state.lastError || '等待本机 MCP 桥接'; }
document.querySelector('#stop').onclick = async () => { await chrome.runtime.sendMessage({ type: 'stop' }); await refresh(); };
document.querySelector('#resume').onclick = async () => { await chrome.runtime.sendMessage({ type: 'resume' }); await refresh(); };
await refresh();
