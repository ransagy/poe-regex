// Offline shell for Renly's Tools.
//
// The build publishes a precache-manifest.json listing the static assets for
// the current commit. The worker fetches that list and build-info.json at
// install time, caches the app shell plus _framework assets, and removes the
// previous build's cache on activation.
const CACHE_PREFIX = "renlys-tools-";
const VERSION_URL = "/build-info.json";
const PRECACHE_URL = "/precache-manifest.json";
const FALLBACK_SHELL = [
  "/",
  "/index.html",
  "/css/app.css",
  "/manifest.webmanifest",
  "/favicon.svg",
  "/web.styles.css",
];

let cacheName = `${CACHE_PREFIX}dev`;

self.addEventListener("install", (event) => {
  event.waitUntil((async () => {
    let version = "dev";
    try {
      const response = await fetch(VERSION_URL, { cache: "no-store" });
      if (response.ok) {
        const info = await response.json();
        if (typeof info.commit === "string" && info.commit.length >= 7) {
          version = info.commit.slice(0, 12);
        }
      }
    } catch {
      // Deployments always publish build-info.json; fall back to a dev cache.
    }

    cacheName = `${CACHE_PREFIX}${version}`;
    const cache = await caches.open(cacheName);
    let urls = FALLBACK_SHELL;
    try {
      const response = await fetch(PRECACHE_URL, { cache: "no-store" });
      if (response.ok) {
        const listed = await response.json();
        if (Array.isArray(listed) && listed.length > 0) urls = listed;
      }
    } catch {
      // The fallback shell still gives a usable offline page.
    }

    await Promise.all(urls.map((url) => cache.add(url).catch(() => {})));
    await self.skipWaiting();
  })());
});

self.addEventListener("activate", (event) => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(
      keys
        .filter((key) => key.startsWith(CACHE_PREFIX) && key !== cacheName)
        .map((key) => caches.delete(key)),
    );
    await self.clients.claim();
  })());
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  // Never serve these from the shell cache; they describe the live deployment.
  if (
    url.pathname === "/build-info.json" ||
    url.pathname === "/precache-manifest.json" ||
    url.pathname === "/sw.js"
  ) {
    return;
  }

  if (request.mode === "navigate") {
    event.respondWith((async () => {
      try {
        const response = await fetch(request);
        if (response.ok) return response;
      } catch {
        // Offline: fall through to the cached shell.
      }
      return (await caches.match("/index.html")) ||
        new Response("Offline", { status: 503 });
    })());
    return;
  }

  event.respondWith((async () => {
    const cached = await caches.match(request);
    if (cached) return cached;
    try {
      const response = await fetch(request);
      if (response.ok && response.type === "basic") {
        const cache = await caches.open(cacheName);
        cache.put(request, response.clone());
      }
      return response;
    } catch {
      return new Response("Offline", { status: 503 });
    }
  })());
});
