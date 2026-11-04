import { mkdtemp, mkdir, readFile, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import assert from 'node:assert/strict';
const {version}=JSON.parse(await readFile('package.json','utf8'));
const temporary=await mkdtemp(join(tmpdir(),'agentmouse-edition-switch-'));
try {
  for(const edition of ['Core','Full']){const target=join(temporary,edition);await mkdir(target);execFileSync('tar.exe',['-xf',resolve(`release/AgentMouse-${edition}-${version}-windows-x64.zip`),'-C',target],{windowsHide:true});}
  const home=join(temporary,'user-home');
  const env={...process.env,USERPROFILE:home,HOME:home,CODEX_HOME:join(home,'.codex'),ZCODE_STORAGE_DIR:join(home,'.zcode'),ZCODE_DATA_BASE_DIR:home,AGENTMOUSE_HOME:join(home,'.agentmouse')};
  for(const edition of ['Core','Full','Core']){
    execFileSync(process.execPath,[join(temporary,edition,'install.mjs'),'--agent','codex,zcode','--home',home,'--browser','none'],{encoding:'utf8',windowsHide:true,timeout:300000});
    const result=execFileSync(process.execPath,[resolve('scripts/test-clients.mjs')],{env,encoding:'utf8',windowsHide:true,timeout:60000});
    assert.match(result,new RegExp((edition==='Full'?'26':'11')+' tools'));
    console.log(`PASS isolated native Codex/ZCode switch -> ${edition}: `+result.trim().replaceAll('\n',' | '));
  }
}finally{
  assert.equal(dirname(resolve(temporary)),resolve(tmpdir()));assert.ok(basename(temporary).startsWith('agentmouse-edition-switch-'));await rm(temporary,{recursive:true,force:true,maxRetries:10,retryDelay:200});
}
