// Persisted display scale for the workbench. The value is applied as a
// body zoom so it behaves like the browser zoom users already use. The
// pre-boot inline script in index.html applies the saved value before first
// paint; this helper keeps the Blazor select and the localStorage in sync.
function currentScale() {
  const v = parseFloat(document.body.style.zoom || "");
  return Number.isFinite(v) && v > 0 ? v : 1;
}

// When the scaled content no longer fits the viewport, allow the page to
// scroll and let the bench take its natural height. At 130% on a 900px-high
// laptop this is what keeps the picker/list usable; 1920x1080 at 130% still
// fits without the page scrolling.
function syncLayout() {
  const scale = currentScale();
  const w = window.innerWidth / scale;
  const h = window.innerHeight / scale;
  document.body.classList.toggle("compact", w < 1100 || h < 640);
}

window.poeRegexScale = {
  current: currentScale,
  syncLayout: syncLayout,
  set(scale) {
    const v = Number(scale);
    if (!Number.isFinite(v) || v < 1 || v > 1.5) return;
    localStorage.setItem("poe-regex.ui-scale", String(v));
    document.body.style.zoom = String(v);
    syncLayout();
  }
};

window.addEventListener("resize", syncLayout);
