// Read-only directory tree for browsers without the File System Access pickers.
export function importedDirectory(files) {
  if (!files.length) return null;
  const rootName = files[0].webkitRelativePath.split('/')[0] || 'Videos';
  const makeDirectory = name => ({
    name, kind:'directory', virtual:true, children:new Map(),
    async queryPermission({mode} = {}) { return mode === 'readwrite' ? 'denied' : 'granted'; },
    async requestPermission(options) { return this.queryPermission(options); },
    async isSameEntry(other) { return this === other; },
    async *entries() { yield* this.children; },
  });
  const root = makeDirectory(rootName);
  for (const file of files) {
    const parts = (file.webkitRelativePath || `${rootName}/${file.name}`).split('/').slice(1); let folder = root;
    for (const name of parts.slice(0,-1)) { if (!folder.children.has(name)) folder.children.set(name,makeDirectory(name)); folder = folder.children.get(name); }
    folder.children.set(file.name,{name:file.name,kind:'file',virtual:true,async getFile() { return file; }});
  }
  return root;
}

// ZIP64 store-only output. Each entry references original Blob slices, rather
// than allocating a copy of every video or implementing an in-memory filesystem.
const encoder = new TextEncoder();
const crcTable = Uint32Array.from({length:256},(_,i) => { let n=i; for(let j=0;j<8;j++) n=n&1 ? 0xedb88320^(n>>>1) : n>>>1; return n>>>0; });
const block = length => { const bytes = new Uint8Array(length); return {bytes,v:new DataView(bytes.buffer)}; };
async function crc32(blob,signal) {
  let crc = 0xffffffff;
  for(let start=0;start<blob.size;start+=4*1024*1024) {
    signal?.throwIfAborted(); const data = new Uint8Array(await blob.slice(start,start+4*1024*1024).arrayBuffer());
    for(const value of data) crc=crcTable[(crc^value)&255]^(crc>>>8);
    await new Promise(resolve => setTimeout(resolve,0));
  }
  return (crc^0xffffffff)>>>0;
}
export async function zipClips(entries,{signal,progress} = {}) {
  const parts=[],central=[]; let offset=0,at=0;
  for(const entry of entries) {
    signal?.throwIfAborted(); const name=encoder.encode(entry.name),crc=await crc32(entry.blob,signal);
    const extra=block(20); extra.v.setUint16(0,1,true); extra.v.setUint16(2,16,true); extra.v.setBigUint64(4,BigInt(entry.blob.size),true); extra.v.setBigUint64(12,BigInt(entry.blob.size),true);
    const local=block(30); local.v.setUint32(0,0x04034b50,true); local.v.setUint16(4,45,true); local.v.setUint16(6,0x800,true); local.v.setUint32(14,crc,true); local.v.setUint32(18,0xffffffff,true); local.v.setUint32(22,0xffffffff,true); local.v.setUint16(26,name.length,true); local.v.setUint16(28,extra.bytes.length,true);
    parts.push(local.bytes,name,extra.bytes,entry.blob);
    const cx=block(28); cx.v.setUint16(0,1,true); cx.v.setUint16(2,24,true); cx.v.setBigUint64(4,BigInt(entry.blob.size),true); cx.v.setBigUint64(12,BigInt(entry.blob.size),true); cx.v.setBigUint64(20,BigInt(offset),true);
    const header=block(46); header.v.setUint32(0,0x02014b50,true); header.v.setUint16(4,45,true); header.v.setUint16(6,45,true); header.v.setUint16(8,0x800,true); header.v.setUint32(16,crc,true); header.v.setUint32(20,0xffffffff,true); header.v.setUint32(24,0xffffffff,true); header.v.setUint16(28,name.length,true); header.v.setUint16(30,cx.bytes.length,true); header.v.setUint32(42,0xffffffff,true);
    central.push(header.bytes,name,cx.bytes); offset+=local.bytes.length+name.length+extra.bytes.length+entry.blob.size; progress?.(++at/entries.length);
  }
  const centralSize=central.reduce((n,p)=>n+p.length,0),end=block(56); end.v.setUint32(0,0x06064b50,true); end.v.setBigUint64(4,44n,true); end.v.setUint16(12,45,true); end.v.setUint16(14,45,true); end.v.setBigUint64(24,BigInt(entries.length),true); end.v.setBigUint64(32,BigInt(entries.length),true); end.v.setBigUint64(40,BigInt(centralSize),true); end.v.setBigUint64(48,BigInt(offset),true);
  const locator=block(20); locator.v.setUint32(0,0x07064b50,true); locator.v.setBigUint64(8,BigInt(offset+centralSize),true); locator.v.setUint32(16,1,true);
  const legacy=block(22); legacy.v.setUint32(0,0x06054b50,true); legacy.v.setUint16(8,0xffff,true); legacy.v.setUint16(10,0xffff,true); legacy.v.setUint32(12,0xffffffff,true); legacy.v.setUint32(16,0xffffffff,true);
  return new Blob([...parts,...central,end.bytes,locator.bytes,legacy.bytes],{type:'application/zip'});
}
