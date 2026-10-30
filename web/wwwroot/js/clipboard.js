// Clipboard helper with a textarea fallback for non-secure contexts.
window.poeRegexCopy = async function (text) {
  try {
    await navigator.clipboard.writeText(text);
    return "copied";
  } catch (e) {
    try {
      const ta = document.createElement("textarea");
      ta.value = text;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand("copy");
      document.body.removeChild(ta);
      return ok ? "copied" : "unavailable";
    } catch (e2) {
      return "unavailable";
    }
  }
};

// Auto-copy runs after a pick changes; without a fresh user gesture (page load, restore) the browser would refuse, so skip quietly.
window.poeRegexAutoCopy = async function (text) {
  if (navigator.userActivation && !navigator.userActivation.isActive) return "skipped";
  return window.poeRegexCopy(text);
};
