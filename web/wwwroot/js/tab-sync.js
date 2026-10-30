// Cross-tab workbench handoff. A share link opened in another tab writes a
// marker; this tab freezes its workbench writes before asking the app to show
// the reload dialog.
window.__poeWorkbenchFrozen = false;

window.poeRegexSetWorkbench = function (key, value) {
  if (window.__poeWorkbenchFrozen) return;
  localStorage.setItem(key, value);
};

window.poeRegexWatchHandoff = function (dotnet) {
  if (window.__poeHandoffHandler) return;

  window.__poeHandoffHandler = function (event) {
    if (event.key !== 'poe-regex.handoff.v1' || !event.newValue) return;
    // Set this before the async .NET call so an in-flight state change cannot
    // overwrite the workbench that the other tab just installed.
    window.__poeWorkbenchFrozen = true;
    dotnet.invokeMethodAsync('OnSharedRecipeOpenedElsewhere').catch(function () {});
  };

  window.addEventListener('storage', window.__poeHandoffHandler);
};

window.poeRegexUnwatchHandoff = function () {
  if (!window.__poeHandoffHandler) return;
  window.removeEventListener('storage', window.__poeHandoffHandler);
  window.__poeHandoffHandler = null;
};
