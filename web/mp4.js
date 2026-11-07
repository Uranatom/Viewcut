// ISO BMFF packet-copy cutter. Reads metadata and selected sample ranges only.
// No decoding, transcoding, server uploads, WASM filesystem or whole-file buffers.
const ascii = (data, start, count) => String.fromCharCode(...data.subarray(start, start + count));
const bytes = text => Uint8Array.from(text, c => c.charCodeAt(0));
const view = data => new DataView(data.buffer, data.byteOffset, data.byteLength);
const u32 = n => { const b = new Uint8Array(4); view(b).setUint32(0, n); return b; };
const u64 = n => { const b = new Uint8Array(8); view(b).setBigUint64(0, BigInt(n)); return b; };
const concat = parts => { const b = new Uint8Array(parts.reduce((n, p) => n + p.length, 0)); let o = 0; for (const p of parts) { b.set(p, o); o += p.length; } return b; };
const box = (type, ...parts) => { const b = concat(parts); return concat([u32(b.length + 8), bytes(type), b]); };
const full = (type, payload, version = 0) => box(type, Uint8Array.of(version, 0, 0, 0), payload);
function children(data, start = 8, end = data.length) {
  const result = []; const v = view(data);
  for (let at = start; at < end;) {
    if (at + 8 > end) throw new Error('invalidMp4');
    let size = v.getUint32(at), header = 8;
    if (size === 1) { if (at + 16 > end) throw new Error('invalidMp4'); size = Number(v.getBigUint64(at + 8)); header = 16; }
    if (!size) size = end - at;
    if (!Number.isSafeInteger(size) || size < header || at + size > end) throw new Error('invalidMp4');
    // Normalize the rare extended-size metadata box to an ordinary box.
    const type = ascii(data, at + 4, 4);
    result.push({ type, data: header === 16 ? box(type, data.subarray(at + 16, at + size)) : data.subarray(at, at + size) });
    at += size;
  }
  return result;
}
const get = (list, type) => { const b = list.find(b => b.type === type); if (!b) throw new Error('invalidMp4'); return b.data; };
function timing(data) {
  const v = view(data), version = data[8];
  const scale = v.getUint32(version ? 28 : 20);
  const duration = version ? Number(v.getBigUint64(32)) : v.getUint32(24);
  if (!scale) throw new Error('invalidMp4');
  return { scale, duration };
}
function setDuration(data, duration, track = false) {
  const b = data.slice(), v = view(b), version = b[8];
  const offset = track ? (version ? 36 : 28) : (version ? 32 : 24);
  if (version) v.setBigUint64(offset, BigInt(Math.round(duration)));
  else { if (duration > 0xffffffff) throw new Error('durationTooLong'); v.setUint32(offset, Math.round(duration)); }
  return b;
}
function expandRuns(data, count, signed = false) {
  const v = view(data), values = new Float64Array(count); let sample = 0;
  if (data.length < 16 || 16 + v.getUint32(12) * 8 > data.length) throw new Error('invalidMp4');
  for (let at = 16; at < data.length; at += 8) {
    const length = v.getUint32(at), value = signed ? v.getInt32(at + 4) : v.getUint32(at + 4);
    if (sample + length > count) throw new Error('invalidMp4');
    values.fill(value, sample, sample + length); sample += length;
  }
  if (sample !== count) throw new Error('invalidMp4');
  return values;
}
function runs(type, values, signed = false) {
  const pairs = [];
  for (const value of values) { const last = pairs.at(-1); if (last && last[1] === value) last[0]++; else pairs.push([1, value]); }
  return full(type, concat([u32(pairs.length), ...pairs.flatMap(([count, value]) => [u32(count), u32(value >>> 0)])]), signed ? 1 : 0);
}
function parseTrack(raw, movieScale, fileSize) {
  const boxes = children(raw), mdia = children(get(boxes, 'mdia')), minf = children(get(mdia, 'minf'));
  const handler = ascii(get(mdia, 'hdlr'), 16, 4);
  if (!['vide', 'soun'].includes(handler)) return null;
  const stbl = children(get(minf, 'stbl')), mdhd = get(mdia, 'mdhd'), { scale } = timing(mdhd);
  const stsd = get(stbl, 'stsd');
  if (ascii(stsd, 20, 4) === 'encv' || ascii(stsd, 20, 4) === 'enca') throw new Error('encryptedMp4');
  const sizesBox = stbl.find(b => b.type === 'stsz' || b.type === 'stz2');
  if (!sizesBox) throw new Error('invalidMp4');
  const sv = view(sizesBox.data), count = sv.getUint32(16);
  if (count > 5_000_000) throw new Error('indexTooLarge');
  const sizes = new Uint32Array(count);
  if (sizesBox.type === 'stsz') {
    const fixed = sv.getUint32(12); if (fixed) sizes.fill(fixed);
    else for (let i = 0; i < count; i++) sizes[i] = sv.getUint32(20 + i * 4);
  } else {
    const field = sizesBox.data[15];
    for (let i = 0; i < count; i++) {
      if (field === 4) sizes[i] = (sizesBox.data[20 + (i >> 1)] >> (i % 2 ? 0 : 4)) & 15;
      else if (field === 8) sizes[i] = sv.getUint8(20 + i);
      else if (field === 16) sizes[i] = sv.getUint16(20 + i * 2);
      else throw new Error('invalidMp4');
    }
  }
  if (!count) return null;
  const durations = expandRuns(get(stbl, 'stts'), count);
  const ct = stbl.find(b => b.type === 'ctts')?.data;
  const compositions = ct ? expandRuns(ct, count, ct[8] === 1) : new Float64Array(count);
  const offsetsBox = stbl.find(b => b.type === 'stco' || b.type === 'co64');
  if (!offsetsBox) throw new Error('invalidMp4');
  const ov = view(offsetsBox.data), sc = view(get(stbl, 'stsc')), entries = sc.getUint32(12), chunks = ov.getUint32(12);
  if (!entries) throw new Error('invalidMp4');
  const syncBox = stbl.find(b => b.type === 'stss')?.data, sync = new Set();
  if (syncBox) { const v = view(syncBox); for (let i = 0; i < v.getUint32(12); i++) sync.add(v.getUint32(16 + i * 4) - 1); }
  let shift = 0;
  const edts = boxes.find(b => b.type === 'edts');
  if (edts) {
    const elst = get(children(edts.data), 'elst'), ev = view(elst), wide = elst[8] === 1, stride = wide ? 20 : 12;
    let empty = 0, mediaEdits = 0;
    for (let i = 0; i < ev.getUint32(12); i++) {
      const at = 16 + i * stride;
      const segment = wide ? Number(ev.getBigUint64(at)) : ev.getUint32(at);
      const mediaTime = wide ? Number(ev.getBigInt64(at + 8)) : ev.getInt32(at + 4);
      if (ev.getInt16(at + (wide ? 16 : 8)) !== 1 || ev.getInt16(at + (wide ? 18 : 10)) !== 0) throw new Error('complexEdits');
      if (mediaTime === -1 && !mediaEdits) empty += segment / movieScale;
      else if (mediaTime >= 0 && ++mediaEdits === 1) shift = empty - mediaTime / scale;
      else throw new Error('complexEdits');
    }
  }
  const samples = new Array(count); let si = 0, run = 0, dts = 0;
  for (let chunk = 1; chunk <= chunks; chunk++) {
    while (run + 1 < entries && sc.getUint32(16 + (run + 1) * 12) <= chunk) run++;
    const perChunk = sc.getUint32(20 + run * 12), description = sc.getUint32(24 + run * 12);
    let offset = offsetsBox.type === 'co64' ? Number(ov.getBigUint64(16 + (chunk - 1) * 8)) : ov.getUint32(16 + (chunk - 1) * 4);
    for (let i = 0; i < perChunk; i++) {
      if (si >= count || !Number.isSafeInteger(offset) || offset + sizes[si] > fileSize) throw new Error('invalidMp4');
      samples[si] = { offset, size: sizes[si], dts, cts: dts + compositions[si], duration: durations[si], sync: !syncBox || sync.has(si), description };
      offset += sizes[si]; dts += durations[si]; si++;
    }
  }
  if (si !== count) throw new Error('invalidMp4');
  return { raw, boxes, mdia, minf, stsd, mdhd, scale, handler, samples, shift };
}
export async function indexFile(file, signal) {
  let moov, ftyp; const mdats = [];
  for (let offset = 0; offset < file.size;) {
    signal?.throwIfAborted();
    const header = new Uint8Array(await file.slice(offset, offset + 16).arrayBuffer());
    if (header.length < 8) throw new Error('invalidMp4');
    const hv = view(header), type = ascii(header, 4, 4); let size = hv.getUint32(0), hs = 8;
    if (size === 1) { if (header.length < 16) throw new Error('invalidMp4'); size = Number(hv.getBigUint64(8)); hs = 16; }
    if (!size) size = file.size - offset;
    if (!Number.isSafeInteger(size) || size < hs || offset + size > file.size) throw new Error('invalidMp4');
    if (type === 'moof') throw new Error('fragmentedMp4');
    if (type === 'mdat') mdats.push([offset + hs, offset + size]);
    if (type === 'ftyp' || type === 'moov') {
      if (size > 128 * 1024 * 1024) throw new Error('indexTooLarge');
      let data = new Uint8Array(await file.slice(offset, offset + size).arrayBuffer());
      if (hs === 16) data = box(type, data.subarray(16));
      if (type === 'moov') moov = data; else ftyp = data;
    }
    offset += size;
  }
  if (!moov) throw new Error('invalidMp4');
  const boxes = children(moov);
  if (boxes.some(b => b.type === 'mvex')) throw new Error('fragmentedMp4');
  const mvhd = get(boxes, 'mvhd'), { scale, duration } = timing(mvhd);
  const tracks = boxes.filter(b => b.type === 'trak').map(b => parseTrack(b.data, scale, file.size)).filter(Boolean);
  if (tracks.filter(t => t.handler === 'vide').length !== 1) throw new Error('unsupportedTracks');
  for (const t of tracks) for (const s of t.samples) if (!mdats.some(([start, end]) => s.offset >= start && s.offset + s.size <= end)) throw new Error('invalidMp4');
  return { ftyp: ftyp || box('ftyp', bytes('qt  '), u32(0), bytes('qt  ')), mvhd, scale, duration: duration / scale, tracks };
}
function makeTrack(track, selected, baseDts, start, end, movieScale, offset) {
  const offsets = [], sizes = [], durations = [], cts = [], sync = [], sc = [];
  let mediaDuration = 0, description = -1;
  selected.forEach((s, i) => {
    offsets.push(u64(offset)); offset += s.size; sizes.push(u32(s.size)); durations.push(s.duration); cts.push(s.cts - s.dts);
    if (s.sync) sync.push(u32(i + 1));
    if (s.description !== description) { sc.push(u32(i + 1), u32(1), u32(s.description)); description = s.description; }
    mediaDuration += s.duration;
  });
  const stbl = box('stbl', track.stsd, runs('stts', durations), ...(cts.some(Boolean) ? [runs('ctts', cts, cts.some(n => n < 0))] : []),
    full('stsc', concat([u32(sc.length / 3), ...sc])), full('stsz', concat([u32(0), u32(sizes.length), ...sizes])),
    full('co64', concat([u32(offsets.length), ...offsets])), ...(sync.length < selected.length ? [full('stss', concat([u32(sync.length), ...sync]))] : []));
  const visibleStart = Math.max(start, Math.min(...selected.slice(0, 32).map(s => s.cts / track.scale + track.shift)));
  const delay = Math.max(0, visibleStart - start);
  const mediaTime = Math.max(0, Math.round((visibleStart - track.shift) * track.scale - baseDts));
  const visibleDuration = Math.max(0, Math.min(end - visibleStart, mediaDuration / track.scale - mediaTime / track.scale));
  const editEntries = [];
  if (delay > .00001) editEntries.push(u64(Math.round(delay * movieScale)), u64(0xffffffffffffffffn), Uint8Array.of(0, 1, 0, 0));
  editEntries.push(u64(Math.round(visibleDuration * movieScale)), u64(mediaTime), Uint8Array.of(0, 1, 0, 0));
  const edts = box('edts', full('elst', concat([u32(editEntries.length / 3), ...editEntries]), 1));
  const minf = box('minf', ...track.minf.map(b => b.type === 'stbl' ? stbl : b.data));
  const mdia = box('mdia', ...track.mdia.map(b => b.type === 'mdhd' ? setDuration(b.data, mediaDuration) : b.type === 'minf' ? minf : b.data));
  return { data: box('trak', ...track.boxes.filter(b => !['edts', 'mdia', 'tkhd'].includes(b.type)).map(b => b.data),
    setDuration(get(track.boxes, 'tkhd'), (delay + visibleDuration) * movieScale, true), edts, mdia), offset };
}
export function planCut(index, start, end) {
  if (!Number.isFinite(start + end) || start < 0 || end <= start || end > index.duration + .1) throw new Error('invalidRange');
  const video = index.tracks.find(t => t.handler === 'vide');
  let first = 0;
  video.samples.forEach((s, i) => { if (s.sync && s.cts / video.scale + video.shift <= start + .000001) first = i; });
  if (!video.samples[first].sync) throw new Error('noKeyframe');
  const actualStart = Math.max(0, video.samples[first].cts / video.scale + video.shift);
  // Decode order includes the reference pictures needed by the selected B frames.
  let last = first;
  video.samples.forEach((s, i) => { if (i >= first && s.cts / video.scale + video.shift < end) last = i; });
  const selections = index.tracks.map(track => {
    const selected = track === video ? track.samples.slice(first, last + 1) : track.samples.filter(s => {
      const time = s.cts / track.scale + track.shift; return time + s.duration / track.scale > actualStart && time < end;
    });
    return { track, selected, baseDts: selected[0]?.dts || 0 };
  }).filter(x => x.selected.length);
  const payloadSize = selections.reduce((sum, t) => sum + t.selected.reduce((n, s) => n + s.size, 0), 0);
  function moovAt(offset) {
    const tracks = [];
    for (const selection of selections) { const result = makeTrack(selection.track, selection.selected, selection.baseDts, actualStart, end, index.scale, offset); tracks.push(result.data); offset = result.offset; }
    return box('moov', setDuration(index.mvhd, (end - actualStart) * index.scale), ...tracks);
  }
  const placeholder = moovAt(0), payloadOffset = index.ftyp.length + placeholder.length + 16;
  const moov = moovAt(payloadOffset);
  const header = concat([index.ftyp, moov, u32(1), bytes('mdat'), u64(payloadSize + 16)]);
  const ranges = [];
  for (const { selected } of selections) for (const s of selected) {
    const lastRange = ranges.at(-1);
    if (lastRange && lastRange.end === s.offset && s.offset + s.size - lastRange.start <= 4 * 1024 * 1024) lastRange.end += s.size;
    else ranges.push({ start: s.offset, end: s.offset + s.size });
  }
  return { header, ranges, actualStart, end, size: header.length + payloadSize };
}
export async function writeCut(file, plan, writable, { signal, progress } = {}) {
  let written = 0;
  try {
    signal?.throwIfAborted(); await writable.write(plan.header); written += plan.header.length;
    for (const range of plan.ranges) for (let at = range.start; at < range.end; at += 4 * 1024 * 1024) {
      signal?.throwIfAborted(); const part = file.slice(at, Math.min(range.end, at + 4 * 1024 * 1024));
      await writable.write(part); written += part.size; progress?.(written / plan.size);
    }
    signal?.throwIfAborted(); await writable.close();
  } catch (error) { await writable.abort().catch(() => {}); throw error; }
}
