import { pathToFileURL,fileURLToPath } from 'node:url';
import path from 'node:path';
import { mkdir,writeFile,copyFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { serve } from '../server.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const windows=process.platform==='win32',tools=process.env.VIEWCUT_TEST_TOOLS || path.join(root,'.tools','ffmpeg');
const ffmpeg=path.join(tools,windows?'ffmpeg.exe':'ffmpeg');
const output=path.join(root,'artifacts','web-safari'); await mkdir(path.join(output,'Trip','Day 1'),{recursive:true});
// Baseline H.264 is used to avoid mistaking engine-specific VP9/HEVC support
// for page compatibility. Actual macOS Safari acceptance remains separate.
const sample=path.join(output,'Trip','Day 1','camera.mp4');
execFileSync(ffmpeg,['-v','error','-y','-f','lavfi','-i','testsrc2=size=320x180:rate=24','-t','6','-c:v','libx264','-profile:v','baseline','-pix_fmt','yuv420p','-g','24','-an',sample],{windowsHide:true});
await copyFile(sample,path.join(output,'Trip','second.mp4'));
const pw=await import(pathToFileURL(process.env.VIEWCUT_PLAYWRIGHT || path.resolve(path.dirname(process.execPath),'../node_modules/playwright/index.mjs')).href);
const {server,url}=await serve({port:0});
const chrome=process.env.VIEWCUT_SAFARI_ENGINE==='chrome';
const browser=await (chrome?pw.chromium:pw.webkit).launch({headless:true,...(chrome?{executablePath:path.join(process.env.ProgramFiles,'Google','Chrome','Application','chrome.exe')}:{})});
try {
 const context=await browser.newContext({viewport:{width:1440,height:900},acceptDownloads:true});
 const page=await context.newPage(),errors=[]; page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{ Object.defineProperty(navigator,'platform',{get:()=> 'MacIntel'}); window.showDirectoryPicker=undefined; });
 await page.goto(url); await page.locator('#folderInput').setInputFiles(path.join(output,'Trip'));
 await page.locator('.file-row').filter({hasText:'Day 1'}).dblclick(); await page.locator('.file-row').filter({hasText:'camera.mp4'}).dblclick();
 if(windows&&!chrome) {
   // This Windows WebKit build advertises MP4 but fails native decoding. Verify
   // the real Worker/ZIP path without substituting media element behavior.
   const archive=await page.evaluate(async()=>new Promise((resolve,reject)=>{
     const file=[...document.querySelector('#folderInput').files].find(f=>f.name==='camera.mp4');
     const worker=new Worker(new URL('./export-worker.js',location.href),{type:'module'});
     worker.onmessage=async({data})=>{if(data.type==='error'){worker.terminate();reject(new Error(data.key));}if(data.type==='complete'){let binary='';for(const b of new Uint8Array(await data.archive.arrayBuffer()))binary+=String.fromCharCode(b);worker.terminate();resolve(btoa(binary));}};
     worker.onerror=reject;worker.postMessage({file,clips:[{id:'1',number:1,start:.5,end:1.8,name:'日落'},{id:'2',number:2,start:2.5,end:4.1,name:''}],directory:null});
   }));
   await writeFile(path.join(output,'WebKit-worker.zip'),Buffer.from(archive,'base64'));
   await page.locator('[data-action=preferences]').click();await page.locator('#language').selectOption('en-US');await page.locator('#theme').selectOption('light');await page.locator('#preferences [data-action=close]').click();
   await page.screenshot({path:path.join(output,'WebKit-Windows-import.png')});assert.deepEqual(errors,[]);
   console.log('PASS Windows WebKit: folder import, real Worker remux + ZIP, preferences; native video playback unavailable');
 } else {
 await page.waitForFunction(()=>!document.querySelector('[data-action=markIn]').disabled).catch(async error=>{console.log(await page.evaluate(()=>({status:document.querySelector('#status').textContent,media:document.querySelector('#player').error?.message,src:document.querySelector('#player').currentSrc,active:document.querySelector('#activeName').textContent})));throw error;});
 for(const [start,end] of [[.5,1.8],[2.5,4.1]]) { await page.locator('#player').evaluate((e,t)=>e.currentTime=t,start); await page.locator('[data-action=markIn]').click(); await page.locator('#player').evaluate((e,t)=>e.currentTime=t,end); await page.locator('[data-action=markOut]').click(); }
 assert.equal(await page.locator('.segment').count(),2);
 await page.locator('.segment').first().click(); await page.locator('.segment').nth(1).click({modifiers:['Meta']}); assert.equal(await page.locator('.segment.selected').count(),2);
 await page.keyboard.press('Backspace'); assert.equal(await page.locator('.segment').count(),0);
 await page.keyboard.press('Meta+z'); assert.equal(await page.locator('.segment').count(),2);
 await page.keyboard.press('Meta+Shift+z'); assert.equal(await page.locator('.segment').count(),0); await page.keyboard.press('Meta+z');
 const track=await page.locator('#track').boundingBox(); await page.mouse.move(track.x+track.width*.4,track.y+50);
 await page.waitForFunction(()=>!document.querySelector('#preview').hidden);
 await page.waitForFunction(()=>{ const c=document.querySelector('#preview canvas'); return c.getContext('2d').getImageData(0,0,c.width,c.height).data.some((n,i)=>i%4!==3&&n>10); });
 await page.locator('[data-action=export]').click(); await page.locator('#downloadDialog[open]').waitFor({timeout:30000});
 const promise=page.waitForEvent('download'); await page.locator('[data-action=downloadZip]').click(); const download=await promise; await download.saveAs(path.join(output,'clips.zip'));
 await page.locator('[data-action=up]').click(); await page.locator('.file-row').filter({hasText:'second.mp4'}).waitFor();
 await page.locator('.file-row').filter({hasText:'second.mp4'}).click({button:'right'}); assert.ok(await page.getByRole('menuitem',{name:'删除'}).isDisabled()); await page.keyboard.press('Escape');
 await page.locator('[data-action=preferences]').click(); await page.locator('#language').selectOption('en-US'); await page.locator('#theme').selectOption('light'); await page.locator('#preferences [data-action=close]').click();
 await page.screenshot({path:path.join(output,'WebKit-mac-light.png')}); assert.deepEqual(errors,[]);
 console.log(`PASS ${chrome?'Chrome fallback':'macOS WebKit'}: Mac shortcuts, read-only directory import, hover preview, ZIP export, preferences`);
 }
 await writeFile(path.join(output,`results-${chrome?'chrome-fallback':'webkit'}.json`),JSON.stringify({engine:browser.version(),platform:process.platform,status:'passed',mediaPlayback:!windows||chrome,actualSafariOnMac:'not tested'},null,2));
} finally {await browser.close();server.close();}
