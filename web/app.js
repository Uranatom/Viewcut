import { Editor, outputName, validateName, copy } from './model.js';
import { t, setLanguage } from './i18n.js';
import { icon } from './icons.js';
import { saveHandle, loadHandle, permission } from './storage.js';
import { additive,hasDirectoryAccess,isMac,shortcut } from './platform.js';
import { importedDirectory } from './portable.js';
import { keepLauncherAlive } from './host.js';
const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const player = $('#player'), previewVideo = $('#previewVideo'), viewport = $('#timelineViewport'), track = $('#track');
let preferences, drafts;
try { preferences = JSON.parse(localStorage.getItem('viewcut.preferences')) || {}; drafts = JSON.parse(localStorage.getItem('viewcut.drafts')) || {}; } catch { preferences = {}; drafts = {}; }
let language = preferences.language || 'zh-CN', theme = preferences.theme || 'dark';
let editor = new Editor(), active = null, sourceKey = '', url = null, destination = null, root = null, folders = [], entries = [], visible = [], selectedFiles = new Set(), fileAnchor = 0, page = 'files', largeIcons = preferences.largeIcons || false;
let history = [], historyAt = -1, fileClipboard = null, busyFiles = false, exportWorker = null, jobs = [], duration = 0, zoom = 1, saveTimer, previewTarget = null, previewBusy = false, loadTicket = 0, folderTicket = 0, sourceReady = false, previewReady = false;
let rootId = '', knownRoots, browsing = 0;
let downloadUrl = null, downloadFilename = '';
async function rootIdentity(handle) {
  if (handle.virtual) return 'import:' + handle.name;
  knownRoots ??= await loadHandle('knownRoots').catch(() => []) || [];
  for (const item of knownRoots) if (await item.handle.isSameEntry(handle)) return item.id;
  const id = crypto.randomUUID(); knownRoots.push({handle,id}); knownRoots = knownRoots.slice(-32);
  await saveHandle('knownRoots',knownRoots).catch(() => {}); return id;
}
const isActive = entry => !!active && (entry === active.entry || !!entry.key && entry.key === active.entry.key);
const editors = new Map(); const rootStyle = document.documentElement.style;
const safeNumber = (value, fallback, min, max) => Number.isFinite(value) ? Math.max(min, Math.min(max, value)) : fallback;
rootStyle.setProperty('--sidebar', safeNumber(preferences.sidebar, 390, 260, 900) + 'px');
rootStyle.setProperty('--timeline', safeNumber(preferences.timeline, 230, 140, 600) + 'px');
const format = (time, precise = true) => {
  time = Math.max(0, Number.isFinite(time) ? time : 0); const h = Math.floor(time / 3600), m = Math.floor(time / 60) % 60, s = Math.floor(time) % 60;
  return `${precise || h ? String(h).padStart(2,'0') + ':' : ''}${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}${precise ? '.' + String(Math.floor(time % 1 * 100)).padStart(2,'0') : ''}`;
};
const element = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; };
function status(text) { $('#status').textContent = text; }
function report(error) {
  if (error.name === 'AbortError') return;
  const key = ['NotAllowedError','SecurityError'].includes(error.name) ? 'permission' : error.message;
  status(t('error') + ': ' + t(key));
  if (['existingOutput','duplicateName','fileExists'].includes(key)) alert(t(key));
  console.error(error);
}
function run(action) { return Promise.resolve().then(action).catch(report); }
function persist() {
  if (active) { drafts[sourceKey] = { clips:editor.snapshot(), at:Date.now() }; editors.delete(sourceKey); editors.set(sourceKey, editor); }
  const keys = Object.keys(drafts).sort((a,b) => drafts[b].at - drafts[a].at); for (const key of keys.slice(64)) delete drafts[key];
  while (editors.size > 64) editors.delete(editors.keys().next().value);
  clearTimeout(saveTimer); saveTimer = setTimeout(() => { try { localStorage.setItem('viewcut.preferences', JSON.stringify(preferences)); localStorage.setItem('viewcut.drafts', JSON.stringify(drafts)); } catch { status(t('saveUnavailable')); } }, 80);
}
function applyTheme() { document.documentElement.dataset.theme = theme === 'system' ? matchMedia('(prefers-color-scheme:light)').matches ? 'light' : 'dark' : theme; }
matchMedia('(prefers-color-scheme:light)').addEventListener('change', applyTheme);
function localize() {
  setLanguage(language); document.documentElement.lang = language; preferences.language = language; preferences.theme = theme; applyTheme();
  $$('[data-i18n]').forEach(e => e.textContent = t(e.dataset.i18n));
  $$('button[data-action]').forEach(button => {
    const action = button.dataset.action; button.innerHTML = icon(action); button.title = shortcut(t(action)); button.setAttribute('aria-label', shortcut(t(action)));
    if (!['close','confirmRename'].includes(action)) button.type = 'button';
  });
  $('#search').placeholder = t('search'); $('#search').setAttribute('aria-label', t('search'));
  $('#stage').dataset.dropLabel = t('dropVideo');
  $('#language').value = language; $('#theme').value = theme;
  $('#activeName').textContent = active?.file.name || t('notLoaded'); $('#mediaName').textContent = active?.file.name || t('openFile');
  $('#destinationLabel').textContent = destination?.name || (!hasDirectoryAccess ? t('browserDownloads') : ''); renderAll(); status(t('ready')); persist();
}
function buttons() {
  for (const name of ['play','markIn','markOut','fit','zoomIn','zoomOut']) $(`[data-action="${name}"]`).disabled = !sourceReady;
  $('[data-action=previous]').disabled = !active || videoEntries().length < 2; $('[data-action=next]').disabled = !active || videoEntries().length < 2;
  $('[data-action=undo]').disabled = !editor.undoStack.length; $('[data-action=redo]').disabled = !editor.redoStack.length;
  const exp = $('[data-action=export]'); exp.disabled = !sourceReady || !editor.clips.length || !!exportWorker;
  exp.innerHTML = icon('export') + (editor.clips.length ? `<span class="badge">${editor.clips.length}</span>` : '');
  $('[data-action=showDestination]').disabled = !hasDirectoryAccess && !downloadUrl || hasDirectoryAccess && !destination;
  $('[data-action=destination]').disabled = !hasDirectoryAccess;
  if (!hasDirectoryAccess) { $('[data-action=destination]').title = t('browserDownloadsHelp'); $('[data-action=showDestination]').title = t('downloadReady'); }
  $('[data-action=back]').disabled = historyAt <= 0; $('[data-action=forward]').disabled = historyAt >= history.length - 1; $('[data-action=up]').disabled = folders.length < 2;
  $('[data-action=grid]').classList.toggle('selected', largeIcons); $('[data-action=list]').classList.toggle('selected', !largeIcons);
  $('[data-action=play]').innerHTML = icon(player.paused ? 'play' : 'pause');
}
function edited() { persist(); renderTimeline(); renderClips(); buttons(); }
function clipLabel(clip) { return clip.name || `${t('clip')} ${String(clip.number).padStart(2, '0')}`; }
function renderAll() { renderFiles(); renderBreadcrumb(); renderTimeline(); renderClips(); renderJobs(); buttons(); }
function clock() {
  $('#clock').textContent = `${format(player.currentTime)} / ${format(duration)}`;
  $('#playhead').style.left = (duration ? player.currentTime / duration * 100 : 0) + '%';
}
function timelineWidth() { return Math.max(1, viewport.clientWidth * zoom); }
function timeAt(clientX) { const rect = track.getBoundingClientRect(); return Math.max(0, Math.min(duration, (clientX - rect.left) / rect.width * duration)); }
function renderTimeline() {
  const width = timelineWidth(); track.style.width = width + 'px'; $('#ruler').style.width = width + 'px';
  const span = duration / zoom, rawStep = span / Math.max(2, viewport.clientWidth / 90), power = 10 ** Math.floor(Math.log10(rawStep || 1));
  const step = [1,2,5,10].map(n => n * power).find(n => n >= rawStep) || 1;
  $('#ruler').replaceChildren();
  if (duration) for (let at = 0; at <= duration + .0001; at += step) { const tick = element('span','tick',format(at,false)); tick.style.left = at / duration * 100 + '%'; $('#ruler').append(tick); }
  const layer = $('#clipLayer'); layer.replaceChildren(); const lanes = [];
  for (const clip of [...editor.clips].sort((a,b) => a.start - b.start)) {
    let lane = lanes.findIndex(end => end <= clip.start); if (lane === -1) lane = lanes.length; lanes[lane] = clip.end;
    const e = element('div', 'segment' + (editor.selection.has(clip.id) ? ' selected' : '')); e.dataset.id = clip.id; e.tabIndex = 0; e.setAttribute('role','option'); e.setAttribute('aria-selected', editor.selection.has(clip.id));
    e.style.cssText = `left:${clip.start / duration * 100}%;width:${(clip.end - clip.start) / duration * 100}%;top:${8 + lane * 40}px;--clip:var(--clip${(clip.number - 1) % 4 + 1})`;
    e.title = `${clipLabel(clip)}  ${format(clip.start)} — ${format(clip.end)}`; e.setAttribute('aria-label',e.title);
    const label = element('span','segment-label',clipLabel(clip));
    for (const edge of ['start','end']) { const handle = element('span','handle ' + edge); handle.dataset.edge = edge; handle.setAttribute('role','slider'); handle.tabIndex = 0; handle.setAttribute('aria-label', t(edge === 'start' ? 'markIn' : 'markOut')); handle.setAttribute('aria-valuemin', '0'); handle.setAttribute('aria-valuemax',String(duration)); handle.setAttribute('aria-valuenow', String(clip[edge])); e.append(handle); }
    e.append(label); layer.append(e);
  }
  layer.style.height = Math.max(62, lanes.length * 40 + 16) + 'px';
  $('#markLine').hidden = editor.mark === null; $('#markLine').style.left = (editor.mark || 0) / (duration || 1) * 100 + '%';
  $('#clipCount').textContent = `${editor.clips.length} ${t('clips')}`; clock();
}
function selectClip(id, additive) { if (!additive) editor.selection.clear(); if (additive && editor.selection.has(id)) editor.selection.delete(id); else editor.selection.add(id); for (const e of $$('.segment,.clip-card')) { const selected = editor.selection.has(e.dataset.id); e.classList.toggle('selected',selected); e.setAttribute('aria-selected',selected); } }
function renderClips() {
  const list = $('#clipList'); list.replaceChildren();
  if (!editor.clips.length) list.append(element('p','empty-list',t('noClips')));
  for (const clip of editor.clips) { const card = element('div','clip-card' + (editor.selection.has(clip.id) ? ' selected' : '')); card.dataset.id = clip.id; card.tabIndex = 0; card.append(element('strong','',clipLabel(clip)),element('small','',`${format(clip.start)} — ${format(clip.end)}`)); list.append(card); }
}
function renderJobs() { const list = $('#jobList'); list.replaceChildren(); if (!jobs.length) list.append(element('p','empty-list',t('noJobs'))); for (const job of jobs) { const card = element('div','job-card ' + job.state); card.append(element('strong','',job.name),element('small','',t(job.state) + (job.actualStart !== undefined ? ` · ${format(job.actualStart)} — ${format(job.end)}` : ''))); list.append(card); } }
function showPage(value) { page = value; for (const key of ['files','clips','jobs']) { $(`#${key}Page`).hidden = key !== value; $(`[data-action=${key}]`).classList.toggle('selected', key === value); } }
function renderBreadcrumb() { const e = $('#breadcrumb'); e.replaceChildren(); folders.forEach((folder, i) => { if (i) e.append(element('span','muted','›')); const b = element('button','',folder.name); b.title = folder.name; b.onclick = () => run(() => navigate(folders.slice(0,i + 1))); e.append(b); }); }
function renderFiles() {
  const query = $('#search').value.toLocaleLowerCase(); visible = entries.filter(e => e.name.toLocaleLowerCase().includes(query));
  const list = $('#fileList'); list.className = 'files ' + (largeIcons ? 'grid' : 'list'); $('.file-heading').hidden = largeIcons; list.replaceChildren();
  if (!visible.length) list.append(element('p','empty-list',t(query ? 'noResults' : root || entries.length ? 'emptyFolder' : 'chooseFolder')));
  visible.forEach((entry, i) => {
    const e = element('div','file-row' + (entry.kind === 'directory' ? ' folder' : '') + (selectedFiles.has(entry.name) ? ' selected' : '') + (isActive(entry) ? ' playing' : '')); e.dataset.index = i; e.setAttribute('role','option'); e.setAttribute('aria-selected', selectedFiles.has(entry.name)); e.title = entry.name;
    const name = element('div','file-name'); name.innerHTML = icon(entry.kind === 'directory' ? 'folder' : isVideo(entry.name) ? 'video' : 'file'); name.append(element('span','',entry.name));
    e.append(name, element('span','file-date',entry.modified ? new Date(entry.modified).toLocaleDateString(language) : ''),element('span','file-size',entry.size === undefined ? '' : (entry.size / 1048576).toLocaleString(language,{maximumFractionDigits:1}) + ' MB')); list.append(e);
    if (largeIcons && entry.kind === 'file' && isVideo(entry.name)) thumbnailObserver.observe(e);
  });
  $('#fileFooter').textContent = `${visible.length} ${t('items')}${selectedFiles.size ? ` · ${t('selected')} ${selectedFiles.size}` : ''}`;
}
const isVideo = name => /\.(mp4|mov|m4v)$/i.test(name);
const videoEntries = () => entries.filter(e => e.kind !== 'directory' && isVideo(e.name));
async function navigate(nextFolders, remember = true) {
  const ticket = ++folderTicket; const handle = nextFolders.at(-1); if (!handle) return;
  browsing = ticket;
  try {
  await permission(handle); const nextEntries = [], nextRootId = await rootIdentity(nextFolders[0]);
  for await (const [name, child] of handle.entries()) nextEntries.push({ name, kind:child.kind, handle:child,key:JSON.stringify([nextRootId,...nextFolders.slice(1).map(f => f.name),name]) });
  nextEntries.sort((a,b) => (a.kind === 'directory' ? 0 : 1) - (b.kind === 'directory' ? 0 : 1) || a.name.localeCompare(b.name,language,{numeric:true}));
  if (ticket !== folderTicket) return;
  const oldSelection = selectedFiles;
  folders = [...nextFolders]; root = folders[0]; rootId = nextRootId; entries = nextEntries; selectedFiles = remember ? new Set() : new Set(entries.filter(e => oldSelection.has(e.name)).map(e => e.name)); if (remember) $('#search').value = '';
  if (remember) { history = history.slice(0,historyAt + 1); history.push([...folders]); historyAt = history.length - 1; }
  showPage('files'); renderFiles(); renderBreadcrumb(); buttons();
  if (browsing === ticket) browsing = 0;
  // Lazy metadata in bounded batches keeps directories with thousands of files responsive.
  for (let start = 0; start < entries.length; start += 24) {
    if (ticket !== folderTicket) return;
    await Promise.all(entries.slice(start,start + 24).filter(e => e.kind === 'file').map(async entry => { try { const file = await entry.handle.getFile(); entry.size = file.size; entry.modified = file.lastModified; } catch {} }));
    if (ticket !== folderTicket) return;
    for (const row of $$('.file-row')) { const entry = visible[Number(row.dataset.index)]; if (!entry) continue; row.querySelector('.file-size').textContent = entry.size === undefined ? '' : (entry.size / 1048576).toLocaleString(language,{maximumFractionDigits:1}) + ' MB'; row.querySelector('.file-date').textContent = entry.modified ? new Date(entry.modified).toLocaleDateString(language) : ''; }
    await new Promise(resolve => setTimeout(resolve,0));
  }
  } finally { if (browsing === ticket) browsing = 0; }
}
async function openFolder() { if (!hasDirectoryAccess) { $('#folderInput').click(); return; } const handle = await showDirectoryPicker({id:'viewcut-source',mode:'read'}); await saveHandle('root',handle).catch(() => {}); await navigate([handle]); }
$('#folderInput').onchange = () => run(async () => { const directory = importedDirectory([...$('#folderInput').files]); if (directory) { await navigate([directory]); status(t('portableFolder')); } });
async function getFile(entry) { return entry.file || entry.handle.getFile(); }
async function openEntry(entry) { if (entry.kind === 'directory') return navigate([...folders,entry.handle]); await openVideo(entry); }
async function openVideo(entry) {
  if (!isVideo(entry.name)) throw new Error('unsupportedFile');
  const ticket = ++loadTicket; const file = await getFile(entry); if (ticket !== loadTicket) return;
  persist(); player.pause(); sourceReady = false; previewReady = false; active = { entry,file }; duration = 0;
  sourceKey = JSON.stringify([rootId, entry.file?.webkitRelativePath || entry.name, ...folders.slice(1).map(f => f.name),file.size,file.lastModified]);
  editor = editors.get(sourceKey) || new Editor(drafts[sourceKey]?.clips || []); editor.mark = null; zoom = 1; viewport.scrollLeft = 0;
  if (url) URL.revokeObjectURL(url); url = URL.createObjectURL(file); player.src = url; previewVideo.src = url; previewTarget = null; previewBusy = false;
  $('#preview').hidden = true;
  $('#empty').hidden = true; $('#mediaName').textContent = $('#activeName').textContent = file.name; document.title = `Viewcut · ${file.name}`; status(t('loading')); renderAll();
}
function changedSource() { return active?.entry.handle?.getFile().then(file => { if (file.size !== active.file.size || file.lastModified !== active.file.lastModified) throw new Error('sourceChanged'); }) || Promise.resolve(); }
function adjacent(direction) { const videos = videoEntries(), index = videos.findIndex(isActive); if (videos.length) return openVideo(videos[(index + direction + videos.length) % videos.length]); }
async function chooseDestination() { destination = await showDirectoryPicker({ id:'viewcut-destination',mode:'readwrite' }); $('#destinationLabel').textContent = destination.name; await saveHandle('destination',destination).catch(() => {}); buttons(); }
async function exportAll() {
  if (exportWorker || !editor.clips.length || !sourceReady) return;
  if (hasDirectoryAccess) { if (!destination) await chooseDestination(); else await permission(destination,'readwrite'); }
  await changedSource();
  const clips = editor.snapshot(), file = active.file;
  const existing = new Set(); if (destination) for await (const name of destination.keys()) existing.add(name.toLocaleLowerCase());
  for (const clip of clips) { const name = outputName(file.name,clip).toLocaleLowerCase(); if (existing.has(name)) throw new Error('existingOutput'); existing.add(name); }
  jobs = clips.map(c => ({id:c.id,name:outputName(file.name,c),state:'waiting'})); renderJobs();
  exportWorker = new Worker(new URL('./export-worker.js',import.meta.url),{type:'module'}); $('#progress').hidden = false; $('#progress').value = 0; $('#cancelExport').hidden = false; buttons();
  const finish = () => { exportWorker?.terminate(); exportWorker = null; $('#progress').hidden = true; $('#cancelExport').hidden = true; for (const job of jobs) if (job.state === 'waiting' || job.state === 'exporting') job.state = 'cancelled'; renderJobs(); buttons(); };
  exportWorker.onmessage = ({data}) => {
    if (data.type === 'indexing') status(t('indexing'));
    if (data.type === 'running') { jobs.find(j => j.id === data.id).state = 'exporting'; status(`${t('exporting')} · ${data.name}`); renderJobs(); }
    if (data.type === 'progress') $('#progress').value = data.value;
    if (data.type === 'done') { Object.assign(jobs.find(j => j.id === data.id),data,{state:'done'}); renderJobs(); }
    if (data.type === 'complete') { finish(); if (data.archive) { if (downloadUrl) URL.revokeObjectURL(downloadUrl); downloadUrl = URL.createObjectURL(data.archive); downloadFilename = file.name.replace(/\.[^.]+$/,'') + '-Viewcut.zip'; $('#downloadDialog').showModal(); buttons(); } status(`${t('done')} · ${data.count} ${t('clips')}`); }
    if (data.type === 'error') { const job = jobs.find(j => j.state === 'exporting'); if (job) job.state = 'failed'; finish(); report(new Error(data.key)); }
  };
  exportWorker.onerror = error => { finish(); report(new Error(error.message)); };
  exportWorker.postMessage({file,clips,directory:hasDirectoryAccess ? destination : null});
}
function renameClip(id) {
  const clip = editor.clips.find(c => c.id === id); if (!clip) return;
  $('#renameDialog').dataset.id = id; $('#clipName').value = clip.name || ''; $('#clipName').placeholder = clipLabel(clip); $('#renameError').textContent = '';
  $('#renameDialog').showModal(); $('#clipName').focus(); $('#clipName').select();
}
$('#renameDialog form').onsubmit = event => { event.preventDefault(); run(async () => {
  const id = $('#renameDialog').dataset.id, name = $('#clipName').value;
  let error = validateName(name,editor.clips,id);
  if (!error) { const filename = outputName(active.file.name,{...editor.clips.find(c => c.id === id),name}).toLowerCase(); if (editor.clips.some(c => c.id !== id && outputName(active.file.name,c).toLowerCase() === filename)) error = 'duplicateName'; }
  if (!error && name && destination) for await (const existing of destination.keys()) if (existing.toLocaleLowerCase() === outputName(active.file.name,{name}).toLocaleLowerCase()) { error = 'existingOutput'; break; }
  if (error) { $('#renameError').textContent = t(error); return; }
  editor.rename(id,name); $('#renameDialog').close(); edited();
}); };
function menu(event, items) {
  event.preventDefault(); const e = $('#contextMenu'); e.replaceChildren();
  for (const [label,action,glyph,key,disabled] of items) { const b = element('button'); b.setAttribute('role','menuitem'); b.innerHTML = icon(glyph || label); b.append(document.createTextNode(t(label))); if (key) b.append(element('kbd','',shortcut(key))); b.disabled = !!disabled; b.onclick = () => { e.hidden = true; run(action); }; e.append(b); }
  e.hidden = false; const rect = e.getBoundingClientRect(); e.style.left = Math.max(4,Math.min(event.clientX,innerWidth - rect.width - 6)) + 'px'; e.style.top = Math.max(4,Math.min(event.clientY,innerHeight - rect.height - 6)) + 'px'; e.querySelector('button:not(:disabled)')?.focus();
}
function clipMenu(event) { const clip = event.target.closest('[data-id]'); if (!clip) return; if (!editor.selection.has(clip.dataset.id)) selectClip(clip.dataset.id,false); menu(event,[['rename',() => renameClip(clip.dataset.id),'rename','F2',editor.selection.size > 1],[editor.selection.size > 1 ? 'removeSelected' : 'remove',() => { editor.remove(); edited(); },'remove','Del']]); }
$('#clipLayer').oncontextmenu = clipMenu; $('#clipList').oncontextmenu = clipMenu;
$('#clipList').onclick = event => { const card = event.target.closest('[data-id]'); if (card) selectClip(card.dataset.id,additive(event)); };
$('#clipList').ondblclick = event => { const clip = editor.clips.find(c => c.id === event.target.closest('[data-id]')?.dataset.id); if (clip) player.currentTime = clip.start; };
function markIn() { if (!sourceReady) return; editor.mark = player.currentTime; status(t('marked')); renderTimeline(); }
function markOut() { if (!sourceReady) return; if (editor.mark === null) { status(t('needMark')); return; } editor.add(editor.mark,player.currentTime); editor.mark = null; edited(); status(t('ready')); }
function changeZoom(value, anchor = viewport.clientWidth / 2) { const old = zoom; zoom = Math.max(1,Math.min(64,value)); const fraction = (viewport.scrollLeft + anchor) / (viewport.clientWidth * old); renderTimeline(); viewport.scrollLeft = fraction * viewport.clientWidth * zoom - anchor; }

let drag = null;
track.onpointerdown = event => {
  if (event.button !== 0 || !sourceReady) return;
  const segment = event.target.closest('.segment'), edge = event.target.closest('[data-edge]')?.dataset.edge;
  if (segment) { const id = segment.dataset.id; if (!editor.selection.has(id) || additive(event)) selectClip(id,additive(event)); if (!edge) return; drag = { clip:editor.clips.find(c => c.id === id),edge,before:editor.snapshot() }; player.pause(); }
  else { drag = {seek:true}; player.currentTime = timeAt(event.clientX); }
  event.preventDefault(); track.focus(); track.setPointerCapture(event.pointerId);
};
track.onpointermove = event => {
  if (!sourceReady) return;
  const at = timeAt(event.clientX);
  if (drag?.clip) { drag.clip[drag.edge] = drag.edge === 'start' ? Math.min(drag.clip.end - .04,at) : Math.max(drag.clip.start + .04,at); player.currentTime = drag.clip[drag.edge]; renderTimeline(); }
  else if (drag?.seek) player.currentTime = at;
  showPreview(at,event.clientX);
};
function finishDrag(cancel = false) { if (drag?.clip) { if (cancel) editor.clips = drag.before; else editor.record(drag.before); edited(); } drag = null; }
track.onpointerup = () => finishDrag(); track.onpointercancel = () => finishDrag(true); track.onlostpointercapture = () => finishDrag();
track.onpointerleave = () => { if (!drag) $('#preview').hidden = true; };
track.onkeydown = event => {
  const handle = event.target.closest('[data-edge]');
  if (!handle || !['ArrowLeft','ArrowRight'].includes(event.key)) return;
  event.preventDefault(); const clip = editor.clips.find(c => c.id === handle.closest('[data-id]').dataset.id), edge = handle.dataset.edge;
  editor.edit(() => clip[edge] = Math.max(edge === 'start' ? 0 : clip.start + .04,Math.min(edge === 'start' ? clip.end - .04 : duration,clip[edge] + (event.key === 'ArrowLeft' ? -1 : 1) * (event.shiftKey ? 1 : 1 / 30))));
  player.currentTime = clip[edge]; edited(); $(`.segment[data-id="${clip.id}"] [data-edge="${edge}"]`)?.focus();
};
viewport.onwheel = event => { if (!sourceReady) return; event.preventDefault(); if (event.shiftKey) viewport.scrollLeft += event.deltaY + event.deltaX; else changeZoom(zoom * Math.exp(-event.deltaY * .002),event.clientX - viewport.getBoundingClientRect().left); };
function showPreview(at,x) {
  if (!previewReady) return;
  const e = $('#preview'); e.hidden = false; e.style.left = Math.max(4,Math.min(x - 121,innerWidth - 248)) + 'px'; e.style.top = Math.max(4,Math.min(track.getBoundingClientRect().top - 174,innerHeight - 180)) + 'px';
  e.querySelector('output').textContent = format(at); previewTarget = Math.min(at,Math.max(0,duration - .001)); pumpPreview();
}
function paintPreview() {
  if (previewVideo.readyState < 2 || !previewVideo.videoWidth || !previewVideo.videoHeight) return false;
  const canvas = $('#preview canvas'), context = canvas.getContext('2d');
  if (!context) return false;
  context.fillStyle = '#000'; context.fillRect(0,0,canvas.width,canvas.height);
  try { const scale = Math.min(canvas.width / previewVideo.videoWidth,canvas.height / previewVideo.videoHeight); const w = previewVideo.videoWidth * scale,h = previewVideo.videoHeight * scale; context.drawImage(previewVideo,(canvas.width-w)/2,(canvas.height-h)/2,w,h); canvas.dataset.frameTime = previewVideo.currentTime.toFixed(3); return true; }
  catch (error) { if (error.name !== 'InvalidStateError') throw error; return false; }
}
function pumpPreview() {
  if (previewBusy || previewTarget === null || !previewReady) return;
  if (Math.abs(previewVideo.currentTime - previewTarget) < .015 && previewVideo.readyState >= 2) { previewTarget = null; paintPreview(); return; }
  previewBusy = true; const at = previewTarget; previewTarget = null;
  try { previewVideo.currentTime = at; }
  catch (error) { previewBusy = false; previewTarget = at; if (error.name !== 'InvalidStateError') throw error; }
}
previewVideo.onloadedmetadata = () => { previewReady = true; };
previewVideo.onseeked = () => { previewBusy = false; paintPreview(); if (previewVideo.requestVideoFrameCallback && previewVideo.readyState >= 2) previewVideo.requestVideoFrameCallback(() => paintPreview()); pumpPreview(); };
previewVideo.onloadeddata = previewVideo.oncanplay = () => { paintPreview(); pumpPreview(); };
player.onloadedmetadata = () => { duration = player.duration; if (!Number.isFinite(duration) || duration <= 0) { report(new Error('unsupportedPlayback')); return; } sourceReady = true; renderTimeline(); buttons(); status(t('ready')); };
player.onerror = () => { sourceReady = false; status(t('unsupportedPlayback')); buttons(); };
player.ontimeupdate = clock;
let frameLoop = 0;
player.onplay = () => { buttons(); const update = () => { clock(); if (!player.paused) frameLoop = requestAnimationFrame(update); }; cancelAnimationFrame(frameLoop); update(); };
player.onpause = () => { cancelAnimationFrame(frameLoop); buttons(); clock(); };
$('#stage').ondblclick = () => { if (sourceReady) run(() => player.paused ? player.play() : player.pause()); };
function divider(selector,axis) {
  const control = $(selector); let start, pending, animation = 0;
  const apply = () => { animation = 0; if (!start || pending === undefined) return; const vertical = axis === 'x', workspace = $('#workspace').getBoundingClientRect(), editorRect = $('#editor').getBoundingClientRect(); const min = vertical ? 260 : 140; const max = vertical ? Math.max(min,workspace.width - 300) : Math.max(min,editorRect.height - 160); const value = Math.round(Math.max(min,Math.min(max,pending))); rootStyle.setProperty(vertical ? '--sidebar' : '--timeline',value + 'px'); preferences[vertical ? 'sidebar' : 'timeline'] = value; control.setAttribute('aria-valuenow',String(value)); control.setAttribute('aria-valuemax',String(max)); };
  control.onpointerdown = event => { if (event.button !== 0) return; event.preventDefault(); const vertical = axis === 'x'; start = { position:vertical ? event.clientX : event.clientY, size:vertical ? $('#sidebar').getBoundingClientRect().width : $('#timelinePanel').getBoundingClientRect().height }; pending = undefined; document.body.classList.add('resizing'); document.body.style.cursor = vertical ? 'col-resize' : 'row-resize'; control.setPointerCapture(event.pointerId); $('#preview').hidden = true; };
  control.onpointermove = event => { if (!start) return; pending = start.size + start.position - (axis === 'x' ? event.clientX : event.clientY); if (!animation) animation = requestAnimationFrame(apply); };
  const finish = () => { if (!start) return; cancelAnimationFrame(animation); apply(); start = null; document.body.classList.remove('resizing'); document.body.style.cursor = ''; persist(); renderTimeline(); };
  control.onpointerup = finish; control.onpointercancel = finish; control.onlostpointercapture = finish;
  control.onkeydown = event => { const vertical = axis === 'x'; if (!(vertical ? ['ArrowLeft','ArrowRight'] : ['ArrowUp','ArrowDown']).includes(event.key)) return; event.preventDefault(); const current = vertical ? $('#sidebar').getBoundingClientRect().width : $('#timelinePanel').getBoundingClientRect().height; start = {}; pending = current + (['ArrowLeft','ArrowUp'].includes(event.key) ? 16 : -16); apply(); start = null; persist(); renderTimeline(); };
}
divider('#verticalDivider','x'); divider('#horizontalDivider','y');
let resizeFrame; new ResizeObserver(() => { cancelAnimationFrame(resizeFrame); resizeFrame = requestAnimationFrame(renderTimeline); }).observe(viewport);
$('#fileList').onclick = event => {
  const row = event.target.closest('[data-index]'); if (!row) { selectedFiles.clear(); renderFileSelection(); return; }
  const index = Number(row.dataset.index), entry = visible[index];
  if (event.shiftKey) { if (!additive(event)) selectedFiles.clear(); for (let i = Math.min(fileAnchor,index); i <= Math.max(fileAnchor,index); i++) if (visible[i]) selectedFiles.add(visible[i].name); }
  else { if (!additive(event)) selectedFiles.clear(); if (additive(event) && selectedFiles.has(entry.name)) selectedFiles.delete(entry.name); else selectedFiles.add(entry.name); fileAnchor = index; }
  renderFileSelection(); $('#fileList').focus();
};
function renderFileSelection() { for (const row of $$('.file-row')) { const selected = selectedFiles.has(visible[Number(row.dataset.index)].name); row.classList.toggle('selected',selected); row.setAttribute('aria-selected',selected); } $('#fileFooter').textContent = `${visible.length} ${t('items')} · ${t('selected')} ${selectedFiles.size}`; }
$('#fileList').ondblclick = event => { const row = event.target.closest('[data-index]'); if (row) run(() => openEntry(visible[Number(row.dataset.index)])); };
let rubber = null, suppressFileClick = false;
$('#fileList').addEventListener('pointerdown', event => {
  if (event.button !== 0 || event.target.closest('.file-row')) return;
  const list = $('#fileList'), rect = list.getBoundingClientRect();
  rubber = { x:event.clientX - rect.left + list.scrollLeft,y:event.clientY - rect.top + list.scrollTop,initial:additive(event) ? new Set(selectedFiles) : new Set(),pointer:event.pointerId };
  if (!additive(event)) selectedFiles.clear(); list.setPointerCapture(event.pointerId);
});
$('#fileList').addEventListener('pointermove', event => {
  if (!rubber) return; const list = $('#fileList'), rect = list.getBoundingClientRect();
  const x = event.clientX - rect.left + list.scrollLeft,y = event.clientY - rect.top + list.scrollTop;
  const left = Math.min(x,rubber.x),top = Math.min(y,rubber.y),width = Math.abs(x-rubber.x),height = Math.abs(y-rubber.y);
  if (width + height < 5) return;
  suppressFileClick = true; let box = $('#selectionBox'); if (!box) { box = element('div'); box.id = 'selectionBox'; list.append(box); }
  box.style.cssText = `left:${left}px;top:${top}px;width:${width}px;height:${height}px`;
  selectedFiles = new Set(rubber.initial);
  for (const row of $$('.file-row')) { const r = row.getBoundingClientRect(),rx = r.left - rect.left + list.scrollLeft,ry = r.top - rect.top + list.scrollTop; if (rx < left + width && rx + r.width > left && ry < top + height && ry + r.height > top) selectedFiles.add(visible[Number(row.dataset.index)].name); row.classList.toggle('selected',selectedFiles.has(visible[Number(row.dataset.index)].name)); row.setAttribute('aria-selected',row.classList.contains('selected')); }
  $('#fileFooter').textContent = `${visible.length} ${t('items')} · ${t('selected')} ${selectedFiles.size}`;
});
$('#fileList').addEventListener('pointerup', () => { rubber = null; $('#selectionBox')?.remove(); setTimeout(() => suppressFileClick = false,0); });
$('#fileList').addEventListener('click', event => { if (suppressFileClick) { event.stopImmediatePropagation(); event.preventDefault(); } },true);
$('#fileList').addEventListener('pointercancel', () => { rubber = null; $('#selectionBox')?.remove(); });
const selectedEntries = () => entries.filter(e => selectedFiles.has(e.name));
const currentDirectory = () => { const directory = folders.at(-1); if (!directory) throw new Error('folderAccess'); return directory; };
async function exists(directory,name) { for await (const current of directory.keys()) if (current.toLocaleLowerCase() === name.toLocaleLowerCase()) return true; return false; }
async function copyEntry(entry,directory,name = entry.name) {
  if (await exists(directory,name)) throw new Error('fileExists');
  if (entry.kind === 'directory') {
    const target = await directory.getDirectoryHandle(name,{create:true});
    for await (const [childName,handle] of entry.handle.entries()) await copyEntry({name:childName,kind:handle.kind,handle},target);
  } else {
    const file = await getFile(entry), target = await directory.getFileHandle(name,{create:true}); let stream;
    try { stream = await target.createWritable(); await file.stream().pipeTo(stream); }
    catch (error) { await stream?.abort().catch(() => {}); await directory.removeEntry(name).catch(() => {}); throw error; }
  }
}
async function fileOperation(action) { if (browsing) throw new Error('loadingFolder'); if (folders.at(-1)?.virtual) throw new Error('readOnlyFolder'); if (busyFiles) return; busyFiles = true; try { await action(); status(t('fileOperationDone')); if (folders.length) await navigate(folders,false); } finally { busyFiles = false; } }
async function deleteFiles() { const items = selectedEntries(); if (!items.length) return; if (!confirm(t('deleteFiles') + '\n\n' + items.map(e => e.name).join('\n'))) return; await fileOperation(async () => { const directory = currentDirectory(); await permission(directory,'readwrite'); for (const entry of items) { await directory.removeEntry(entry.name,{recursive:entry.kind === 'directory'}); if (isActive(entry)) { player.pause(); sourceReady = false; buttons(); } } }); }
function clipboardFiles(cut = false) { fileClipboard = {entries:selectedEntries(),cut,directory:folders.at(-1)}; status(t(cut ? 'cutReady' : 'clipboardReady')); }
async function pasteFiles() {
  if (!fileClipboard?.entries.length) return;
  await fileOperation(async () => {
    const directory = currentDirectory(); await permission(directory,'readwrite');
    if (fileClipboard.cut && fileClipboard.directory) await permission(fileClipboard.directory,'readwrite');
    for (const entry of fileClipboard.entries) {
      if (entry.kind === 'directory' && await entry.handle.resolve(directory) !== null) throw new Error('unsafeCopy');
      if (await exists(directory,entry.name)) throw new Error('fileExists');
    }
    for (const entry of fileClipboard.entries) { await copyEntry(entry,directory); if (fileClipboard.cut) { if (!fileClipboard.directory) throw new Error('folderAccess'); await fileClipboard.directory.removeEntry(entry.name,{recursive:entry.kind === 'directory'}); } }
    if (fileClipboard.cut) fileClipboard = null;
  });
}
async function renameFile() {
  const items = selectedEntries(); if (items.length !== 1) return;
  const entry = items[0], name = prompt(t('renameFile'),entry.name); if (name === null || name === entry.name) return;
  const error = validateName(name,[],null); if (error || !name) throw new Error(error || 'invalidName');
  await fileOperation(async () => { const directory = currentDirectory(); await permission(directory,'readwrite'); await copyEntry(entry,directory,name); await directory.removeEntry(entry.name,{recursive:entry.kind === 'directory'}); if (isActive(entry)) { player.pause(); sourceReady = false; buttons(); } });
}
async function newFolder() { const name = prompt(t('newFolderName')); if (name === null) return; const error = validateName(name,[],null); if (!name || error) throw new Error(error || 'invalidName'); await fileOperation(async () => { const directory = currentDirectory(); await permission(directory,'readwrite'); if (await exists(directory,name)) throw new Error('fileExists'); await directory.getDirectoryHandle(name,{create:true}); }); }
function properties() { const items = selectedEntries(); alert(items.map(e => `${e.name}${e.size === undefined ? '' : '\n' + e.size.toLocaleString(language) + ' bytes'}${e.modified ? '\n' + new Date(e.modified).toLocaleString(language) : ''}`).join('\n\n')); }
function fileMenu(event) {
  const row = event.target.closest('[data-index]'); if (row) { const entry = visible[Number(row.dataset.index)]; if (!selectedFiles.has(entry.name)) { selectedFiles = new Set([entry.name]); renderFiles(); } }
  const count = selectedFiles.size, writable = !browsing && folders.length && !folders.at(-1).virtual;
  menu(event,[['openFolder',openFolder,'openFolder'],['copy',() => clipboardFiles(false),'copy','Ctrl+C',!count],['cut',() => clipboardFiles(true),'cut','Ctrl+X',!count || !writable],['paste',pasteFiles,'paste','Ctrl+V',!fileClipboard || !writable],['rename',renameFile,'rename','F2',count !== 1 || !writable],['remove',deleteFiles,'remove','Del',!count || !writable],['newFolder',newFolder,'newFolder','Ctrl+Shift+N',!writable],['selectAll',() => { selectedFiles = new Set(visible.map(e => e.name)); renderFiles(); },'selectAll','Ctrl+A'],['invert',() => { selectedFiles = new Set(visible.filter(e => !selectedFiles.has(e.name)).map(e => e.name)); renderFiles(); },'invert'],['properties',properties,'properties','Alt+Enter',!count]]);
}
$('#fileList').oncontextmenu = fileMenu;
$('#search').oninput = () => { fileAnchor = 0; renderFiles(); };
async function importFiles(files,openFirst=false) {
  const videos = files.filter(file => isVideo(file.name)); if (!videos.length) throw new Error('unsupportedFile');
  ++folderTicket; browsing = 0; folders = []; root = null; rootId = ''; history = []; historyAt = -1;
  entries = videos.map(file => ({name:file.name,kind:'file',file,size:file.size,modified:file.lastModified})); selectedFiles.clear(); $('#search').value = ''; renderAll();
  if (openFirst || entries.length === 1) await openVideo(entries[0]);
}
$('#fileInput').onchange = () => run(() => importFiles([...$('#fileInput').files]));
let dropDepth = 0;
const fileDrag = event => [...(event.dataTransfer?.types || [])].includes('Files');
$('#stage').ondragenter = event => { if (!fileDrag(event)) return; event.preventDefault(); dropDepth++; $('#stage').classList.add('drop-target'); };
$('#stage').ondragover = event => { if (!fileDrag(event)) return; event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; };
$('#stage').ondragleave = event => { event.preventDefault(); if (--dropDepth <= 0) { dropDepth = 0; $('#stage').classList.remove('drop-target'); } };
$('#stage').ondrop = event => { event.preventDefault(); dropDepth = 0; $('#stage').classList.remove('drop-target'); run(() => importFiles([...event.dataTransfer.files],true)); };
document.addEventListener('dragover',event => { if (fileDrag(event)) event.preventDefault(); });
document.addEventListener('drop',event => { if (fileDrag(event)) event.preventDefault(); });
const actions = {
  destination:chooseDestination, showDestination:async () => { if (!hasDirectoryAccess) { if (downloadUrl) $('#downloadDialog').showModal(); return; } if (destination) { await navigate([destination]); status(t('exportFolderShown')); } },preferences:() => $('#preferences').showModal(),
  downloadZip:() => { if (!downloadUrl) return; const anchor = element('a'); anchor.href = downloadUrl; anchor.download = downloadFilename; document.body.append(anchor); anchor.click(); anchor.remove(); $('#downloadDialog').close(); status(t('downloadStarted')); },
  play:() => sourceReady && (player.paused ? player.play() : player.pause()),previous:() => adjacent(-1),next:() => adjacent(1),markIn,markOut,
  undo:() => { editor.undo(); edited(); },redo:() => { editor.redo(); edited(); },zoomIn:() => changeZoom(zoom*1.5),zoomOut:() => changeZoom(zoom/1.5),fit:() => { changeZoom(1); viewport.scrollLeft = 0; },export:exportAll,cancelExport:() => exportWorker?.postMessage({type:'cancel'}),
  files:() => showPage('files'),clips:() => showPage('clips'),jobs:() => showPage('jobs'),openFolder,openFile:() => $('#fileInput').click(),
  back:async () => { if (historyAt > 0) { const previous = historyAt; try { await navigate(history[--historyAt],false); } catch(error) { historyAt = previous; throw error; } } },forward:async () => { if (historyAt < history.length-1) { const previous = historyAt; try { await navigate(history[++historyAt],false); } catch(error) { historyAt = previous; throw error; } } },up:() => folders.length > 1 && navigate(folders.slice(0,-1)),refresh:() => folders.length && navigate(folders,false),
  grid:() => { largeIcons = preferences.largeIcons = true; persist(); renderFiles(); buttons(); },list:() => { largeIcons = preferences.largeIcons = false; persist(); renderFiles(); buttons(); },fileMenu:event => { const rect = event.currentTarget.getBoundingClientRect(); fileMenu({preventDefault(){},target:event.currentTarget,clientX:rect.left,clientY:rect.bottom}); },cancelRename:() => $('#renameDialog').close(),
};
$$('button[data-action]').forEach(button => { if (actions[button.dataset.action]) button.onclick = event => run(() => actions[button.dataset.action](event)); });
$('#language').onchange = event => { language = event.target.value; localize(); };
$('#theme').onchange = event => { theme = event.target.value; preferences.theme = theme; applyTheme(); persist(); };
document.addEventListener('pointerdown', event => { if (!event.target.closest('#contextMenu')) $('#contextMenu').hidden = true; });
document.addEventListener('keydown', event => {
  if (!$('dialog[open]') && !$('#contextMenu').hidden) {
    const options = [...$('#contextMenu').querySelectorAll('button:not(:disabled)')];
    if (event.key === 'Escape') { $('#contextMenu').hidden = true; return; }
    if (['ArrowDown','ArrowUp'].includes(event.key)) { event.preventDefault(); options[(options.indexOf(document.activeElement) + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length]?.focus(); return; }
  }
  if ($('dialog[open]') || event.target.matches('input,textarea,select,[contenteditable=true]')) return;
  const ctrl = event.ctrlKey || event.metaKey, key = event.key.toLowerCase(), inFiles = page === 'files' && !!event.target.closest('#filesPage');
  if (inFiles && browsing && (key === 'delete' || key === 'f2' || ctrl && ['x','v'].includes(key) || ctrl && event.shiftKey && key === 'n')) { event.preventDefault(); status(t('loadingFolder')); return; }
  if (inFiles && folders.at(-1)?.virtual && (key === 'delete' || key === 'f2' || ctrl && ['x','v'].includes(key) || ctrl && event.shiftKey && key === 'n' || isMac && ctrl && key === 'backspace')) { event.preventDefault(); status(t('readOnlyFolder')); return; }
  let action;
  if (ctrl && key === 'z') action = event.shiftKey ? actions.redo : actions.undo;
  else if (ctrl && key === 'y') action = actions.redo;
  else if (inFiles) {
    if (ctrl && key === 'a') action = () => { selectedFiles = new Set(visible.map(e => e.name)); renderFiles(); };
    if (ctrl && key === 'c') action = () => clipboardFiles(false);
    if (ctrl && key === 'x') action = () => clipboardFiles(true);
    if (ctrl && key === 'v') action = pasteFiles;
    if (ctrl && event.shiftKey && key === 'n') action = newFolder;
    if (key === 'delete') action = deleteFiles;
    if (isMac && event.metaKey && key === 'backspace') action = deleteFiles;
    if (key === 'f2') action = renameFile;
    if (key === 'f5') action = actions.refresh;
    if (key === 'enter' && !event.altKey && selectedFiles.size === 1) action = () => openEntry(selectedEntries()[0]);
    if (key === 'enter' && event.altKey) action = properties;
    if (key === 'backspace' || event.altKey && key === 'arrowup') action = actions.up;
    if (event.altKey && key === 'arrowleft') action = actions.back;
    if (event.altKey && key === 'arrowright') action = actions.forward;
    if (key === 'arrowdown' || key === 'arrowup') action = () => { const index = Math.max(0,Math.min(visible.length-1,fileAnchor+(key === 'arrowdown' ? 1 : -1))); if (visible[index]) { if (!event.shiftKey) selectedFiles.clear(); selectedFiles.add(visible[index].name); fileAnchor = index; renderFiles(); $(`.file-row[data-index="${index}"]`)?.scrollIntoView({block:'nearest'}); } };
  } else if (!ctrl && !event.altKey) {
    if (key === ' ') action = actions.play;
    if (key === 'i') action = markIn; if (key === 'o') action = markOut;
    if (key === 'p') action = actions.previous; if (key === 'n') action = actions.next;
    if (['delete','backspace'].includes(key) && editor.selection.size) action = () => { editor.remove(); edited(); };
    if (key === 'f2' && editor.selection.size === 1) action = () => renameClip([...editor.selection][0]);
    if (['arrowleft','arrowright'].includes(key) && !event.target.closest('[data-edge]')) action = () => { if (sourceReady) player.currentTime = Math.max(0,Math.min(duration,player.currentTime + (key === 'arrowleft' ? -1 : 1) * (event.shiftKey ? 5 : 1 / 30))); };
  }
  if (action) { event.preventDefault(); run(action); }
});
window.addEventListener('beforeunload', event => { persist(); clearTimeout(saveTimer); try { localStorage.setItem('viewcut.preferences',JSON.stringify(preferences)); localStorage.setItem('viewcut.drafts',JSON.stringify(drafts)); } catch {} if (exportWorker || busyFiles) { event.preventDefault(); event.returnValue = ''; } });
const thumbnails = new Map(), thumbnailQueue = [], thumbnailQueued = new WeakSet(); let thumbnailBusy = false;
function putThumbnail(row,url) { const name = row.querySelector('.file-name'); if (!name || name.querySelector('img')) return; const image = element('img','thumbnail'); image.src = url; image.alt = ''; name.querySelector('svg')?.replaceWith(image); }
const thumbnailObserver = new IntersectionObserver(items => {
  for (const item of items) if (item.isIntersecting) {
    thumbnailObserver.unobserve(item.target); const entry = visible[Number(item.target.dataset.index)]; if (!entry) continue;
    const key = entry.key || entry.name;
    if (thumbnails.has(key)) putThumbnail(item.target,thumbnails.get(key));
    else if (!thumbnailQueued.has(entry)) { thumbnailQueued.add(entry); thumbnailQueue.push({entry,row:item.target,key}); }
  }
  runThumbnails();
},{root:$('#fileList'),rootMargin:'100px'});
async function runThumbnails() {
  if (thumbnailBusy || !thumbnailQueue.length) return; thumbnailBusy = true;
  const {entry,row,key} = thumbnailQueue.shift(); let video,objectUrl,timer;
  try {
    const file = await getFile(entry); video = document.createElement('video'); video.muted = true; video.preload = 'metadata'; video.playsInline = true; objectUrl = URL.createObjectURL(file);
    await new Promise((resolve,reject) => { timer = setTimeout(() => reject(new Error('thumbnailTimeout')),4000); video.onerror = reject; video.onloadedmetadata = () => { video.currentTime = Math.min(1,video.duration/3); }; video.onseeked = resolve; video.src = objectUrl; });
    const canvas = document.createElement('canvas'); canvas.width = 192; canvas.height = 108; const context = canvas.getContext('2d');
    const scale = Math.min(canvas.width/video.videoWidth,canvas.height/video.videoHeight),w = video.videoWidth*scale,h = video.videoHeight*scale; context.drawImage(video,(canvas.width-w)/2,(canvas.height-h)/2,w,h);
    const blob = await new Promise(resolve => canvas.toBlob(resolve,'image/jpeg',.75)); if (!blob) return;
    const url = URL.createObjectURL(blob); thumbnails.set(key,url); if (row.isConnected && largeIcons) putThumbnail(row,url);
    while (thumbnails.size > 64) { const oldest = thumbnails.keys().next().value; URL.revokeObjectURL(thumbnails.get(oldest)); thumbnails.delete(oldest); }
  } catch {} finally { clearTimeout(timer); if (video) { video.removeAttribute('src'); video.load(); } if (objectUrl) URL.revokeObjectURL(objectUrl); thumbnailBusy = false; runThumbnails(); }
}
setInterval(async () => {
  if (document.hidden || busyFiles || !folders.length || page !== 'files' || rubber) return;
  const folder = folders.at(-1), ticket = folderTicket;
  try { const current = []; for await (const [name,handle] of folder.entries()) current.push(name + ':' + handle.kind); const previous = entries.map(e => e.name + ':' + e.kind); if (ticket === folderTicket && current.sort().join('\n') !== previous.sort().join('\n')) await navigate(folders,false); } catch {}
},5000);
localize();
if (!window.Worker || !window.isSecureContext) status(t('unsupportedBrowser'));
else if (hasDirectoryAccess) { loadHandle('destination').then(async handle => { if (handle && await handle.queryPermission({mode:'readwrite'}) === 'granted') { destination = handle; $('#destinationLabel').textContent = handle.name; buttons(); } }).catch(() => {}); loadHandle('root').then(async handle => { if (handle && await handle.queryPermission({mode:'read'}) === 'granted') await navigate([handle]); }).catch(() => {}); }
keepLauncherAlive(() => !!exportWorker || busyFiles);
