import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { openAsBlob } from 'node:fs';
import { mkdir,open,readFile,writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { indexFile,planCut,writeCut } from '../mp4.js';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const directory = path.join(root,'artifacts','web-media');
const tools = process.env.VIEWCUT_TEST_TOOLS || path.join(root,'.tools','ffmpeg');
const ffmpeg = path.join(tools,process.platform === 'win32' ? 'ffmpeg.exe' : 'ffmpeg'), ffprobe = path.join(tools,process.platform === 'win32' ? 'ffprobe.exe' : 'ffprobe');
function ff(...args) { return execFileSync(ffmpeg,['-v','error',...args],{windowsHide:true,timeout:60000}).toString(); }
function probe(filename) { return JSON.parse(execFileSync(ffprobe,['-v','error','-show_format','-show_streams','-of','json',filename],{windowsHide:true}).toString()); }
async function saveCut(file,plan,filename) { const handle = await open(filename,'w'); await writeCut(file,plan,{write:async data => handle.write(data instanceof Blob ? new Uint8Array(await data.arrayBuffer()) : data),close:() => handle.close(),abort:() => handle.close()}); }
await mkdir(directory,{recursive:true});
test('H264 / AAC cuts decode, preserve packet payloads, and start at a keyframe',async () => {
  const source = path.join(directory,'相机 素材.mp4');
  ff('-y','-f','lavfi','-i','testsrc2=size=640x360:rate=30','-f','lavfi','-i','sine=sample_rate=48000','-t','9','-c:v','libx264','-g','120','-sc_threshold','0','-c:a','aac',source);
  const file = await openAsBlob(source), index = await indexFile(file);
  assert.equal(index.tracks.length,2);
  for (const [start,end] of [[1.8,4.2],[4.5,6.8],[0,.3],[8.5,9]]) {
    const plan = planCut(index,start,end), output = path.join(directory,`cut-${start}.mp4`); await saveCut(file,plan,output);
    ff('-xerror','-i',output,'-f','null','-');
    const info = probe(output); assert.ok(Math.abs(Number(info.format.duration) - (end-plan.actualStart)) < .2,JSON.stringify(info.format));
    assert.equal(info.streams[0].codec_name,'h264'); assert.equal(info.streams[1].codec_name,'aac');
    const cutIndex = await indexFile(await openAsBlob(output)); assert.ok(cutIndex.tracks[0].samples[0].sync);
    const original = index.tracks[0].samples.find(s => Math.abs(s.cts/index.tracks[0].scale + index.tracks[0].shift-plan.actualStart) < .001 && s.sync);
    const sample = cutIndex.tracks[0].samples[0], result = await openAsBlob(output);
    assert.deepEqual(new Uint8Array(await file.slice(original.offset,original.offset+original.size).arrayBuffer()),new Uint8Array(await result.slice(sample.offset,sample.offset+sample.size).arrayBuffer()));
  }
});
test('HEVC MOV and fast-start MP4 remain decodable without re-encoding',async () => {
  for (const format of ['mov','mp4']) {
    const source = path.join(directory,`camera.${format}`);
    ff('-y','-f','lavfi','-i','testsrc2=size=640x360:rate=30','-t','4','-c:v',format === 'mov' ? 'libx265' : 'libx264','-preset','ultrafast','-g','30',...(format === 'mov' ? ['-tag:v','hvc1','-x265-params','pools=2:log-level=error'] : ['-movflags','+faststart']),'-an',source);
    const file = await openAsBlob(source), index = await indexFile(file), output = path.join(directory,`cut.${format}`);
    await saveCut(file,planCut(index,1.4,2.5),output); ff('-xerror','-i',output,'-f','null','-');
    assert.equal(probe(output).streams[0].codec_name,format === 'mov' ? 'hevc' : 'h264');
  }
});
test('metadata access skips a large mdat, and cancellation aborts output',async () => {
  const source = await openAsBlob(path.join(directory,'相机 素材.mp4')); let readBytes = 0;
  const measured = {size:source.size,slice:(start,end) => {readBytes += Math.min(source.size,end)-start; return source.slice(start,end);}};
  const index = await indexFile(measured); assert.ok(readBytes < source.size/4,`read ${readBytes}/${source.size}`);
  const controller = new AbortController(); let aborted = false;
  await assert.rejects(writeCut(source,planCut(index,0,1),{write:async () => controller.abort(),close:async () => {},abort:async () => aborted = true},{signal:controller.signal}),{name:'AbortError'});
  assert.ok(aborted);
  await assert.rejects(indexFile(new Blob([new Uint8Array(32)])),/invalidMp4/);
});
test('64-bit source offsets beyond 4 GB are indexed and copied without allocating the gap',async () => {
  const original = await readFile(path.join(directory,'相机 素材.mp4')), padding = 2 ** 32 + 16;
  const atom = (type,payload) => { const result = Buffer.alloc(8+payload.length); result.writeUInt32BE(result.length); result.write(type,4); payload.copy(result,8); return result; };
  function shiftOffsets(data) {
    const parts = [];
    for (let at = 0; at < data.length;) {
      const size = data.readUInt32BE(at), type = data.toString('ascii',at+4,at+8), payload = data.subarray(at+8,at+size);
      if (type === 'stco') { const count = payload.readUInt32BE(4), replacement = Buffer.alloc(8+count*8); payload.copy(replacement,0,0,8); for (let i=0;i<count;i++) replacement.writeBigUInt64BE(BigInt(payload.readUInt32BE(8+i*4)+padding),8+i*8); parts.push(atom('co64',replacement)); }
      else if (['moov','trak','mdia','minf','stbl'].includes(type)) parts.push(atom(type,shiftOffsets(payload)));
      else parts.push(data.subarray(at,at+size));
      at += size;
    }
    return Buffer.concat(parts);
  }
  const ftypSize = original.readUInt32BE(0), rewritten = shiftOffsets(original.subarray(ftypSize));
  const freeHeader = Buffer.alloc(16); freeHeader.writeUInt32BE(1); freeHeader.write('free',4); freeHeader.writeBigUInt64BE(BigInt(padding),8);
  let bytesRead = 0;
  const virtual = {size:ftypSize+padding+rewritten.length,slice(start,end) {
    end = Math.min(end,this.size); const length = end-start; assert.ok(length <= 4*1024*1024,'bounded reads'); bytesRead += length;
    const result = Buffer.alloc(length);
    for (const [offset,data] of [[0,original.subarray(0,ftypSize)],[ftypSize,freeHeader],[ftypSize+padding,rewritten]]) { const from = Math.max(start,offset),to = Math.min(end,offset+data.length); if (to>from) data.copy(result,from-start,from-offset,to-offset); }
    return new Blob([result]);
  }};
  const index = await indexFile(virtual); assert.ok(index.tracks[0].samples[0].offset > 2**32);
  const output = path.join(directory,'64-bit-cut.mp4'); await saveCut(virtual,planCut(index,1,3),output);
  ff('-xerror','-i',output,'-f','null','-'); assert.ok(bytesRead < 2*1024*1024);
});
