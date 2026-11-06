import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {buildGuard} from './build-guard.mjs';

export function buildNative(root = dirname(dirname(fileURLToPath(import.meta.url))), stdio = 'inherit') {
  if (process.platform !== 'win32' || process.arch !== 'x64') throw new Error('The native controller requires Windows x64.');
  const compiler = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET/Framework64/v4.0.30319/csc.exe');
  if (!existsSync(compiler)) throw new Error('Windows .NET Framework 4.x compiler is missing.');
  mkdirSync(join(root, 'dist'), { recursive: true });
  const common = ['/nologo', '/utf8output', '/codepage:65001', '/platform:x64', '/optimize+', ...['System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Web.Extensions'].map(name => '/r:' + name + '.dll')];
  common.push(...['UIAutomationClient','UIAutomationTypes','WindowsBase'].map(name => '/r:' + join(dirname(compiler), 'WPF', name + '.dll')));
  const sources = ['Native.cs','ActivationGuard.cs','DesktopInventory.cs','WindowCapture.cs', 'Core.cs', 'Transport.cs', 'Ui.cs', 'Program.cs', 'SelfTest.cs'].map(file => join(root, file));
  for (const cli of [false, true]) execFileSync(compiler, [...common, '/target:' + (cli ? 'exe' : 'winexe'), ...(cli ? ['/define:CLI'] : []), '/out:' + join(root, 'dist', cli ? 'AgentMouse.Cli.exe' : 'AgentMouse.exe'), ...sources], { stdio, windowsHide: true });
  buildGuard(root);
}
if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) buildNative();
