// Persisted palette for the workbench. The theme is an attribute on <html>
// (data-theme) so plain CSS can swap the variables. The pre-boot inline script
// in index.html applies the saved theme before first paint; this helper keeps
// the Blazor select, the DOM, the browser chrome color, and other tabs in sync.
(function () {
  const key = "poe-regex.theme";
  const themes = {
    forge: { label: "Forge Black", chrome: "#070809" },
    sandstone: { label: "Sandstone", chrome: "#efe5d5" },
    "sandstone-light": { label: "Sandstone Light", chrome: "#f7f0e4" },
    "sandstone-deep": { label: "Sandstone Deep", chrome: "#e7d7bf" },
    "sandstone-muted": { label: "Sandstone Muted", chrome: "#ece7de" },
    "forge-light": { label: "Forge Light", chrome: "#ecebe8" },
    "forge-steel": { label: "Forge Steel", chrome: "#e9edf0" },
    "forge-graphite": { label: "Forge Graphite", chrome: "#dbdee0" }
  };
  const names = Object.keys(themes);
  const normalize = value => names.indexOf(value) >= 0 ? value : "forge";

  function apply(value) {
    const name = normalize(value);
    document.documentElement.dataset.theme = name;
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute("content", themes[name].chrome);
    return name;
  }

  let notify = null;

  window.poeRegexTheme = {
    themes: themes,
    current() {
      return normalize(document.documentElement.dataset.theme);
    },
    set(value) {
      const name = normalize(value);
      try { localStorage.setItem(key, name); } catch (e) { /* storage may be unavailable */ }
      return apply(name);
    },
    // Blazor hands us a reference so the header select follows a theme picked
    // in another tab (the storage event cannot update component state itself).
    bind(dotNetRef) {
      notify = dotNetRef;
      apply(window.poeRegexTheme.current());
    }
  };

  // A second tab can pick a theme without reloading.
  window.addEventListener("storage", event => {
    if (event.key !== key) return;
    const name = apply(event.newValue);
    if (notify && typeof notify.invokeMethodAsync === "function") {
      notify.invokeMethodAsync("ApplyThemeFromStorage", name).catch(() => {});
    }
  });

  apply(window.poeRegexTheme.current());
})();
