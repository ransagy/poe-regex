// One-line chip rail: shows pager chips in reserved gutters when the rail
// overflows, keeps their counts honest, and pages the rail by a window.
// Wired lazily per row; scroll/resize keep it in sync afterwards.
(function () {
  function railOf(row) {
    return row ? row.querySelector(".chips") : null;
  }

  function hide(btn) {
    if (!btn) return;
    btn.classList.remove("show", "hid");
  }

  function label(btn, n, what) {
    if (!btn) return;
    const text = "\u2026 +" + n;
    if (btn.textContent !== text) btn.textContent = text;
    const title = what + " (" + n + ")";
    if (btn.title !== title) {
      btn.title = title;
      btn.setAttribute("aria-label", title);
    }
  }

  function sync(row) {
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
      return;
    }
    // Reserve both gutters (they are display:none until the first overflow;
    // showing them can only narrow the rail, so this never flips back).
    start.classList.add("show");
    end.classList.add("show");
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
    if (startN > 0) label(start, startN, "show picks hidden at the start");
    if (endN > 0) label(end, endN, "show picks hidden at the end");
    if (overflowing !== wasPaged) requestAnimationFrame(() => sync(row));
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
    rail.addEventListener("scroll", () => sync(row), { passive: true });
    if (window.ResizeObserver) new ResizeObserver(() => sync(row)).observe(rail);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => sync(row));
    for (const btn of row.querySelectorAll(".chips-more")) {
      btn.addEventListener("click", () => page(rail, Number(btn.dataset.dir) || 1));
    }
  }

  window.poeRegexChipsSync = function (rowId) {
    const row = document.getElementById(rowId);
    if (!row) return;
    wire(row);
    sync(row);
  };

  window.poeRegexChipsEnd = function (rowId) {
    const row = document.getElementById(rowId);
    if (!row) return;
    const rail = railOf(row);
    if (!rail) return;
    // Jump to the tail, left-aligned on a chip start so the newest picks read
    // as full labels rather than half ones.
    const max = rail.scrollWidth - rail.clientWidth;
    let target = 0;
    for (const el of rail.children) {
      if (el.offsetLeft <= max) target = el.offsetLeft;
      else break;
    }
    rail.scrollLeft = target;
    sync(row);
  };
})();
