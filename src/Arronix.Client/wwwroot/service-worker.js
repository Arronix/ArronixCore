// The development service worker: deliberately inert.
//
// A worker that cached the application shell would serve yesterday's build to whoever is working on it,
// and the resulting "why is my change not showing" is the single most expensive minute in web development.
// The published build replaces this file wholesale with service-worker.published.js, which is where the
// caching, the offline shell and the push seams actually live.

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', () => self.clients.claim());
self.addEventListener('fetch', () => { });
