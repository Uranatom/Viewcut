import test from 'node:test';
import assert from 'node:assert/strict';
import { zipClips,importedDirectory } from '../portable.js';
import { writeFile,mkdir } from 'node:fs/promises';
import path from 'node:path';
test('imported folders preserve hierarchy and read-only permission',async () => {
  const file = new File(['video'],'camera.mp4'); Object.defineProperty(file,'webkitRelativePath',{value:'Trip/Day 1/camera.mp4'});
  const root = importedDirectory([file]); assert.equal(root.name,'Trip');
  const day = [...root.children.values()][0]; assert.equal(day.name,'Day 1');
  assert.equal(await day.children.get('camera.mp4').getFile(),file);
  assert.equal(await root.queryPermission({mode:'readwrite'}),'denied');
});
test('ZIP64 output preserves names, payloads and cancellation',async () => {
  const archive = await zipClips([{name:'日落.mp4',blob:new Blob(['first clip'])},{name:'Mountain.mov',blob:new Blob(['second clip'])}]);
  await mkdir(path.resolve('artifacts/web-portable'),{recursive:true}); await writeFile(path.resolve('artifacts/web-portable/fixture.zip'),new Uint8Array(await archive.arrayBuffer()));
  const data = new DataView(await archive.arrayBuffer()); assert.equal(data.getUint32(0,true),0x04034b50);
  const controller = new AbortController(); controller.abort(); await assert.rejects(zipClips([{name:'x.mp4',blob:new Blob(['x'])}],{signal:controller.signal}),{name:'AbortError'});
});
