// Family-picker popover placement. The panel is fixed-positioned so the
// import dialog's scroll container cannot clip it; this keeps it anchored to
// the trigger and flips above when there is not enough room below.
function poeRegexEffectiveZoom(el) {
  let z = 1;
  for (let n = el; n; n = n.parentElement) {
    const v = parseFloat(getComputedStyle(n).zoom);
    if (Number.isFinite(v) && v > 0) z *= v;
  }
  return z;
}

window.poeRegexPlacePopover = function (triggerId, panelId) {
  const trigger = document.getElementById(triggerId);
  const panel = document.getElementById(panelId);
  if (!trigger || !panel) return;
  const z = poeRegexEffectiveZoom(panel);
  const pad = 12;
  const rect = trigger.getBoundingClientRect();
  const width = Math.min(
    Math.max(rect.width, 272),
    Math.max(200, window.innerWidth - pad * 2));
  panel.style.width = (width / z) + "px";

  const height = panel.getBoundingClientRect().height || 240;
  let left = rect.left;
  if (left + width > window.innerWidth - pad)
    left = window.innerWidth - width - pad;
  left = Math.max(pad, Math.min(left, window.innerWidth - width - pad));

  let top = rect.bottom + 4;
  if (top + height > window.innerHeight - pad)
    top = Math.max(pad, rect.top - height - 4);

  panel.style.left = (left / z) + "px";
  panel.style.top = (top / z) + "px";
};
