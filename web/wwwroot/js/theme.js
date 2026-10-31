// Persisted palette preference for the workbench. Two concrete themes
// (forge / bench-amber) plus "system", which resolves through the OS/browser
// color-scheme. The applied theme is an attribute on <html> so plain CSS can
// swap the variables. The pre-boot inline script in index.html applies the
// saved preference before first paint; this helper keeps the Blazor select,
// the DOM, the browser chrome color, the OS scheme, and other tabs in sync.
(function () {
  const key = "poe-regex.theme";
  const themes = {
    forge: { label: "Forge Black", chrome: "#070809" },
    "bench-amber": { label: "Bench Amber", chrome: "#3b2a16" }
  };
  const names = Object.keys(themes);
  const preferences = names.concat("system");
  const defaultPreference = "forge";
  const normalize = value => preferences.indexOf(value) >= 0 ? value : defaultPreference;
  const prefersLight = () =>
    !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches);
  const resolve = preference =>
    preference === "system" ? (prefersLight() ? "bench-amber" : "forge") : preference;

  let preference = defaultPreference;
  try {
    preference = normalize(localStorage.getItem(key));
  } catch (e) { /* storage may be unavailable */ }

  function apply(value) {
    preference = normalize(value);
    const theme = resolve(preference);
    document.documentElement.dataset.theme = theme;
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute("content", themes[theme].chrome);
    return theme;
  }

  let notify = null;

  window.poeRegexTheme = {
    themes: themes,
    current() {
      return preference;
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
      apply(preference);
    }
  };

  // A second tab can pick a theme without reloading.
  window.addEventListener("storage", event => {
    if (event.key !== key) return;
    apply(event.newValue);
    if (notify && typeof notify.invokeMethodAsync === "function") {
      notify.invokeMethodAsync("ApplyThemeFromStorage", preference).catch(() => {});
    }
  });

  // While "system" is selected, follow the OS/browser scheme live.
  if (window.matchMedia) {
    const media = window.matchMedia("(prefers-color-scheme: light)");
    const onChange = () => { if (preference === "system") apply("system"); };
    if (media.addEventListener) media.addEventListener("change", onChange);
    else if (media.addListener) media.addListener(onChange);
  }

  apply(preference);
})();
