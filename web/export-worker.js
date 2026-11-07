import { indexFile, planCut, writeCut } from './mp4.js';
import { outputName } from './model.js';
import { zipClips } from './portable.js';
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
    if (directory) for await (const name of directory.keys()) names.add(name.toLocaleLowerCase());
    for (const clip of clips) { const name = outputName(file.name, clip).toLocaleLowerCase(); if (names.has(name)) throw new Error('existingOutput'); names.add(name); planCut(index, clip.start, clip.end); }
    const downloads = [], prepared = [];
    for (let i = 0; i < clips.length; i++) {
      signal.throwIfAborted(); const clip = clips[i]; currentName = outputName(file.name, clip); created = false;
      self.postMessage({ type:'running', id:clip.id, name:currentName });
      if (!directory) {
        const plan = planCut(index,clip.start,clip.end);
        downloads.push({name:currentName,blob:new Blob([plan.header,...plan.ranges.map(r => file.slice(r.start,r.end))],{type:'video/mp4'})});
        prepared.push({type:'done',id:clip.id,name:currentName,actualStart:plan.actualStart,end:plan.end}); complete++;
        continue;
      }
      try { await directory.getFileHandle(currentName); throw new Error('existingOutput'); } catch (e) { if (e.name !== 'NotFoundError') throw e; }
      const handle = await directory.getFileHandle(currentName, { create:true }); created = true;
      const writable = await handle.createWritable();
      const plan = planCut(index, clip.start, clip.end);
      await writeCut(file, plan, writable, { signal, progress:value => self.postMessage({ type:'progress', value:(i + value) / clips.length }) });
      created = false; complete++;
      self.postMessage({ type:'done', id:clip.id, name:currentName, actualStart:plan.actualStart, end:plan.end });
    }
    const archive = directory ? null : await zipClips(downloads,{signal,progress:value => self.postMessage({type:'progress',value})});
    for (const message of prepared) self.postMessage(message);
    self.postMessage({ type:'complete', count:complete,archive });
  } catch (error) {
    if (created && currentName) await data.directory.removeEntry(currentName).catch(() => {});
    self.postMessage({ type:'error', key:signal.aborted ? 'cancelled' : error.message, detail:error.name, count:complete });
  }
};
