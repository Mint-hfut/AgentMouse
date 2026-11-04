import assert from 'node:assert/strict';
import { mkdtemp, rm, readFile, mkdir, rename } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import { execFileSync } from 'node:child_process';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
const {version}=JSON.parse(await readFile('package.json','utf8'));
const temporary=await mkdtemp(join(tmpdir(),'agentmouse-fusion-zip-'));
const client=new Client({name:'fusion-zip-acceptance',version:'1'});
try {
  const payload=join(temporary,'payload');await mkdir(payload);
  execFileSync('tar.exe',['-xf',resolve(process.argv[2]||`release/AgentMouse-Full-${version}-windows-x64.zip`),'-C',payload],{windowsHide:true});
  assert.equal(JSON.parse(await readFile(join(payload,'package.json'),'utf8')).agentmouseEdition,'full');
  const home=join(temporary,'用户 home');
  const isolatedEnv={...process.env,PATH:[dirname(process.execPath),join(process.env.WINDIR,'System32')].join(';'),PYTHONHOME:join(temporary,'no-host-python'),PYTHONPATH:join(temporary,'no-host-site-packages')};
  execFileSync(process.execPath,[join(payload,'install.mjs'),'--agent','claude-code','--mode','mcp','--home',home,'--browser','none'],{windowsHide:true,encoding:'utf8',timeout:300000,env:isolatedEnv});
  const parked=join(temporary,'original-payload-parked');assert.equal(dirname(resolve(payload)),resolve(temporary));assert.equal(dirname(resolve(parked)),resolve(temporary));await rename(payload,parked);
  const runtime=join(home,`AgentMouse/${version}-fusion`);
  await client.connect(new StdioClientTransport({command:process.execPath,args:[join(runtime,'fusion-entry.mjs')],cwd:runtime,env:{...isolatedEnv,AGENTMOUSE_HOME:join(home,'.agentmouse')},stderr:'pipe'}));
  assert.equal((await client.listTools()).tools.length,26);
  assert.equal(client.getServerVersion().version,version);
  const result=await client.callTool({name:'App',arguments:{mode:'find',query:'node'}});assert.ok(!result.isError);
  console.log('PASS Full ZIP -> isolated Chinese home -> bundled Python -> 26-tool MCP -> running-app query; Python/uv absent from PATH, host Python env invalid, original payload relocated; no GUI/input');
} finally {
  await client.close();assert.equal(dirname(resolve(temporary)),resolve(tmpdir()));assert.ok(basename(temporary).startsWith('agentmouse-fusion-zip-'));await rm(temporary,{recursive:true,force:true,maxRetries:10,retryDelay:200});
}
