import { readFile, writeFile, mkdtemp, copyFile, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const version = '0.12.10';
const digest = 'd00089ce82e1bdf637ddbe42e0c3d41a1ba8cf4c9e825e7fa4d0bb970e844bd4';
const localArchive = process.argv[2];
let archive;
if (localArchive) archive = await readFile(localArchive);
else {
  const response = await fetch(`https://registry.npmjs.org/@ffmpeg/core/-/core-${version}.tgz`, {signal:AbortSignal.timeout(120000)});
  if (!response.ok) throw new Error(`Could not download @ffmpeg/core: HTTP ${response.status}`);
  archive = Buffer.from(await response.arrayBuffer());
}
if (createHash('sha256').update(archive).digest('hex') !== digest) throw new Error('Unexpected FFmpeg core checksum');

const temp = await mkdtemp(join(tmpdir(), 'viewcut-ffmpeg-'));
try {
  const tarball = join(temp, 'core.tgz');
  await writeFile(tarball, archive);
  execFileSync('tar', ['-xf', tarball, '-C', temp, 'package/dist/umd']);
  for (const name of ['ffmpeg-core.js', 'ffmpeg-core.wasm']) {
    await copyFile(join(temp, 'package', 'dist', 'umd', name), new URL(name, import.meta.url));
  }
  await copyFile(new URL('../media/licenses/GPL-2.0.txt', import.meta.url),
    new URL('./ffmpeg-core-LICENSE.txt', import.meta.url));
} finally {
  await rm(temp, {recursive:true, force:true});
}
