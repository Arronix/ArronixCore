// The browser half of the client contract store.
//
// One responsibility: hold bytes under a key that names them, and hand them back. It decides nothing.
// Every rule about which bytes are wanted, whether they are the right ones and whether they may be loaded
// lives in the application, because the application is the half that can be reviewed as one thing.
//
// The key is a content hash, so an entry can never be stale in the sense of holding the wrong bytes: it is
// either the entry that was asked for or it is not there. Removing entries an installation no longer names
// is therefore a housekeeping question rather than a correctness one.
//
// Cache Storage rather than IndexedDB because this is exactly what it is for, and because a request/response
// pair keeps the stored bytes opaque and unparsed. It is unavailable outside a secure context, and that is
// reported rather than worked around: a client with no store refetches, which is slower and just as correct.

const CACHE_NAME = 'arronix-client-contracts-v1';
const KEY_PREFIX = '/arronix-contract/';

function available() {
    return typeof caches !== 'undefined' && caches !== null;
}

function addressOf(key) {
    return KEY_PREFIX + encodeURIComponent(key);
}

export function isAvailable() {
    return available();
}

export async function read(key) {
    if (!available()) {
        return null;
    }

    const store = await caches.open(CACHE_NAME);
    const held = await store.match(addressOf(key));
    if (!held) {
        return null;
    }

    const bytes = new Uint8Array(await held.arrayBuffer());
    let binary = '';
    for (let index = 0; index < bytes.length; index++) {
        binary += String.fromCharCode(bytes[index]);
    }

    return btoa(binary);
}

export async function write(key, base64) {
    if (!available()) {
        return false;
    }

    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) {
        bytes[index] = binary.charCodeAt(index);
    }

    const store = await caches.open(CACHE_NAME);
    await store.put(addressOf(key), new Response(bytes, {
        headers: { 'Content-Type': 'application/octet-stream' }
    }));

    return true;
}

export async function keys() {
    if (!available()) {
        return [];
    }

    const store = await caches.open(CACHE_NAME);
    const requests = await store.keys();
    return requests
        .map(request => new URL(request.url).pathname)
        .filter(path => path.startsWith(KEY_PREFIX))
        .map(path => decodeURIComponent(path.substring(KEY_PREFIX.length)));
}

export async function remove(key) {
    if (!available()) {
        return false;
    }

    const store = await caches.open(CACHE_NAME);
    return await store.delete(addressOf(key));
}

export async function clear() {
    if (!available()) {
        return false;
    }

    return await caches.delete(CACHE_NAME);
}
