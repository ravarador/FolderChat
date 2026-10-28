// FolderChat's service worker. It makes FolderChat installable as an app, and shows a page that says how to start
// FolderChat when the app is opened while the server isn't running.
//
// It deliberately caches nothing else: the server runs on this computer, so pages, scripts and the API always come
// straight from it, and an update to FolderChat can never leave an old copy of a script behind.
const CACHE = "folderchat-offline-1";
const OFFLINE_PAGE = "/offline.html";
const OFFLINE_FILES = [OFFLINE_PAGE, "/icons/icon-192.png"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(OFFLINE_FILES)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(key => key !== CACHE).map(key => caches.delete(key))))
            .then(() => self.clients.claim()));
});

// Only page loads, and the offline page's own icon, are handled. Everything else, including every /api request,
// goes to the server untouched.
self.addEventListener("fetch", event => {
    if (event.request.mode === "navigate") {
        event.respondWith(fetch(event.request).catch(async () => (await caches.match(OFFLINE_PAGE)) ?? Response.error()));
    } else if (OFFLINE_FILES.includes(new URL(event.request.url).pathname)) {
        event.respondWith(fetch(event.request).catch(async () => (await caches.match(event.request)) ?? Response.error()));
    }
});
