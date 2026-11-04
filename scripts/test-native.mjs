import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
execFileSync(fileURLToPath(new URL('../dist/AgentMouse.Cli.exe', import.meta.url)), [process.argv.includes('--routing') ? '--routing-self-test' : '--state-self-test'], { stdio: 'inherit', windowsHide: true, timeout: 20000 });
if (!process.argv.includes('--routing')) {
  const query = request => JSON.parse(execFileSync(fileURLToPath(new URL('../dist/AgentMouse.Cli.exe', import.meta.url)), ['--desktop-query', JSON.stringify(request)], { encoding: 'utf8', windowsHide: true, timeout: 15000, maxBuffer: 4 * 1024 * 1024 }));
  const processes = query({ method: 'list_apps', query: 'AgentMouse.Cli' });
  assert.equal(processes.ok, true); assert.ok(processes.result.processes.some(process => process.running && process.pid > 0));
  const windows = query({ method: 'list_windows' }); assert.equal(windows.ok, true); assert.ok(Array.isArray(windows.result));
  for (const window of windows.result) { assert.equal(typeof window.pid, 'number'); assert.equal(typeof window.minimized, 'boolean'); assert.equal(typeof window.canCapture, 'boolean'); assert.equal(window.zOrderDirection, 'front-to-back'); assert.ok(Array.isArray(window.overlappingWindowsAbove)); }
  console.log('PASS real read-only process/window queries without opening a controller');
}
