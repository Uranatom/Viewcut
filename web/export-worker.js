import { indexFile, planCut, writeCut } from './mp4.js';
import { outputName } from './model.js';
let controller;
self.onmessage = async ({ data }) => {
  if (data.type === 'cancel') { controller?.abort(); return; }
  controller = new AbortController(); const signal = controller.signal;
  let currentName = null, created = false, complete = 0;
  try {
    const { file, clips, directory } = data;
    self.postMessage({ type:'indexing' });
    const index = await indexFile(file, signal);
    // Validate the whole batch before creating the first output.
    const names = new Set();
    for await (const name of directory.keys()) names.add(name.toLocaleLowerCase());
    for (const clip of clips) { const name = outputName(file.name, clip).toLocaleLowerCase(); if (names.has(name)) throw new Error('existingOutput'); names.add(name); planCut(index, clip.start, clip.end); }
    for (let i = 0; i < clips.length; i++) {
      signal.throwIfAborted(); const clip = clips[i]; currentName = outputName(file.name, clip); created = false;
      self.postMessage({ type:'running', id:clip.id, name:currentName });
      try { await directory.getFileHandle(currentName); throw new Error('existingOutput'); } catch (e) { if (e.name !== 'NotFoundError') throw e; }
      const handle = await directory.getFileHandle(currentName, { create:true }); created = true;
      const writable = await handle.createWritable();
      const plan = planCut(index, clip.start, clip.end);
      await writeCut(file, plan, writable, { signal, progress:value => self.postMessage({ type:'progress', value:(i + value) / clips.length }) });
      created = false; complete++;
      self.postMessage({ type:'done', id:clip.id, name:currentName, actualStart:plan.actualStart, end:plan.end });
    }
    self.postMessage({ type:'complete', count:complete });
  } catch (error) {
    if (created && currentName) await data.directory.removeEntry(currentName).catch(() => {});
    self.postMessage({ type:'error', key:signal.aborted ? 'cancelled' : error.message, detail:error.name, count:complete });
  }
};
