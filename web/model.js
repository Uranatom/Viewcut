export const copy = value => structuredClone(value);
export function validateName(name, clips, id) {
  if (!name) return null;
  if (name.length > 100 || /[<>:"/\\|?*\x00-\x1f]/.test(name) || /[. ]$/.test(name) || /^\.{1,2}$/.test(name)) return 'invalidName';
  if (/^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)/i.test(name)) return 'reservedName';
  if (clips.some(c => c.id !== id && c.name?.toLocaleLowerCase() === name.toLocaleLowerCase())) return 'duplicateName';
  return null;
}
export function outputName(source, clip) {
  const base = source.replace(/\.[^.]+$/, '');
  const ext = /\.mov$/i.test(source) ? '.mov' : '.mp4';
  return (clip.name || `${base}_${String(clip.number).padStart(3, '0')}`) + ext;
}
export class Editor {
  constructor(clips = []) { this.clips = copy(clips); this.undoStack = []; this.redoStack = []; this.selection = new Set(); this.mark = null; }
  snapshot() { return copy(this.clips); }
  record(before) {
    if (JSON.stringify(before) === JSON.stringify(this.clips)) return false;
    this.undoStack.push(before); if (this.undoStack.length > 100) this.undoStack.shift();
    this.redoStack = []; return true;
  }
  edit(action) { const before = this.snapshot(); action(); return this.record(before); }
  add(start, end) {
    if (!Number.isFinite(start + end) || end - start < .04) throw new Error('invalidRange');
    const clip = { id: crypto.randomUUID(), number: Math.max(0, ...this.clips.map(c => c.number)) + 1, start, end, name: '' };
    this.edit(() => this.clips.push(clip)); this.selection = new Set([clip.id]); return clip;
  }
  remove() { this.edit(() => this.clips = this.clips.filter(c => !this.selection.has(c.id))); this.selection.clear(); }
  rename(id, name) {
    const error = validateName(name, this.clips, id); if (error) throw new Error(error);
    this.edit(() => { this.clips.find(c => c.id === id).name = name; });
  }
  undo() { if (!this.undoStack.length) return; this.redoStack.push(this.snapshot()); this.clips = this.undoStack.pop(); this.selection.clear(); }
  redo() { if (!this.redoStack.length) return; this.undoStack.push(this.snapshot()); this.clips = this.redoStack.pop(); this.selection.clear(); }
}
