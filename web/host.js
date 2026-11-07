// Native launcher lifecycle. Static hosting returns 204 for this optional bridge.
export async function keepLauncherAlive(isBusy) {
  try {
    const response = await fetch('./__viewcut/runtime',{cache:'no-store'});
    if (response.status !== 200) return;
    const runtime = await response.json(),id=crypto.randomUUID().replaceAll('-','');
    if (!runtime.pulse?.startsWith('/__viewcut/pulse/')) return;
    const endpoint=runtime.pulse+'/'+id;
    const pulse = () => fetch(endpoint,{cache:'no-store',keepalive:true}).catch(()=>{});
    await pulse(); const interval = Math.max(1000,Math.min(30000,Number(runtime.interval) || 15000)); const timer=setInterval(pulse,interval);
    window.addEventListener('pageshow',pulse); document.addEventListener('visibilitychange',pulse);
    window.addEventListener('pagehide',event => { if(event.persisted || isBusy()) return; clearInterval(timer); fetch(endpoint+'?close=1',{keepalive:true}).catch(()=>{}); });
  } catch {}
}
