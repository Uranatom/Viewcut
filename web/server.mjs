import http from 'node:http';
import { open, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.dirname(fileURLToPath(import.meta.url));
export function serve({ port = 4173 } = {}) {
  const server = http.createServer(async (request,response) => {
    let handle;
    try {
      if (!['GET','HEAD'].includes(request.method)) { response.writeHead(405); response.end(); return; }
      const url = new URL(request.url,'http://localhost');
      if (url.pathname === '/__viewcut/runtime') { response.writeHead(204); response.end(); return; }
      const filename = path.resolve(root,'.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
      const relative = path.relative(root,filename);
      if (relative.startsWith('..') || path.isAbsolute(relative) || relative.split(path.sep).some(p => p.startsWith('.')) || /^(tests|node_modules)(\/|\\)/.test(relative) || relative.endsWith('.mjs') || relative.endsWith('.md') || relative === 'package.json') throw new Error('notFound');
      const info = await stat(filename); if (!info.isFile()) throw new Error('notFound');
      const types = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.svg':'image/svg+xml','.mp4':'video/mp4','.mov':'video/quicktime','.json':'application/json','.txt':'text/plain; charset=utf-8'};
      const headers = {'Content-Type':types[path.extname(filename)] || 'application/octet-stream','X-Content-Type-Options':'nosniff','Cache-Control':'no-cache','Referrer-Policy':'no-referrer','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'"};
      response.writeHead(200,{...headers,'Content-Length':info.size});
      if (request.method === 'HEAD') { response.end(); return; }
      handle = await open(filename); const stream = handle.createReadStream(); stream.on('error',() => response.destroy()); stream.pipe(response); stream.on('close',() => handle?.close().catch(() => {}));
    } catch { await handle?.close().catch(() => {}); if (!response.headersSent) response.writeHead(404); response.end('Not found'); }
  });
  return new Promise((resolve,reject) => { server.once('error',reject); server.listen(port,'127.0.0.1',() => { const address = `http://127.0.0.1:${server.address().port}`; console.log(`Viewcut: ${address}`); resolve({server,url:address}); }); });
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await serve({port:Number(process.env.PORT || 4173)});
