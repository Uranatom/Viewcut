// Real SafariDriver acceptance on an ephemeral macOS runner (not a UA override).
import {spawn} from 'node:child_process';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {serve} from '../server.mjs';
const output=path.resolve('artifacts/safari-native');await mkdir(output,{recursive:true});
const {server,url}=await serve({port:0});
const driver=spawn('/usr/bin/safaridriver',['--port','14444'],{stdio:'inherit'});
let session;
async function request(method,route,body){const r=await fetch('http://127.0.0.1:14444'+route,{method,headers:{'Content-Type':'application/json'},...(body?{body:JSON.stringify(body)}:{})});const data=await r.json();if(data.value?.error)throw new Error(data.value.message);return data.value;}
const wait=ms=>new Promise(r=>setTimeout(r,ms));async function until(action){let last;for(let i=0;i<100;i++){try{const v=await action();if(v)return v;}catch(e){last=e;}await wait(150);}throw last||new Error('Timed out');}
const script=(value,args=[])=>request('POST',`/session/${session}/execute/sync`,{script:value,args});
try{
 await until(()=>request('GET','/status'));
 const created=await request('POST','/session',{capabilities:{alwaysMatch:{browserName:'safari'}}});session=created.sessionId;
 await request('POST',`/session/${session}/url`,{url});
 const b64=(await readFile(path.resolve('artifacts/web-safari/Trip/Day 1/camera.mp4'))).toString('base64');
 await script("const bytes=Uint8Array.from(atob(arguments[0]),c=>c.charCodeAt(0)),f=new File([bytes],'Safari camera.mp4',{type:'video/mp4'}),d=new DataTransfer();d.items.add(f);const e=document.querySelector('#fileInput');e.files=d.files;e.dispatchEvent(new Event('change',{bubbles:true}));",[b64]);
 await until(()=>script("return !document.querySelector('[data-action=markIn]').disabled"));
 for(const [start,end] of [[.5,1.8],[2.5,4.1]]){
  await script("document.querySelector('#player').currentTime=arguments[0]",[start]);await until(()=>script("const v=document.querySelector('#player');return !v.seeking&&Math.abs(v.currentTime-arguments[0])<.1",[start]));await script("document.querySelector('[data-action=markIn]').click()");
  await until(()=>script("return !document.querySelector('#markLine').hidden"));
  await script("document.querySelector('#player').currentTime=arguments[0]",[end]);await until(()=>script("const v=document.querySelector('#player');return !v.seeking&&Math.abs(v.currentTime-arguments[0])<.1",[end]));await script("document.querySelector('[data-action=markOut]').click()");
 }
 assert.equal(await script("return document.querySelectorAll('.segment').length"),2);
 await script("const e=document.querySelector('#track'),r=e.getBoundingClientRect();e.dispatchEvent(new PointerEvent('pointermove',{clientX:r.x+r.width*.4,clientY:r.y+50,bubbles:true}));");
 await until(()=>script("const c=document.querySelector('#preview canvas');return !document.querySelector('#preview').hidden&&c.getContext('2d').getImageData(0,0,c.width,c.height).data.some((n,i)=>i%4!==3&&n>10)"));
 await script("document.querySelector('[data-action=export]').click()");await until(()=>script("return document.querySelector('#downloadDialog').open"));
 await writeFile(path.join(output,'results.json'),JSON.stringify({browser:'Safari',capabilities:created.capabilities,status:'passed',tests:['H264 File playback','multiple clip marking','real frame preview','Worker remux and ZIP preparation'],nativePicker:'not automated',downloadSaveDialog:'not automated'},null,2));
 console.log('PASS real macOS Safari: H264 playback, multiple clips, frame preview, ZIP preparation');
}catch(error){await writeFile(path.join(output,'results.json'),JSON.stringify({browser:'Safari',status:'failed',error:error.message},null,2));throw error;}
finally{if(session)await request('DELETE',`/session/${session}`).catch(()=>{});driver.kill();server.close();}
