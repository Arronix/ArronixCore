// The published service worker.
//
// Its whole job is to make the application shell survive the server going away. The server holds every
// piece of data this application shows, so there is no offline mode to build and none is attempted here:
// what caching buys is that an installed client shows its own designed "can't reach the server" state
// instead of a browser error page, and recovers by itself when the server answers again.
//
// Three rules, and they are the whole design:
//
//   1. Cache the shell and the static assets, from the manifest the build produces. Nothing is guessed at
//      and nothing is cached opportunistically, so what is cached is exactly what was published.
//   2. Never intercept the REST API or the live-event endpoint. A cached answer from either would be a
//      stale library or a replayed event, and both are worse than an honest failure the application knows
//      how to present.
//   3. Never cache this file. A service worker that caches itself is a service worker that can never be
//      replaced, and the resulting client is unfixable without the user clearing site data by hand.

self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'arronix-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/, /^service-worker-assets\.js$/];

// Rule 2, written once. Anything whose path starts with one of these is the server's business and is
// passed straight through to the network, cached never, and allowed to fail so the application can say so.
const networkOnlyPaths = ['/api/', '/hub/', '/health'];

const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, self.origin).href);

async function onInstall() {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));

    // Taking over immediately is the right trade here: the shell is versioned by the build's own asset
    // manifest, so an old client and a new one never share a cache to be confused by.
    await self.skipWaiting();
}

async function onActivate() {
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys
            .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
            .map(key => caches.delete(key)));

    await self.clients.claim();
}

async function onFetch(event) {
    const request = event.request;
    const url = new URL(request.url);

    // Rule 2: the server's endpoints are never intercepted, on any method, from any origin.
    if (networkOnlyPaths.some(path => url.pathname.startsWith(path))) {
        return fetch(request);
    }

    // Anything that is not a plain document or asset read is the server's business too.
    if (request.method !== 'GET' || url.origin !== self.origin) {
        return fetch(request);
    }

    // A navigation to any in-application address is answered with the cached shell, which is what makes a
    // deep link into a library open the designed disconnected screen rather than a browser error.
    const shouldServeIndexHtml = request.mode === 'navigate';
    const cacheKey = shouldServeIndexHtml ? `${base}index.html` : request.url;

    if (!shouldServeIndexHtml && !manifestUrlList.includes(url.href)) {
        return fetch(request);
    }

    const cache = await caches.open(cacheName);
    const cached = await cache.match(cacheKey);

    if (cached) {
        return cached;
    }

    return fetch(request);
}

// --- Push seam -------------------------------------------------------------------------------------
//
// Deliberately not implemented in this milestone, and deliberately left visible rather than omitted.
//
// The pieces a push implementation needs are all here: a worker with an origin-wide scope, a manifest that
// makes the client installable, and a platform that already renders an extension's own summary of an item
// into a title, a subtitle, artwork and a relative deep link — which is exactly the payload a notification
// wants. What is missing is server-side: a subscription store, application-server keys, and the decision
// about which of the platform's notification events are worth waking someone up for.
//
// The companion design note covers that analysis; it is not duplicated here. When it lands, the two
// handlers below are where it attaches, and nothing above needs to change.
//
// self.addEventListener('push', event => { /* render event.data.json() into a notification */ });
// self.addEventListener('notificationclick', event => { /* focus a client and navigate to the deep link */ });
