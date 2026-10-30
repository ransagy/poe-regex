// Cross-tab workbench coordination. Any workbench write from another tab
// freezes this tab before it can overwrite the new state; the share-link
// handoff gets a specific message.
window.__poeWorkbenchFrozen = false;

window.poeRegexSetWorkbench = function (key, value) {
  if (window.__poeWorkbenchFrozen) return;
  localStorage.setItem(key, value);
};

window.poeRegexReload = function () {
  location.reload();
};

window.poeRegexWatchTabs = function (dotnet) {
  if (window.__poeTabHandler) return;

  const workbenchKey = 'poe-regex.workbench.v1';
  const handoffKey = 'poe-regex.handoff.v1';
  let timer = null;

  const freeze = function () {
    // Set this before the async .NET call so an in-flight state change cannot
    // overwrite the workbench that the other tab just installed.
    window.__poeWorkbenchFrozen = true;
  };
  const invoke = function (method) {
    dotnet.invokeMethodAsync(method).catch(function () {});
  };
  const scheduleGeneric = function () {
    if (timer) clearTimeout(timer);
    timer = setTimeout(function () {
      timer = null;
      invoke('OnWorkbenchChangedInOtherTab');
    }, 300);
  };

  window.__poeTabHandler = function (event) {
    if (event.key === handoffKey && event.newValue) {
      freeze();
      if (timer) { clearTimeout(timer); timer = null; }
      invoke('OnSharedRecipeOpenedElsewhere');
      return;
    }
    if (event.key === workbenchKey || event.key === null) {
      freeze();
      scheduleGeneric();
    }
  };

  window.addEventListener('storage', window.__poeTabHandler);
};

window.poeRegexUnwatchTabs = function () {
  if (!window.__poeTabHandler) return;
  window.removeEventListener('storage', window.__poeTabHandler);
  window.__poeTabHandler = null;
};
