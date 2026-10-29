// Item tooltip placement: prefer the cursor with a small offset, flipping to
// the opposite side and clamping so the pinned tooltip stays fully visible.
function effectiveZoom(el) {
  let z = 1;
  for (let n = el; n; n = n.parentElement) {
    const v = parseFloat(getComputedStyle(n).zoom);
    if (Number.isFinite(v) && v > 0) z *= v;
  }
  return z;
}

window.poeRegexPlaceTooltip = function (el, x, y) {
  if (!el) return;
  const z = effectiveZoom(el);
  const pad = 12;
  const r = el.getBoundingClientRect();
  let left = x + pad;
  if (left + r.width > window.innerWidth - pad) {
    left = Math.max(4, x - r.width - pad);
  }
  let top = y + pad;
  if (top + r.height > window.innerHeight - pad) {
    top = Math.max(4, y - r.height - pad);
  }
  // Client coordinates are viewport pixels; style offsets live in the
  // (possibly body-zoomed) element's local coordinate space.
  el.style.left = (left / z) + "px";
  el.style.top = (top / z) + "px";
};
