// One-line chip rail: pagers always sit in their gutters with the true count
// (disabled + grayed when nothing is hidden that way), the mouse wheel scrolls
// the rail sideways (and continues into the family list when the rail can't
// move), and a best-effort anchor keeps the leading chip pinned across
// removals. Wired lazily per row; scroll/resize keep it in sync afterwards.
(function () {
  function railOf(row) {
    return row ? row.querySelector(".chips") : null;
  }

  function chipsOf(rail) {
    return Array.from(rail.children).filter((el) => el.classList.contains("chip"));
  }

  // Remember the chip pinned at the left edge (and its offset) so a removal
  // somewhere before the viewport can be compensated on the next sync.
  function remember(row, rail) {
    const r = rail.getBoundingClientRect();
    let anchor = null;
    for (const el of chipsOf(rail)) {
      if (el.getBoundingClientRect().left >= r.left - 1) {
        anchor = el;
        break;
      }
    }
    if (!anchor) {
      delete row.dataset.anchor;
      delete row.dataset.anchorDx;
      return;
    }
    row.dataset.anchor = anchor.id;
    row.dataset.anchorDx = String(anchor.offsetLeft - rail.scrollLeft);
  }

  function restoreAnchor(row, rail) {
    if (!row.dataset.anchor) return;
    const anchor = document.getElementById(row.dataset.anchor);
    if (!anchor || !rail.contains(anchor)) return;
    rail.scrollLeft = anchor.offsetLeft - Number(row.dataset.anchorDx || 0);
  }

  function setPager(btn, n, atStart) {
    if (!btn) return;
    const span = btn.querySelector(".n");
    const text = "+" + n;
    if (span && span.textContent !== text) span.textContent = text;
    btn.disabled = n === 0;
    const title = n > 0
      ? (atStart ? "show earlier picks (" : "show later picks (") + n + " hidden)"
      : (atStart ? "no earlier picks" : "no later picks");
    if (btn.title !== title) {
      btn.title = title;
      btn.setAttribute("aria-label", title);
    }
  }

  function sync(row, fromRender) {
    const rail = railOf(row);
    if (!rail) return;
    // Only a render-driven sync restores the anchor: scrolling must never be
    // fought (paging animations fire scroll events of their own).
    if (fromRender) restoreAnchor(row, rail);
    const r = rail.getBoundingClientRect();
    let before = 0;
    let after = 0;
    let clipStart = false;
    let clipEnd = false;
    for (const el of chipsOf(rail)) {
      const b = el.getBoundingClientRect();
      if (b.right <= r.left + 1) before++;
      else if (b.left >= r.right - 1) after++;
      else {
        if (b.left < r.left - 1) clipStart = true;
        if (b.right > r.right + 1) clipEnd = true;
      }
    }
    setPager(row.querySelector(".chips-more.start"), before + (clipStart ? 1 : 0), true);
    setPager(row.querySelector(".chips-more.end"), after + (clipEnd ? 1 : 0), false);
    remember(row, rail);
  }

  function page(rail, dir) {
    const items = chipsOf(rail);
    if (items.length === 0) return;
    const max = rail.scrollWidth - rail.clientWidth;
    if (dir > 0) {
      // Land on the first chip that starts beyond the current right edge, so
      // the left edge of the rail always starts a label (never cuts one).
      const edge = rail.scrollLeft + rail.clientWidth - 4;
      const target = items.find((el) => el.offsetLeft >= edge);
      rail.scrollTo({ left: target ? Math.min(target.offsetLeft, max) : max, behavior: "smooth" });
    } else {
      const want = Math.max(0, rail.scrollLeft - rail.clientWidth * 0.85);
      let target = items[0];
      for (const el of items) {
        if (el.offsetLeft <= want) target = el;
        else break;
      }
      rail.scrollTo({ left: target.offsetLeft, behavior: "smooth" });
    }
  }

  function wire(row) {
    if (row.dataset.wired) return;
    row.dataset.wired = "1";
    const rail = railOf(row);
    rail.addEventListener("scroll", () => sync(row, false), { passive: true });
    if (window.ResizeObserver) new ResizeObserver(() => sync(row, false)).observe(rail);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => sync(row, false));
    for (const btn of row.querySelectorAll(".chips-more")) {
      btn.addEventListener("click", () => page(rail, Number(btn.dataset.dir) || 1));
    }
    rail.addEventListener("wheel", (e) => {
      if (Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return; // native horizontal gesture
      const step = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? rail.clientWidth : 1;
      const delta = e.deltaY * step;
      const max = rail.scrollWidth - rail.clientWidth;
      const canScroll = max > 0 &&
        ((delta < 0 && rail.scrollLeft > 0) || (delta > 0 && rail.scrollLeft < max - 1));
      if (!canScroll) return; // at the ends nothing else moves — the list stays unlinked
      rail.scrollLeft += delta;
      e.preventDefault();
    }, { passive: false });
  }

  window.poeRegexChipsSync = function (rowId) {
    const row = document.getElementById(rowId);
    if (!row) return;
    wire(row);
    sync(row, true);
  };
})();
