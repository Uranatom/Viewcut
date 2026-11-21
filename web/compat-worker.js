/* global createFFmpegCore */
// Read the original File in slices through WORKERFS. Only the current output
// occupies FFmpeg's memory filesystem. Bound it to leave room for decoding.
const outputLimit = 256 * 1024 * 1024;
const output = '/clip';
let core;

async function initialize(file) {
  try {
    importScripts('./ffmpeg-core.js');
    const wasmURL = new URL('./ffmpeg-core.wasm', self.location.href).href;
    core = await createFFmpegCore({
      mainScriptUrlOrBlob: self.location.href + '#' + btoa(JSON.stringify({wasmURL})),
    });
  } catch (error) {
    console.error(error);
    throw new Error('compatUnavailable');
  }
  core.FS.mkdir('/input');
  core.FS.mount(core.FS.filesystems.WORKERFS, {blobs:[{name:'source', data:file}]}, '/input');
}

function cut({clip, muxer, extension, mime}) {
  return render([
    '-ss', String(clip.start), '-i', '/input/source', '-t', String(clip.end - clip.start),
    '-map', '0:V:0', '-map', '0:a?', '-map_chapters', '-1',
    '-c', 'copy', '-avoid_negative_ts', 'make_zero',
    ...(extension === 'm2ts' ? ['-mpegts_m2ts_mode', '1'] : []),
  ], muxer, mime, true);
}

function preview() {
  return render([
    '-i', '/input/source', '-map', '0:V:0', '-map', '0:a:0?', '-map_chapters', '-1',
    '-vf', "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2",
    '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-crf', '25',
    '-c:a', 'aac', '-b:a', '96k',
  ], 'mp4', 'video/mp4');
}

function render(args, muxer, mime, checkKeyframe = false) {
  try {
    const result = core.exec('-hide_banner', '-loglevel', 'error', ...args,
      '-fs', String(outputLimit), '-f', muxer, output);
    core.reset();
    const info = core.FS.analyzePath(output);
    if (info.exists && info.object.usedBytes >= outputLimit - 1024 * 1024) throw new Error('compatTooLarge');
    if (result || !info.exists || !info.object.usedBytes) throw new Error('compatFailed');
    if (checkKeyframe) {
      core.ffprobe('-v', 'error', '-select_streams', 'V:0',
        '-read_intervals', '%+#1', '-show_entries', 'packet=flags', '-of', 'csv=p=0',
        '-o', '/flags', output);
      if (!core.FS.analyzePath('/flags').exists ||
          !core.FS.readFile('/flags', {encoding:'utf8'}).trimStart().startsWith('K')) {
        throw new Error('noKeyframe');
      }
    }
    return new Blob([core.FS.readFile(output)], {type:mime});
  } finally {
    core.reset();
    for (const path of [output, '/flags']) if (core.FS.analyzePath(path).exists) core.FS.unlink(path);
  }
}

self.onmessage = async ({data}) => {
  try {
    if (data.file) { await initialize(data.file); self.postMessage({}); return; }
    self.postMessage({blob:data.clip ? cut(data) : preview()});
  } catch (error) {
    console.error(error);
    self.postMessage({error:['compatUnavailable','compatTooLarge','noKeyframe'].includes(error.message) ? error.message : 'compatFailed'});
  }
};
