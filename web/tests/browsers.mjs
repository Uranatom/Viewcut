import { pathToFileURL,fileURLToPath } from 'node:url';
import { readFile,writeFile,mkdir } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { serve } from '../server.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const output = path.join(root,'artifacts','web-browsers'); await mkdir(output,{recursive:true});
const playwrightPath = process.env.VIEWCUT_PLAYWRIGHT || path.resolve(path.dirname(process.execPath),'../node_modules/playwright/index.mjs');
const { chromium } = await import(pathToFileURL(playwrightPath).href);
const { server,url } = await serve({port:0});
const fixture = await readFile(path.join(root,'artifacts','web-media','相机 素材.mp4'));
const hevc = await readFile(path.join(root,'artifacts','web-media','camera.mov'));
const browsers = [
  ['Chrome',path.join(process.env.ProgramFiles,'Google','Chrome','Application','chrome.exe')],
  ['Edge',path.join(process.env['ProgramFiles(x86)'],'Microsoft','Edge','Application','msedge.exe')],
];
const results = [];
try {
  for (const [name,executablePath] of browsers) {
    const browser = await chromium.launch({executablePath,headless:true});
    try {
      const context = await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1});
      const page = await context.newPage(), errors = [];
      page.on('pageerror',error => errors.push(error.message));
      page.on('console',message => { if (message.type() === 'error') console.log(name + ' console: ' + message.text()); });
      page.on('crash',() => console.log(name + ' page crashed'));
      // Only the native folder picker is substituted. All directory handles and
      // streams below are real browser File System Access objects, stored in OPFS.
      await page.addInitScript(() => { window.showDirectoryPicker = async options => { const storage = await navigator.storage.getDirectory(); return storage.getDirectoryHandle(options.id === 'viewcut-destination' ? 'Exports' : 'Camera',{create:true}); }; });
      await page.goto(url);
      await page.evaluate(async data => {
        const root = await navigator.storage.getDirectory(), directory = await root.getDirectoryHandle('Camera',{create:true});
        for (const [name,b64] of Object.entries(data)) { const file = await directory.getFileHandle(name,{create:true}), stream = await file.createWritable(); await stream.write(Uint8Array.from(atob(b64),c => c.charCodeAt(0))); await stream.close(); }
        await directory.getDirectoryHandle('Saved',{create:true});
      },{'Camera sunset.mp4':fixture.toString('base64'),'Camera mountains.mp4':fixture.toString('base64'),'HEVC sample.mov':hevc.toString('base64')});
      await page.locator('[data-action=openFolder]').click();
      await page.locator('.file-row').filter({hasText:'Camera sunset.mp4'}).waitFor();
      await page.locator('.file-row').filter({hasText:'Camera sunset.mp4'}).click();
      assert.equal(await page.locator('#player').evaluate(e => !!e.currentSrc),false,'single click does not load media');
      await page.locator('.file-row').filter({hasText:'Camera sunset.mp4'}).dblclick();
      await page.waitForFunction(() => !document.querySelector('[data-action=markIn]').disabled);
      async function seek(time) { await page.locator('#player').evaluate((e,time) => { e.currentTime = time; },time); await page.waitForFunction(time => Math.abs(document.querySelector('#player').currentTime-time) < .1,time); }
      for (const [start,end] of [[1,3],[4.4,6.8]]) { await seek(start); await page.locator('[data-action=markIn]').click(); await seek(end); await page.locator('[data-action=markOut]').click(); }
      assert.equal(await page.locator('.segment').count(),2);
      assert.equal(await page.locator('.job-card').count(),0,'marking out does not export');
      await page.locator('.segment').first().click({button:'right'});
      await page.getByRole('menuitem',{name:'重命名'}).click();
      await page.locator('#clipName').fill('Sunset'); await page.locator('[data-action=confirmRename]').click();
      assert.ok((await page.locator('.segment-label').allTextContents()).includes('Sunset'));
      await page.locator('.segment').nth(1).click({button:'right'}); await page.getByRole('menuitem',{name:'重命名'}).click(); await page.locator('#clipName').fill('sunset'); await page.locator('[data-action=confirmRename]').click();
      assert.ok(await page.locator('#renameError').textContent()); await page.locator('[data-action=cancelRename]').click();
      // One drag is one undo step and the playhead follows the handle.
      const handle = await page.locator('.segment').first().locator('.handle.end').boundingBox();
      await page.mouse.move(handle.x + 4,handle.y + 15); await page.mouse.down(); await page.mouse.move(handle.x + 60,handle.y + 15,{steps:12}); await page.mouse.up();
      await page.locator('#timelineViewport').focus(); await page.keyboard.press('Control+z'); await page.keyboard.press('Control+Shift+z');
      const timeline = await page.locator('#track').boundingBox(); await page.mouse.move(timeline.x + timeline.width*.5,timeline.y + 52);
      await page.waitForFunction(() => !document.querySelector('#preview').hidden);
      await page.waitForFunction(() => { const c = document.querySelector('#preview canvas'); return c.getContext('2d').getImageData(0,0,c.width,c.height).data.some((n,i) => i % 4 !== 3 && n > 10); });
      const before = await page.locator('#sidebar').evaluate(e => e.clientWidth), divider = await page.locator('#verticalDivider').boundingBox();
      await page.mouse.move(divider.x+3,divider.y+100); await page.mouse.down(); await page.mouse.move(divider.x-90,divider.y+100,{steps:20}); await page.mouse.up();
      assert.ok(await page.locator('#sidebar').evaluate(e => e.clientWidth) > before + 50);
      const horizontal = await page.locator('#horizontalDivider').boundingBox(); await page.mouse.move(horizontal.x+100,horizontal.y+3); await page.mouse.down(); await page.mouse.move(horizontal.x+100,horizontal.y-60,{steps:15}); await page.mouse.up();
      await page.locator('.segment').first().click(); await page.locator('.segment').nth(1).click({modifiers:['Control']}); await page.keyboard.press('Delete'); assert.equal(await page.locator('.segment').count(),0); await page.keyboard.press('Control+z'); assert.equal(await page.locator('.segment').count(),2);
      await page.locator('[data-action=destination]').click(); await page.locator('[data-action=export]').click();
      await page.waitForFunction(() => document.querySelectorAll('.job-card.done').length === 2,{},{timeout:30000});
      const exports = await page.evaluate(async () => { const root = await navigator.storage.getDirectory(), directory = await root.getDirectoryHandle('Exports'), files = []; for await (const [name,handle] of directory.entries()) { const data = new Uint8Array(await (await handle.getFile()).arrayBuffer()); let binary = ''; for (const b of data) binary += String.fromCharCode(b); files.push({name,data:btoa(binary)}); } return files; });
      assert.ok(exports.some(file => file.name === 'Sunset.mp4'));
      for (const file of exports) { const filename = path.join(output,name+'-'+file.name); await writeFile(filename,Buffer.from(file.data,'base64')); execFileSync(path.join(root,'.tools','ffmpeg','ffmpeg.exe'),['-v','error','-xerror','-i',filename,'-f','null','-'],{windowsHide:true}); }
      // File browser multi-selection and actual browser filesystem operations.
      await page.locator('[data-action=files]').click(); const rows = page.locator('.file-row');
      await rows.filter({hasText:'Camera mountains.mp4'}).click(); await rows.filter({hasText:'Camera sunset.mp4'}).click({modifiers:['Control']}); assert.equal(await page.locator('.file-row.selected').count(),2);
      await page.keyboard.press('Control+c'); await rows.filter({hasText:'Saved'}).dblclick(); await page.locator('#fileList').focus(); await page.keyboard.press('Control+v'); await page.waitForFunction(() => document.querySelectorAll('.file-row').length === 2);
      await page.locator('.file-row').first().click(); await page.keyboard.press('Control+a'); assert.equal(await page.locator('.file-row.selected').count(),2);
      page.once('dialog',dialog => dialog.accept()); await page.keyboard.press('Delete'); await page.waitForFunction(() => !document.querySelector('.file-row'));
      await page.locator('[data-action=up]').click(); await page.locator('.file-row').filter({hasText:'Camera sunset.mp4'}).waitFor();
      await page.locator('[data-action=refresh]').click(); await page.locator('.file-row.playing').filter({hasText:'Camera sunset.mp4'}).waitFor();
      await page.locator('[data-action=grid]').click(); await page.locator('.file-row img.thumbnail').first().waitFor({timeout:15000}); await page.locator('[data-action=list]').click();
      if (process.env.VIEWCUT_LARGE_FIXTURE) {
        const source = process.env.VIEWCUT_LARGE_FIXTURE;
        await page.locator('#fileInput').setInputFiles(source);
        await page.waitForFunction(name => document.querySelector('#activeName').textContent === name && !document.querySelector('[data-action=markIn]').disabled,path.basename(source));
        await seek(123); await page.locator('[data-action=markIn]').click(); await seek(126); await page.locator('[data-action=markOut]').click();
        await page.locator('[data-action=export]').click(); await page.waitForFunction(() => document.querySelectorAll('.job-card.done').length === 1,{},{timeout:30000});
        const expectedName = path.basename(source).replace(/\.[^.]+$/,'') + '_001.mp4';
        const exported = await page.evaluate(async expectedName => { const root = await navigator.storage.getDirectory(),directory = await root.getDirectoryHandle('Exports'),file = await (await directory.getFileHandle(expectedName)).getFile(); let binary = ''; for (const b of new Uint8Array(await file.arrayBuffer())) binary += String.fromCharCode(b); return btoa(binary); },expectedName);
        const filename = path.join(output,`${name}-large.mp4`); await writeFile(filename,Buffer.from(exported,'base64')); execFileSync(path.join(root,'.tools','ffmpeg','ffmpeg.exe'),['-v','error','-xerror','-i',filename,'-f','null','-'],{windowsHide:true});
        console.log(`PASS ${name}: large source ${path.basename(source)}`);
      }
      await page.locator('[data-action=preferences]').click(); await page.locator('#language').selectOption('en-US'); await page.locator('#theme').selectOption('light'); await page.locator('#preferences [data-action=close]').click();
      for (const scale of [1,1.25,1.5,1.75,2]) {
        const scaledContext = await browser.newContext({viewport:{width:Math.round(1440/scale),height:Math.round(900/scale)},deviceScaleFactor:scale});
        const scaled = await scaledContext.newPage();
        await scaled.addInitScript(() => localStorage.setItem('viewcut.preferences',JSON.stringify({language:'en-US',theme:'light',sidebar:480,timeline:290})));
        await scaled.goto(url); await scaled.locator('#fileInput').setInputFiles(path.join(root,'artifacts','web-media','相机 素材.mp4'));
        await scaled.waitForFunction(() => !document.querySelector('[data-action=markIn]').disabled);
        await scaled.locator('#player').evaluate(e => e.currentTime = 1); await scaled.locator('[data-action=markIn]').click(); await scaled.locator('#player').evaluate(e => e.currentTime = 4); await scaled.locator('[data-action=markOut]').click();
        assert.equal(await scaled.evaluate(() => devicePixelRatio),scale);
        const sizes = await scaled.evaluate(() => { const ids = ['#editor','#sidebar','#timelinePanel','.appbar']; return ids.map(id => { const e = document.querySelector(id),r = e.getBoundingClientRect(); return {id,width:r.width,height:r.height,scrollWidth:e.scrollWidth,clientWidth:e.clientWidth,bottom:r.bottom}; }); });
        for (const item of sizes) { assert.ok(item.width > 0 && item.height > 0,JSON.stringify(item)); assert.ok(item.scrollWidth <= item.clientWidth+2,`${name} ${scale}: ${JSON.stringify(item)}`); assert.ok(item.bottom <= Math.round(900/scale),`panel exceeds window: ${JSON.stringify(item)}`); }
        assert.ok(await scaled.evaluate(() => document.documentElement.scrollWidth <= innerWidth),'page width fits');
        await scaled.screenshot({path:path.join(output,`${name}-light-${scale*100}.png`)});
        await scaledContext.close();
      }
      await page.locator('[data-action=preferences]').click(); await page.locator('#theme').selectOption('dark'); await page.locator('#preferences [data-action=close]').click();
      await page.screenshot({path:path.join(output,`${name}-dark.png`)});
      await page.reload(); await page.waitForFunction(() => document.documentElement.lang === 'en-US'); assert.equal(await page.locator('html').getAttribute('data-theme'),'dark');
      assert.deepEqual(errors,[]); results.push({name,version:browser.version(),status:'passed',exports:exports.map(f=>f.name)}); console.log(`PASS ${name} ${browser.version()}`);
    } finally { await browser.close(); }
  }
} finally { server.close(); await writeFile(path.join(output,'results.json'),JSON.stringify(results,null,2)); }
