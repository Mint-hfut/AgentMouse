import {execFileSync} from 'node:child_process';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';

assert.equal(process.platform,'win32','This experiment requires a Windows interactive desktop');
const root=dirname(dirname(fileURLToPath(import.meta.url)));
const out=mkdtempSync(join(tmpdir(),'agentmouse-activation-guard-'));
const exe=join(out,'guard.exe');
const compiler=join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe');
const cross=process.argv.includes('--cross-process');
const qt=process.argv.includes('--qt');
assert.ok(!qt||cross,'--qt requires --cross-process');
const args=[];
if(cross){
  const gcc=process.env.AGENTMOUSE_GCC;
  assert.ok(gcc,'Set AGENTMOUSE_GCC to an x64 MinGW compiler for this optional experiment');
  args.push(join(out,'target.exe'),join(out,'guard.dll'));
  execFileSync(gcc,['-Wall','-Wextra','-Os',join(root,'scripts/fixtures/activation-guard-target.c'),'-o',args[0],'-luser32'],{windowsHide:true,stdio:'pipe'});
  execFileSync(gcc,['-Wall','-Wextra','-Os','-shared',join(root,'scripts/fixtures/activation-guard-hook.c'),'-o',args[1],'-luser32','-lcomctl32'],{windowsHide:true,stdio:'pipe'});
  if(qt){assert.ok(process.env.AGENTMOUSE_QT_PYTHON,'Set AGENTMOUSE_QT_PYTHON to the test interpreter with PyQt5==5.15.11');args[0]=process.env.AGENTMOUSE_QT_PYTHON;args.push(join(root,'scripts/fixtures/activation-guard-qt.py'));}
}
execFileSync(compiler,['/nologo','/platform:x64','/target:exe','/out:'+exe,'/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',join(root,'scripts/fixtures',cross?'activation-guard-driver.cs':'activation-guard.cs')],{windowsHide:true,stdio:'pipe'});
try{
  const raw=execFileSync(exe,args,{windowsHide:true,timeout:15000,encoding:'utf8'});
  const result=JSON.parse(raw.trim());assert.equal(result.ok,true);console.log(JSON.stringify(result));
}catch(error){if(error.stdout)process.stderr.write(error.stdout);throw error;}
