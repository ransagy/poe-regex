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

    await Promise.all(urls.map(async (url) => {
      try {
        // Bypass the browser's HTTP cache for non-fingerprinted shell files
        // so a new worker never precaches the previous deployment's CSS/JS.
        // _framework names carry content hashes, so normal caching is safe.
        const response = url.startsWith("/_framework/")
          ? await fetch(url)
          : await fetch(url, { cache: "reload" });
        if (response.ok) await cache.put(url, response);
      } catch {
        // Keep installing even if one optional shell file is unavailable.
      }
    }));
    // Wait here. The page shows an update prompt; it only sends SKIP_WAITING
    // after the user accepts (or the automatic idle path fires). Existing
    // tabs keep their old worker and cache until they reload on purpose.
  })());
});

self.addEventListener("message", (event) => {
  if (event.data && event.data.type === "SKIP_WAITING") {
    event.waitUntil(self.skipWaiting());
  }
});

self.addEventListener("activate", (event) => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(
      keys
        .filter((key) => key.startsWith(CACHE_PREFIX) && key !== cacheName)
        .map((key) => caches.delete(key)),
    );
    // Do not claim existing clients. A reload after activation picks up the
    // new worker; other tabs stay on their old worker until they reload.
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
      // Serve the active build's shell. Fetching the network first would let
      // the new deployment's index.html run against this worker's old cached
      // assets. The update prompt controls when a tab moves to the new build.
      const cached = await caches.match("/index.html");
      if (cached) return cached;
      try {
        const response = await fetch(request);
        if (response.ok) return response;
      } catch {
        // Fall through to the offline response.
      }
      return new Response("Offline", { status: 503 });
    })());
    return;
  }

  event.respondWith((async () => {
    // Shell assets carry ?v=<build> in index.html. The precached copy may be
    // unversioned (fallback) or versioned; either way, the build cache is the
    // source of truth for this worker.
    const cached = await caches.match(request, { ignoreSearch: true });
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
