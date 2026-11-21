import { indexFile, planCut, writeCut } from './mp4.js';
import { outputName } from './model.js';
import { zipClips } from './portable.js';
import { videoFormat } from './formats.js';
import { mediaSession } from './compat-media.js';
let controller;
self.onmessage = async ({ data }) => {
  if (data.type === 'cancel') { controller?.abort(); return; }
  controller = new AbortController(); const signal = controller.signal;
  let currentName = null, created = false, complete = 0, session;
  try {
    const { file, clips, directory } = data;
    self.postMessage({ type:'indexing' });
    const format = videoFormat(file.name);
    if (!format) throw new Error('unsupportedFile');
    let index;
    if (['mp4','mov'].includes(format.muxer)) {
      try { index = await indexFile(file, signal); }
      catch (error) {
        if (!['fragmentedMp4','unsupportedTracks','complexEdits'].includes(error.message)) throw error;
      }
    }
    // Validate the whole batch before creating the first output.
    const names = new Set();
    if (directory) for await (const name of directory.keys()) names.add(name.toLocaleLowerCase());
    for (const clip of clips) {
      const name = outputName(file.name, clip).toLocaleLowerCase();
      if (names.has(name)) throw new Error('existingOutput');
      names.add(name);
      if (!Number.isFinite(clip.start) || !Number.isFinite(clip.end) || clip.start < 0 || clip.end <= clip.start) throw new Error('invalidRange');
      if (index) planCut(index, clip.start, clip.end);
    }
    if (!index) {
      session = mediaSession(file, signal);
      await session.ready;
    }
    const downloads = [], prepared = [];
    for (let i = 0; i < clips.length; i++) {
      signal.throwIfAborted(); const clip = clips[i]; currentName = outputName(file.name, clip); created = false;
      self.postMessage({ type:'running', id:clip.id, name:currentName });
      const plan = index ? planCut(index, clip.start, clip.end) : null;
      const blob = !plan ? await session.request({clip, ...format}) : null;
      const result = {type:'done', id:clip.id, name:currentName, actualStart:plan?.actualStart, end:plan?.end};
      if (!directory) {
        downloads.push({name:currentName,blob:blob ?? new Blob([plan.header,...plan.ranges.map(r => file.slice(r.start,r.end))],{type:format.mime})});
        prepared.push(result);
        continue;
      }
      signal.throwIfAborted();
      try { await directory.getFileHandle(currentName); throw new Error('existingOutput'); } catch (e) { if (e.name !== 'NotFoundError') throw e; }
      const handle = await directory.getFileHandle(currentName, { create:true }); created = true;
      const writable = await handle.createWritable();
      if (plan) await writeCut(file, plan, writable, { signal, progress:value => self.postMessage({ type:'progress', value:(i + value) / clips.length }) });
      else {
        await blob.stream().pipeTo(writable, {signal});
        self.postMessage({type:'progress', value:(i + 1) / clips.length});
      }
      created = false; complete++;
      self.postMessage(result);
    }
    const archive = directory ? null : await zipClips(downloads,{signal,progress:value => self.postMessage({type:'progress',value})});
    complete += prepared.length;
    for (const message of prepared) self.postMessage(message);
    self.postMessage({ type:'complete', count:complete,archive });
  } catch (error) {
    if (created && currentName) await data.directory.removeEntry(currentName).catch(() => {});
    self.postMessage({ type:'error', key:signal.aborted ? 'cancelled' : error.message, detail:error.name, count:complete });
  } finally { session?.close(); }
};
