import { spawn,execFileSync } from 'node:child_process';
import { readFile,mkdir,rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath,pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.join(root,'artifacts','launcher-tests',Date.now().toString()); await mkdir(output,{recursive:true});
const executable=path.join(root,'artifacts','WebLauncher','win-x64','Viewcut.Web.exe');
const pw=await import(pathToFileURL(process.env.VIEWCUT_PLAYWRIGHT || path.resolve(path.dirname(process.execPath),'../node_modules/playwright/index.mjs')).href);
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function until(action,timeout=15000) { const end=Date.now()+timeout;while(Date.now()<end){try{const result=await action();if(result)return result;}catch{}await wait(100);}throw new Error('Timed out'); }
const processes=[],browsers=[];
try {
 for(const name of ['chrome','edge']) {
  const profile=path.join(output,name+'-profile'),ready=path.join(output,name+'-ready.txt'),pidFile=path.join(output,name+'-browser-pid.txt');
  const port=name==='chrome'?14871:14872;
  const child=spawn(executable,['--browser',name,'--profile',profile,'--headless','--port',String(port),'--idle-seconds','3','--ready-file',ready,'--browser-pid-file',pidFile],{windowsHide:true,stdio:'ignore'});processes.push(child);
  const launch=await until(async()=> (await readFile(ready,'utf8')).split('\n'));
  assert.equal(launch[2],'started'); const address=launch[0];
  const browserPid=Number(await readFile(pidFile,'utf8'));
  const debug=await until(async()=> (await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n'));
  const browser=await pw.chromium.connectOverCDP(`http://127.0.0.1:${debug[0]}`); browsers.push(browser);
  const page=await until(async()=> browser.contexts()[0].pages().find(p=>p.url().startsWith(address)));
  await page.locator('[data-action=openFolder]').waitFor();
  await wait(4100); assert.equal(child.exitCode,null,'a live page keeps the service alive');
  const repeatedReady=path.join(output,name+'-repeat.txt');
  execFileSync(executable,['--browser','none','--port',String(port),'--ready-file',repeatedReady],{windowsHide:true,timeout:5000});
  const repeat=(await readFile(repeatedReady,'utf8')).split('\n'); assert.equal(repeat[0],address);assert.equal(repeat[2],'reused');
  assert.equal((await fetch(address+'/../Program.cs')).status,404); assert.equal((await fetch(address+'/__viewcut/runtime')).status,200);
  await page.close(); await browser.close();
  await until(()=>child.exitCode!==null,10000);assert.equal(child.exitCode,0);
  console.log(`PASS ${name}: exe auto-opened installed browser, embedded UI, reused server, clean idle exit`);
 }
} finally {for(const b of browsers) await b.close().catch(()=>{});for(const p of processes) if(p.exitCode===null)p.kill();}
