import {spawn,execFileSync} from 'node:child_process';
import {readFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),rid=process.arch==='arm64'?'osx-arm64':'osx-x64';
const output=path.join(root,'artifacts','mac-launcher-tests');await mkdir(output,{recursive:true});
const binary=path.join(root,'artifacts','WebLauncher',rid,'Viewcut.Web'),ready=path.join(output,'ready.txt');
const pw=await import(pathToFileURL(process.env.VIEWCUT_PLAYWRIGHT).href);
const child=spawn(binary,['--browser','none','--port','14873','--idle-seconds','3','--ready-file',ready],{stdio:'inherit'});
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));async function until(action){for(let i=0;i<150;i++){try{const value=await action();if(value)return value;}catch{}await wait(100);}throw new Error('Timed out');}
let browser;
try{
 const launch=await until(async()=> (await readFile(ready,'utf8')).split('\n'));
 browser=await pw.webkit.launch();const page=await browser.newPage();await page.goto(launch[0]);await page.locator('[data-action=openFolder]').waitFor();
 await wait(4100);assert.equal(child.exitCode,null);
 const repeated=path.join(output,'repeat.txt');execFileSync(binary,['--browser','none','--port','14873','--ready-file',repeated]);assert.ok((await readFile(repeated,'utf8')).endsWith('reused'));
 await page.close();await browser.close();await until(()=>child.exitCode!==null);assert.equal(child.exitCode,0);
 console.log(`PASS native macOS ${process.arch}: embedded assets, WebKit page, repeat launch and idle exit`);
}finally{await browser?.close().catch(()=>{});if(child.exitCode===null)child.kill();}
