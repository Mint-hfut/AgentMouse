import { execFileSync } from 'node:child_process';
import { cp, mkdir, readdir, readFile, writeFile, realpath } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, resolve, dirname, basename, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { setupFusion } from './fusion-setup.mjs';

export async function buildPythonRuntime(sourceRoot, destination) {
  destination=resolve(destination);await mkdir(destination,{recursive:false});
  const buildHome=join(dirname(destination),'python-build-home');
  const environment={...process.env,AGENTMOUSE_HOME:join(buildHome,'.agentmouse')};
  await setupFusion({sourceRoot,home:process.env.USERPROFILE,env:environment,preferBundled:false});
  const config=JSON.parse(await readFile(join(environment.AGENTMOUSE_HOME,'fusion/config.json'),'utf8'));
  const base=execFileSync(config.python,['-c','import sys;print(sys.base_prefix)'],{encoding:'utf8',windowsHide:true}).trim();
  const baseSite=resolve(base,'Lib/site-packages').toLowerCase();
  const allowed=file=>!/__pycache__|\.pyc$|\.egg-info/.test(file);
  for(const entry of await readdir(base,{withFileTypes:true})){
    if(!['Lib','DLLs','tcl','python.exe','pythonw.exe','python3.dll','python314.dll','LICENSE.txt','vcruntime140.dll','vcruntime140_1.dll'].includes(entry.name))continue;
    await cp(join(base,entry.name),join(destination,entry.name),{recursive:true,filter:file=>allowed(file)&&!resolve(file).toLowerCase().startsWith(baseSite)});
  }
  const site=join(destination,'Lib/site-packages');
  await cp(join(config.project,'.venv','Lib/site-packages'),site,{recursive:true,filter:file=>allowed(file)&&!basename(file).startsWith('__editable__')&&!['_virtualenv.py','_virtualenv.pth','direct_url.json','uv_cache.json','uv_build.json'].includes(basename(file))});
  await cp(join(sourceRoot,'vendor/windows-mcp/src/windows_mcp'),join(site,'windows_mcp'),{recursive:true,filter:allowed});
  await writeFile(join(destination,'python314._pth'),'DLLs\nLib\nLib/site-packages\n.\nimport site\n');
  const probe='import sys,fastmcp,windows_mcp,win32gui,win32api,pythoncom,ssl,PIL,psutil;assert sys.flags.isolated;print(sys.version.split()[0])';
  const pythonVersion=execFileSync(join(destination,'python.exe'),['-B','-c',probe],{encoding:'utf8',windowsHide:true,timeout:30000,env:{...process.env,PYTHONHOME:join(destination,'invalid-host-python'),PYTHONPATH:join(destination,'invalid-host-packages'),PATH:join(process.env.WINDIR,'System32')}}).trim();
  const files=[];
  async function walk(folder){for(const entry of await readdir(folder,{withFileTypes:true})){const path=join(folder,entry.name);if(entry.isDirectory())await walk(path);else if(entry.isFile()){const hash=createHash('sha256');for await(const chunk of createReadStream(path))hash.update(chunk);files.push({path:relative(destination,path).replaceAll('\\','/'),sha256:hash.digest('hex')});}}}
  await walk(destination);files.sort((a,b)=>a.path.localeCompare(b.path));
  const id=createHash('sha256').update(JSON.stringify(files)).digest('hex');
  await writeFile(join(destination,'RUNTIME.json'),JSON.stringify({id,pythonVersion,platform:'windows-x64',windowsMcpVersion:'0.8.8',isolated:true,files},null,2));
  return {directory:await realpath(destination),id,pythonVersion,files:files.length};
}

if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
  if(!process.argv[2])throw new Error('Specify a new runtime staging directory.');
  console.log(JSON.stringify(await buildPythonRuntime(dirname(dirname(fileURLToPath(import.meta.url))),process.argv[2])));
}
