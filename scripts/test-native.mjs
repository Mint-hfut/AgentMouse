import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
execFileSync(fileURLToPath(new URL('../dist/AgentMouse.Cli.exe', import.meta.url)), ['--state-self-test'], { stdio: 'inherit', windowsHide: true });
