// Item tooltip placement: prefer the cursor with a small offset, flipping to
// the opposite side and clamping so the pinned tooltip stays fully visible.
window.poeRegexPlaceTooltip = function (el, x, y) {
  if (!el) return;
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
  el.style.left = left + "px";
  el.style.top = top + "px";
};
