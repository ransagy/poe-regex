// Update detection for the workbench PWA.
//
// A new service worker installs and waits. Nothing reloads until the page
// asks it to skip waiting, either from the update toast or the automatic idle
// path. The build commit comes from the <meta name="build-commit"> tag that
// build.sh stamps into index.html.
(function () {
  "use strict";

  if (!("serviceWorker" in navigator)) return;
  var host = location.hostname;
  if (host === "localhost" || host === "127.0.0.1" || host === "[::1]") return;

  var meta = document.querySelector('meta[name="build-commit"]');
  var initial = meta && meta.content ? meta.content.trim() : "";
  if (initial === "__BUILD_COMMIT__") initial = "";

  var test = window.__poeUpdateTest || {};
  var idleMs = Number(test.idleMs) || 60000;
  var countdownSeconds = Number(test.countdownSeconds) || 5;
  var state = {
    current: initial,
    latest: initial,
    status: "idle",
    waiting: false,
    reloadOnly: false,
    error: null,
    checkedAt: 0,
  };
  var registration = null;
  var dotnet = null;
  var reloading = false;
  var checking = null;
  var watching = false;
  var autoMode = "ask";
  var idleTimer = null;
  var autoDue = false;
  var lastActivity = Date.now();

  function emit() {
    var copy = JSON.parse(JSON.stringify(state));
    window.__poeUpdate = copy;
    window.dispatchEvent(new CustomEvent("poe-update-change", { detail: copy }));
    if (dotnet) {
      dotnet.invokeMethodAsync("OnUpdateState", JSON.stringify(copy)).catch(function () {});
    }
  }

  function update(patch) {
    Object.assign(state, patch);
    emit();
  }

  function isModalOpen() {
    return !!document.querySelector(".dlg-overlay");
  }

  function isEditing() {
    var el = document.activeElement;
    if (!el) return false;
    var tag = el.tagName;
    return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
  }

  function resetIdle() {
    lastActivity = Date.now();
    if (idleTimer) {
      clearTimeout(idleTimer);
      idleTimer = null;
    }
    if (autoMode === "idle" && (state.waiting || state.reloadOnly) && !reloading) {
      idleTimer = setTimeout(checkIdle, idleMs);
    }
  }

  function checkIdle() {
    idleTimer = null;
    if (autoMode !== "idle" || !(state.waiting || state.reloadOnly) || reloading) return;
    var elapsed = Date.now() - lastActivity;
    if (elapsed < idleMs) {
      idleTimer = setTimeout(checkIdle, idleMs - elapsed);
      return;
    }
    if (document.visibilityState !== "visible" || isModalOpen() || isEditing()) {
      idleTimer = setTimeout(checkIdle, 5000);
      return;
    }
    if (dotnet && !autoDue) {
      autoDue = true;
      dotnet.invokeMethodAsync("OnAutoApplyDue").catch(function () {
        autoDue = false;
      });
    }
  }

  function noteActivity() {
    lastActivity = Date.now();
    if (autoDue && dotnet) {
      autoDue = false;
      dotnet.invokeMethodAsync("OnUserActivity").catch(function () {});
    }
    resetIdle();
  }

  function setAutoMode(mode) {
    autoMode = mode === "idle" ? "idle" : "ask";
    autoDue = false;
    resetIdle();
  }

  function resumeAuto() {
    autoDue = false;
    resetIdle();
  }

  function dismissAuto() {
    autoDue = true;
    if (idleTimer) {
      clearTimeout(idleTimer);
      idleTimer = null;
    }
  }

  function waitForWaiting(timeoutMs) {
    if (registration && registration.waiting) return Promise.resolve(true);
    return new Promise(function (resolve) {
      var settled = false;
      var timer = setTimeout(function () {
        if (!settled) {
          settled = true;
          clearInterval(poll);
          resolve(false);
        }
      }, timeoutMs);
      var poll = setInterval(function () {
        if (registration && registration.waiting) {
          if (!settled) {
            settled = true;
            clearTimeout(timer);
            clearInterval(poll);
            resolve(true);
          }
        }
      }, 200);
    });
  }

  function activeIs(commit) {
    var url = registration && registration.active && registration.active.scriptURL;
    if (!url || !commit) return false;
    var match = url.match(/[?&]v=([^&]+)/);
    return !!match && decodeURIComponent(match[1]) === commit;
  }

  function registerWorker(commit) {
    var url = "/sw.js?v=" + encodeURIComponent(commit || "dev");
    return navigator.serviceWorker.register(url, {
      scope: "/",
      updateViaCache: "none",
    }).then(function (reg) {
      registration = reg;
      if (!watching) {
        watching = true;
        watch(reg);
      }
      return reg;
    });
  }

  function watch(reg) {
    if (reg.waiting) {
      update({ waiting: true, status: "available" });
      resetIdle();
    }
    reg.addEventListener("updatefound", function () {
      var worker = reg.installing;
      if (!worker) return;
      worker.addEventListener("statechange", function () {
        if (worker.state !== "installed") return;
        if (navigator.serviceWorker.controller) {
          autoDue = false;
          update({ waiting: true, status: "available" });
          resetIdle();
        } else {
          update({ status: "idle" });
        }
      });
    });
  }

  function check(manual) {
    if (checking) return checking;
    checking = (async function () {
      update({ status: "checking", error: null });
      try {
        var response = await fetch("/build-info.json", { cache: "no-store" });
        if (!response.ok) throw new Error("build-info " + response.status);
        var info = await response.json();
        var latest = info && info.commit ? String(info.commit) : "";
        if (!latest) throw new Error("missing commit");
        state.checkedAt = Date.now();
        state.latest = latest;
        if (latest !== state.current) {
          update({
            latest: latest,
            status: registration && registration.waiting ? "available" : "downloading",
          });
          try {
            await registerWorker(latest);
          } catch (e) { /* registration.update below */ }
          try {
            if (registration) await registration.update();
          } catch (e) { /* install may already be running */ }
          var waiting = await waitForWaiting(15000);
          if (!waiting && activeIs(latest)) {
            autoDue = false;
            update({ waiting: false, reloadOnly: true, status: "available" });
          } else {
            update({
              waiting: waiting,
              reloadOnly: false,
              status: waiting ? "available" : (manual ? "error" : "idle"),
              error: !waiting && manual ? "The update is taking longer than expected." : null,
            });
          }
        } else {
          var isWaiting = !!(registration && registration.waiting);
          update({
            waiting: isWaiting,
            reloadOnly: false,
            status: isWaiting ? "available" : "uptodate",
          });
        }
      } catch (e) {
        if (manual) {
          update({ status: "error", error: "Could not check for updates." });
        } else {
          update({ status: "idle" });
        }
      } finally {
        checking = null;
        resetIdle();
      }
    })();
    return checking;
  }

  function prepareReload() {
    try {
      sessionStorage.setItem("poe-regex.update-scroll", String(window.scrollY || document.documentElement.scrollTop || 0));
      var body = document.querySelector(".shell-body");
      if (body) sessionStorage.setItem("poe-regex.update-body-scroll", String(body.scrollTop || 0));
      if (state.latest) sessionStorage.setItem("poe-regex.update-applied", state.latest);
    } catch (e) { /* storage may be unavailable */ }
  }

  async function apply() {
    if (reloading) return false;
    if (!registration) return false;

    var waiting = registration.waiting;
    if (!waiting) {
      await check(true);
      waiting = registration.waiting;
    }
    if (!waiting) {
      if (state.reloadOnly || (state.latest && state.latest !== state.current)) {
        prepareReload();
        reloading = true;
        location.reload();
        return true;
      }
      update({ status: "error", error: "No update is ready yet." });
      return false;
    }

    prepareReload();
    update({ status: "applying", error: null });
    reloading = true;
    var reloaded = false;
    var reload = function () {
      if (reloaded) return;
      reloaded = true;
      location.reload();
    };
    waiting.addEventListener("statechange", function () {
      if (waiting.state === "activated") reload();
    });
    waiting.postMessage({ type: "SKIP_WAITING" });
    // The new worker's activation should be quick. The timeout is a fallback
    // for browsers that do not fire statechange on the waiting worker.
    setTimeout(reload, 3000);
    return true;
  }

  function restoreScroll() {
    try {
      var top = Number(sessionStorage.getItem("poe-regex.update-scroll") || 0);
      var body = document.querySelector(".shell-body");
      var bodyTop = Number(sessionStorage.getItem("poe-regex.update-body-scroll") || 0);
      sessionStorage.removeItem("poe-regex.update-scroll");
      sessionStorage.removeItem("poe-regex.update-body-scroll");
      requestAnimationFrame(function () {
        requestAnimationFrame(function () {
          if (top) window.scrollTo(0, top);
          if (body && bodyTop) body.scrollTop = bodyTop;
        });
      });
    } catch (e) { /* storage may be unavailable */ }
  }

  function bootCheck() {
    setTimeout(function () { check(false); }, 3000);
    setInterval(function () { check(false); }, 30 * 60 * 1000);
  }

  async function boot() {
    if (!state.current) {
      try {
        var response = await fetch("/build-info.json", { cache: "no-store" });
        if (response.ok) {
          var info = await response.json();
          if (info && info.commit) state.current = String(info.commit);
        }
      } catch (e) { /* local or preview without build-info */ }
    }
    if (!state.current) state.current = "dev";
    state.latest = state.current;
    emit();
    try {
      await registerWorker(state.current);
      bootCheck();
    } catch (e) {
      update({ status: "error", error: "The offline shell could not start." });
    }

    ["pointerdown", "keydown", "input", "touchstart", "wheel"].forEach(function (name) {
      document.addEventListener(name, noteActivity, { passive: true });
    });
    document.addEventListener("visibilitychange", function () {
      if (document.visibilityState === "visible" &&
          Date.now() - state.checkedAt > 5 * 60 * 1000) {
        check(false);
      }
      resetIdle();
    });
  }

  window.poeRegexUpdates = {
    bind: function (ref) {
      dotnet = ref;
      emit();
    },
    check: function (manual) { return check(!!manual); },
    apply: function () { return apply(); },
    setAutoMode: setAutoMode,
    resetIdle: resetIdle,
    resumeAuto: resumeAuto,
    dismissAuto: dismissAuto,
    restoreScroll: restoreScroll,
    testConfig: function () { return { countdownSeconds: countdownSeconds }; },
    state: function () { return state; },
  };

  if (document.readyState === "complete") boot();
  else window.addEventListener("load", boot);
})();
