// One-line chip rail: pager chips in reserved gutters (with direction and
// hidden counts), wheel-to-horizontal scrolling, and best-effort scroll
// anchoring so removals before the viewport don't slide the picks.
// Wired lazily per row; scroll/resize keep the pager in sync afterwards.
(function () {
  function railOf(row) {
    return row ? row.querySelector(".chips") : null;
  }

  function hide(btn) {
    if (!btn) return;
    btn.classList.remove("show", "hid");
  }

  function label(btn, n, what, atStart) {
    if (!btn) return;
    const text = atStart ? "\u2039 +" + n : "+" + n + " \u203A";
    if (btn.textContent !== text) btn.textContent = text;
    const title = what + " (" + n + " hidden)";
    if (btn.title !== title) {
      btn.title = title;
      btn.setAttribute("aria-label", title);
    }
  }

  // Remember the chip pinned at the left edge (and its offset) so a removal
  // somewhere before the viewport can be compensated on the next sync.
  function remember(row, rail) {
    const r = rail.getBoundingClientRect();
    let anchor = null;
    for (const el of rail.children) {
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

  function sync(row, fromRender) {
    const rail = railOf(row);
    if (!rail) return;
    const start = row.querySelector(".chips-more.start");
    const end = row.querySelector(".chips-more.end");
    const overflowing = rail.scrollWidth > rail.clientWidth + 1;
    const wasPaged = row.dataset.paged === "1";
    row.dataset.paged = overflowing ? "1" : "0";
    if (!overflowing) {
      hide(start);
      hide(end);
      remember(row, rail);
      return;
    }
    // Reserve both gutters (they are display:none until the first overflow;
    // showing them can only narrow the rail, so this never flips back).
    start.classList.add("show");
    end.classList.add("show");
    // Only a render-driven sync restores the anchor: scrolling must never be
    // fought (paging animations fire scroll events of their own).
    if (fromRender) restoreAnchor(row, rail);
    const r = rail.getBoundingClientRect();
    let before = 0;
    let after = 0;
    let clipStart = false;
    let clipEnd = false;
    for (const el of rail.children) {
      const b = el.getBoundingClientRect();
      if (b.right <= r.left + 1) before++;
      else if (b.left >= r.right - 1) after++;
      else {
        if (b.left < r.left - 1) clipStart = true;
        if (b.right > r.right + 1) clipEnd = true;
      }
    }
    const startN = before + (clipStart ? 1 : 0);
    const endN = after + (clipEnd ? 1 : 0);
    start.classList.toggle("hid", startN === 0);
    end.classList.toggle("hid", endN === 0);
    if (startN > 0) label(start, startN, "show earlier picks", true);
    if (endN > 0) label(end, endN, "show later picks", false);
    remember(row, rail);
    if (overflowing !== wasPaged) requestAnimationFrame(() => sync(row, fromRender));
  }

  function page(rail, dir) {
    const items = Array.from(rail.children);
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
      if (row.dataset.paged !== "1") return;
      const step = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? rail.clientWidth : 1;
      const delta = e.deltaY * step;
      const atStart = rail.scrollLeft <= 0;
      const atEnd = rail.scrollLeft >= rail.scrollWidth - rail.clientWidth - 1;
      if ((delta < 0 && atStart) || (delta > 0 && atEnd)) {
        // At the rail's ends the family list (a sibling, so browsers won't
        // chain to it) keeps the wheel moving.
        const list = row.parentElement ? row.parentElement.querySelector(".mod-list") : null;
        if (!list) return;
        list.scrollTop += delta;
        e.preventDefault();
        return;
      }
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
