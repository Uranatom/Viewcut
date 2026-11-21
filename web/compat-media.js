// A dedicated worker lets us immediately stop FFmpeg when switching files or
// cancelling an export, even while the WebAssembly call is synchronous.
export function mediaSession(file, signal) {
  const worker = new Worker(new URL('./compat-worker.js', import.meta.url));
  let pending, closed = false;
  function close() {
    if (closed) return;
    closed = true;
    worker.terminate();
    signal?.removeEventListener('abort', close);
    pending?.reject(new DOMException('Cancelled', 'AbortError'));
    pending = null;
  }
  worker.onmessage = ({data}) => {
    const request = pending;
    pending = null;
    if (data.error) request.reject(new Error(data.error));
    else request.resolve(data.blob);
  };
  worker.onerror = () => {
    pending?.reject(new Error('compatFailed'));
    pending = null;
    close();
  };
  signal?.addEventListener('abort', close, {once:true});
  if (signal?.aborted) close();
  function request(data) {
    if (closed) return Promise.reject(new DOMException('Cancelled', 'AbortError'));
    return new Promise((resolve, reject) => {
      pending = {resolve, reject};
      worker.postMessage(data);
    });
  }
  return { ready:request({file}), request, close };
}
