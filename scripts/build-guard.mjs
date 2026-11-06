import {execFileSync} from 'node:child_process';
import {existsSync,mkdirSync,readFileSync,writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
export function buildGuard(root=dirname(dirname(fileURLToPath(import.meta.url)))) {
  if(!existsSync(join(root,'native/activation-guard.c'))&&existsSync(join(root,'dist/AgentMouse.Guard.current')))return;
  const bundled=join(root,'compatibility-results/activation-toolchain/llvm-mingw-20261104-ucrt-x86_64/bin/x86_64-w64-mingw32-gcc.exe');
  const compiler=process.env.AGENTMOUSE_GCC||(existsSync(bundled)?bundled:null);
  if(!existsSync(join(root,'native/activation-guard.c'))&&existsSync(join(root,'dist/AgentMouse.Guard.dll')))return;
  if(!compiler&&existsSync(join(root,'dist/AgentMouse.Guard.dll')))return;
  if(!compiler)throw new Error('Building the native guard requires an x64 MinGW compiler. Set AGENTMOUSE_GCC. Release packages include the compiled DLL.');
  mkdirSync(join(root,'dist/guards'),{recursive:true});
  const vendor=join(root,'vendor/minhook');
  const sources=['native/activation-guard.c','native/activation-policy.h','native/virtual-pointer.h',...['hook.c','buffer.c','buffer.h','trampoline.c','trampoline.h','hde/hde64.c','hde/hde64.h','hde/table64.h'].map(file=>'vendor/minhook/src/'+file),'vendor/minhook/include/MinHook.h'];
  const hash=createHash('sha256');for(const file of sources)hash.update(file).update(readFileSync(join(root,file)));
  const name='AgentMouse.Guard.'+hash.digest('hex').slice(0,32)+'.dll';
  if(!existsSync(join(root,'dist/guards',name)))execFileSync(compiler,['-Wall','-Wextra','-Os','-shared','-I'+join(vendor,'include'),...['hook.c','buffer.c','trampoline.c','hde/hde64.c'].map(file=>join(vendor,'src',file)),join(root,'native/activation-guard.c'),'-o',join(root,'dist/guards',name),'-luser32','-lcomctl32'],{windowsHide:true,stdio:'inherit'});
  writeFileSync(join(root,'dist/AgentMouse.Guard.current'),name+'\n');
}
export function guardFiles(root){const name=readFileSync(join(root,'dist/AgentMouse.Guard.current'),'utf8').trim();if(!/^AgentMouse\.Guard\.[a-f0-9]{32}\.dll$/.test(name))throw new Error('Invalid guard manifest');return ['dist/AgentMouse.Guard.current','dist/guards/'+name];}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])buildGuard();
