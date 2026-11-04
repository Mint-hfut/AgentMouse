import { execFileSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';
const config=JSON.parse(await readFile(join(process.env.AGENTMOUSE_HOME||join(homedir(),'.agentmouse'),'fusion/config.json'),'utf8'));
execFileSync(config.python,[resolve('fusion/test_engine.py')],{stdio:'inherit',windowsHide:true,timeout:15000});
execFileSync(process.execPath,[resolve('scripts/test-fusion-sdk.mjs')],{stdio:'inherit',windowsHide:true,timeout:60000});
