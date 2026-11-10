// Keep Tab inside a dialog while it is open. The dialog is removed on close,
// so the listener dies with it; focus restoration is handled by Blazor.
window.poeRegexTrapFocus = function (root) {
  if (!root || root.__poeTrap) return;

  const selector =
    'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

  const handler = function (event) {
    if (event.key !== 'Tab') return;
    const nodes = Array.from(root.querySelectorAll(selector))
      .filter(el => !el.hidden && el.offsetParent !== null);
    if (nodes.length === 0) return;

    const first = nodes[0];
    const last = nodes[nodes.length - 1];
    if (!root.contains(document.activeElement)) {
      event.preventDefault();
      first.focus();
      return;
    }
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  root.addEventListener('keydown', handler);
  root.__poeTrap = handler;
};

// Remember and restore the opener when a dialog is mounted outside its own
// trigger component (import, builder, query import).
window.poeRegexRememberFocus = function () {
  window.__poePreviousFocus = document.activeElement;
};

window.poeRegexRestoreFocus = function () {
  const previous = window.__poePreviousFocus;
  window.__poePreviousFocus = null;
  if (previous && previous.isConnected && typeof previous.focus === 'function') {
    previous.focus();
  }
};

// Touch browsers keep focus and sticky hover on the last tapped control.
// Release picker actions so an undone choice returns to its neutral look.
document.addEventListener('pointerup', function (event) {
  if (event.pointerType !== 'touch') return;
  const target = event.target;
  if (!(target instanceof Element)) return;
  if (!target.closest('.modpicker .suggest-row > button')) return;
  window.setTimeout(function () {
    const active = document.activeElement;
    if (active instanceof Element &&
        active.closest('.modpicker .suggest-row > button')) {
      active.blur();
    }
  }, 50);
}, { passive: true });
