import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { serve } from '../server.mjs';

const windows = process.platform === 'win32';
const tools = process.env.VIEWCUT_TEST_TOOLS || (windows ? '.tools/ffmpeg' : '');
const ffmpeg = tools ? path.resolve(tools, windows ? 'ffmpeg.exe' : 'ffmpeg') : 'ffmpeg';
const output = path.resolve('artifacts/web-formats');
await mkdir(output, {recursive:true});
const fixtures = [
  ['mp4','libx264','aac'], ['mov','libx264','aac'], ['m4v','libx264','aac'],
  ['MKV','libx264','aac'], ['webm','libvpx-vp9','libopus'], ['avi','mpeg4','libmp3lame'],
  ['ts','libx264','aac'], ['m2ts','libx264','ac3'],
];
function ff(args) { execFileSync(ffmpeg, ['-v','error', ...args], {windowsHide:true}); }
for (const [ext, video, audio] of fixtures) {
  ff(['-y','-f','lavfi','-i','testsrc2=size=160x90:rate=24','-f','lavfi','-i','sine=frequency=440',
    '-t','3','-c:v',video,'-g','24','-c:a',audio,...(ext === 'm4v' ? ['-f','mp4'] : []),path.join(output,'sample.'+ext)]);
}

const { chromium, webkit } = await import(pathToFileURL(process.env.VIEWCUT_PLAYWRIGHT ||
  path.resolve('web/node_modules/playwright/index.mjs')).href);
const browsers = [
  ['Chrome',chromium,windows ? path.join(process.env.ProgramFiles,'Google/Chrome/Application/chrome.exe') : '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'],
  ['Edge',chromium,windows ? path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe') : '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge'],
  ...(!windows ? [['WebKit',webkit,undefined]] : []),
];
const host = process.env.VIEWCUT_FORMAT_TEST_URL ? null : await serve({port:0});
try {
  for (const [name, engine, executablePath] of browsers) {
    const browser = await engine.launch({headless:true, executablePath});
    try {
      const page = await browser.newPage({acceptDownloads:true});
      page.on('console', message => { if (message.type() === 'error') console.log(name, message.text()); });
      await page.addInitScript(() => { window.showDirectoryPicker = undefined; });
      await page.goto(host?.url ?? process.env.VIEWCUT_FORMAT_TEST_URL);
      for (const [ext] of fixtures) {
        const clip = 'sample_001.' + ext.toLowerCase();
        await page.locator('#fileInput').setInputFiles(path.join(output, 'sample.' + ext));
        try {
          await page.waitForFunction(filename => document.querySelector('#activeName').textContent === filename &&
            !document.querySelector('[data-action=markIn]').disabled && document.querySelector('#player').readyState >= 2,
            'sample.' + ext, {timeout:60000});
          for (const [time,action] of [[.6,'markIn'],[1.8,'markOut']]) {
            await page.locator('#player').evaluate((video, time) => { video.currentTime = time; }, time);
            await page.waitForFunction(time => { const video = document.querySelector('#player'); return !video.seeking && Math.abs(video.currentTime - time) < .1; }, time);
            await page.locator(`[data-action=${action}]`).click();
          }
          await page.locator('[data-action=export]').click();
          await page.locator('#downloadDialog[open]').waitFor({timeout:60000});
        } catch (error) {
          throw new Error(`${name} ${ext}: ${await page.locator('#status').textContent()}`, {cause:error});
        }
        const promise = page.waitForEvent('download');
        await page.locator('[data-action=downloadZip]').click();
        const archive = await readFile(await (await promise).path());
        assert.equal(archive.readUInt32LE(0), 0x04034b50);
        const nameLength = archive.readUInt16LE(26), dataStart = 30 + nameLength + archive.readUInt16LE(28);
        assert.equal(archive.toString('utf8', 30, 30 + nameLength), clip);
        // The local ZIP64 extra field holds the stored entry size.
        const extra = 30 + nameLength;
        assert.equal(archive.readUInt16LE(extra), 1);
        const size = Number(archive.readBigUInt64LE(extra + 4));
        const exported = path.join(output, name + '-' + clip);
        await writeFile(exported, archive.subarray(dataStart, dataStart + size));
        ff(['-xerror','-i',exported,'-f','null','-']);
        console.log(`PASS ${name}: ${ext} import, playback, cut and valid original-format export`);
      }
    } finally { await browser.close(); }
  }
} finally { host?.server.close(); }
