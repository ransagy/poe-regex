// Register the offline shell only on a deployed origin. Local development
// reloads should never be served from a previous build's cache.
(function () {
  if (!("serviceWorker" in navigator)) return;

  const host = location.hostname;
  if (host === "localhost" || host === "127.0.0.1" || host === "[::1]") return;

  window.addEventListener("load", function () {
    navigator.serviceWorker.register("/sw.js").catch(function () {
      // Offline support is best effort; the app still works online.
    });
  });
})();
