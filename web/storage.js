let database;
async function db() {
  if (!database) database = new Promise((resolve, reject) => {
    const request = indexedDB.open('viewcut', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('handles');
    request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
  });
  return database;
}
export async function saveHandle(key, handle) {
  const database = await db();
  return new Promise((resolve, reject) => { const tx = database.transaction('handles', 'readwrite'); tx.objectStore('handles').put(handle, key); tx.oncomplete = resolve; tx.onerror = () => reject(tx.error); });
}
export async function loadHandle(key) {
  const database = await db();
  return new Promise((resolve, reject) => { const request = database.transaction('handles').objectStore('handles').get(key); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error); });
}
export async function permission(handle, mode = 'read') {
  if (await handle.queryPermission({ mode }) === 'granted') return;
  if (await handle.requestPermission({ mode }) !== 'granted') throw new Error('permission');
}
