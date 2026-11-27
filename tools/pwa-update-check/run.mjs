#!/usr/bin/env node
// Browser acceptance check for the waiting-worker update flow.
//
// Usage:
//   ./build.sh
//   node tools/pwa-update-check/run.mjs --root out/web/wwwroot
//
// The published root must include build-info.json (build.sh writes it). The
// harness serves the current build as A, then simulates B by changing
// build-info.json, sw.js, and index.html. Three scenarios run in fresh
// browser profiles: manual update, automatic idle, and a share link opened
// while an update is waiting.

import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { spawn } from 'node:child_process';

const args = Object.fromEntries(process.argv.slice(2).map((a, i, all) =>
  a.startsWith('--') ? [a.slice(2), all[i + 1]?.startsWith('--') ? '1' : all[i + 1] ?? '1'] : null
).filter(Boolean));
const root = path.resolve(args.root ?? 'out/web/wwwroot');
const port = Number(args.port ?? 8124);
const baseDebugPort = Number(args.debugPort ?? 9254);
const host = args.host ?? 'pwa.test';
const baseUrl = `http://${host}:${port}/`;
const chromeBin = args.chrome ?? process.env.CHROME_BIN ?? 'chromium';

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`published site not found: ${root}/index.html`);
  process.exit(2);
}
if (!fs.existsSync(path.join(root, 'build-info.json'))) {
  console.error('build-info.json is missing; run ./build.sh before this check');
  process.exit(2);
}

const buildInfo = JSON.parse(fs.readFileSync(path.join(root, 'build-info.json'), 'utf8'));
const aCommit = String(buildInfo.commit ?? '');
if (!aCommit || aCommit === '__BUILD_COMMIT__') {
  console.error('build-info.json has no commit; run ./build.sh before this check');
  process.exit(2);
}
const aIndex = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
if (!aIndex.includes(aCommit)) {
  console.error('index.html does not carry the build-info commit; run ./build.sh before this check');
  process.exit(2);
}

const mime = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
  '.md': 'text/plain; charset=utf-8',
};

let override = null;
const server = http.createServer((req, res) => {
  const pathname = decodeURIComponent((req.url ?? '/').split('?')[0]);
  if (pathname === '/build-info.json') {
    res.writeHead(200, { 'Content-Type': mime['.json'], 'Cache-Control': 'no-store' });
    res.end(JSON.stringify({
      commit: override ?? aCommit,
      branch: buildInfo.branch ?? 'main',
    }));
    return;
  }
  let file = path.join(root, pathname === '/' ? 'index.html' : pathname);
  if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
  if (!fs.existsSync(file) && !path.extname(file)) file = path.join(root, 'index.html');
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404).end(); return; }

  let body = fs.readFileSync(file);
  if (override && pathname === '/sw.js') {
    body = Buffer.concat([body, Buffer.from(`\n// build: ${override}\n`)]);
  }
  if (override && pathname === '/index.html') {
    body = Buffer.from(body.toString('utf8').split(aCommit).join(override));
  }
  res.writeHead(200, {
    'Content-Type': mime[path.extname(file)] ?? 'application/octet-stream',
    'Cache-Control': 'no-store',
  });
  res.end(body);
});

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
let debugPort = baseDebugPort;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function commitFor(seed) {
  return seed.repeat(40).slice(0, 40);
}

function shareToken() {
  const slots = {
    weapon: ['Crossbow', []],
    offhand: ['Shield', []],
    helmet: ['Helmet', [{
      ModId: 'IncreasedLife8',
      Names: null,
      VariantModIds: ['IncreasedLife8', 'IncreasedLife9'],
      MinValue: null,
      MaxValue: null,
      RangeModIds: null,
    }]],
    body: ['Body Armour', []],
    gloves: ['Gloves', []],
    boots: ['Boots', []],
    belt: ['Belt', []],
    amulet: ['Amulet', []],
    ring1: ['Ring', []],
    ring2: ['Ring', []],
  };
  const snapshot = {
    Version: 7,
    Mode: 'advanced',
    SelectedSlot: null,
    MultiFocus: 'helmet',
    Slots: Object.fromEntries(Object.entries(slots).map(([key, [classId, included]]) =>
      [key, { ClassId: classId, Included: included, Excluded: [], MatchAll: false }])),
    Global: { ClassId: '', Included: [], Excluded: [], MatchAll: false },
    TestItems: [],
    ExternalIncludes: [],
    Game: 'poe2',
  };
  return zlib.deflateRawSync(Buffer.from(JSON.stringify(snapshot))).toString('base64url');
}

async function withPage(name, options, body) {
  override = null;
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), `pwa-update-${name}-`));
  const dport = debugPort++;
  const chrome = spawn(chromeBin, [
    '--headless=new', '--no-sandbox', '--disable-gpu',
    '--window-size=1280,900',
    `--host-resolver-rules=MAP ${host} 127.0.0.1`,
    `--unsafely-treat-insecure-origin-as-secure=${baseUrl}`,
    `--remote-debugging-port=${dport}`,
    `--user-data-dir=${profile}`,
    'about:blank',
  ], { stdio: 'ignore' });

  let ws;
  try {
    for (let i = 0; i < 300; i++) {
      try {
        const list = await (await fetch(`http://127.0.0.1:${dport}/json/list`)).json();
        const page = list.find((x) => x.type === 'page');
        if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); break; }
      } catch { /* browser not ready */ }
      await sleep(100);
    }
    if (!ws) throw new Error(`${name}: browser did not start`);
    await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });

    let id = 0;
    const pending = new Map();
    ws.onmessage = (event) => {
      const message = JSON.parse(typeof event.data === 'string' ? event.data : Buffer.from(event.data).toString());
      if (!message.id || !pending.has(message.id)) return;
      const entry = pending.get(message.id);
      pending.delete(message.id);
      message.error ? entry.reject(new Error(JSON.stringify(message.error))) : entry.resolve(message.result);
    };
    const send = (method, params = {}) => new Promise((resolve, reject) => {
      const messageId = ++id;
      pending.set(messageId, { resolve, reject });
      ws.send(JSON.stringify({ id: messageId, method, params }));
    });
    await send('Page.enable');
    await send('Runtime.enable');
    if (options) {
      await send('Page.addScriptToEvaluateOnNewDocument', {
        source: `window.__poeUpdateTest = ${JSON.stringify(options)};`,
      });
    }
    const ev = async (expression) => {
      const result = await send('Runtime.evaluate', {
        expression, returnByValue: true, awaitPromise: true, userGesture: true,
      });
      if (result.exceptionDetails) {
        throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
      }
      return result.result?.value;
    };
    const wait = async (expression, timeoutMs = 20000) => {
      const started = Date.now();
      while (Date.now() - started < timeoutMs) {
        try { if (await ev(expression)) return true; } catch { /* navigation */ }
        await sleep(150);
      }
      return false;
    };
    const page = { send, ev, wait };
    const open = async (url = baseUrl) => {
      await send('Page.navigate', { url });
      assert(await wait("!!document.querySelector('#mod-search')", 30000), `${name}: app did not load`);
      if (!(await wait("!!navigator.serviceWorker.controller", 5000))) {
        await send('Page.reload');
        assert(await wait("!!document.querySelector('#mod-search')", 30000), `${name}: app did not reload`);
        assert(await wait("!!navigator.serviceWorker.controller", 20000), `${name}: page not controlled`);
      }
      await sleep(400);
    };
    const chooseIdle = async () => {
      await ev("document.querySelector('#options-open').click()");
      assert(await wait("!!document.querySelector('#update-mode-idle')", 5000), `${name}: options did not open`);
      await ev("document.querySelector('#update-mode-idle').click()");
      await sleep(250);
      await ev("document.querySelector('.options-backdrop')?.click()");
      await sleep(250);
      assert(await ev("localStorage.getItem('poe-regex.update-mode') === 'idle'"), `${name}: idle mode not saved`);
    };
    const buildCommit = () => ev("document.querySelector('meta[name=build-commit]')?.content ?? ''");
    const waitBuild = (commit, timeoutMs = 20000) =>
      wait(`document.querySelector('meta[name=build-commit]')?.content === '${commit}'`, timeoutMs);
    await body({ ...page, open, chooseIdle, buildCommit, waitBuild });
    console.log(`${name}: ok`);
  } finally {
    try { ws?.close(); } catch { /* already closed */ }
    try { chrome.kill('SIGTERM'); } catch { /* already exited */ }
    setTimeout(() => {
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 3 }); } catch { /* temp cleanup */ }
    }, 500);
  }
}

try {
  await new Promise((resolve) => server.listen(port, '127.0.0.1', resolve));

  await withPage('manual', null, async (page) => {
    await page.open();
    const b = commitFor('b');
    override = b;
    await page.ev('poeRegexUpdates.check(true)');
    assert(await page.wait("window.__poeUpdate?.waiting === true", 30000), 'manual: worker did not wait');
    assert(await page.ev("!!document.querySelector('#update-now') && !document.querySelector('#update-now').disabled"),
      'manual: update button missing or disabled');
    assert(await page.buildCommit() === aCommit, 'manual: update applied before the user asked');
    await sleep(700);
    assert(await page.buildCommit() === aCommit, 'manual: update reloaded without user action');
    await page.ev("document.querySelector('#update-later').click()");
    assert(await page.wait("!document.querySelector('#update-toast')", 5000), 'manual: Later did not hide the toast');
    assert(await page.ev("!!document.querySelector('.options-dot')"), 'manual: Options did not keep the update dot');
    await page.ev("document.querySelector('#options-open').click()");
    assert(await page.wait("!!document.querySelector('#update-apply-options')", 5000), 'manual: Options update action missing');
    await page.ev("document.querySelector('#update-apply-options').click()");
    assert(await page.waitBuild(b, 30000), 'manual: update did not reload to the new build');
  });

  await withPage('auto', { idleMs: 1200, countdownSeconds: 1 }, async (page) => {
    await page.open();
    await page.chooseIdle();
    const b = commitFor('c');
    override = b;
    await page.ev('poeRegexUpdates.check(true)');
    assert(await page.wait("window.__poeUpdate?.waiting === true", 30000), 'auto: worker did not wait');
    await sleep(500);
    assert(await page.buildCommit() === aCommit, 'auto: update applied before the idle window');
    assert(await page.waitBuild(b, 15000), 'auto: idle update did not reload');
  });

  await withPage('share', { idleMs: 2500, countdownSeconds: 1 }, async (page) => {
    await page.open();
    await page.chooseIdle();
    const b = commitFor('d');
    override = b;
    await page.ev('poeRegexUpdates.check(true)');
    assert(await page.wait("window.__poeUpdate?.waiting === true", 30000), 'share: worker did not wait');

    const token = shareToken();
    await page.send('Page.navigate', { url: `${baseUrl}?shared=1#r=${token}` });
    assert(await page.wait("!!document.querySelector('#mod-search')", 30000), 'share: app did not load the link');
    assert(await page.wait(
      "(() => { try { return JSON.parse(localStorage.getItem('poe-regex.workbench.v2.poe2')).Slots.helmet.Included.length > 0; } catch { return false; } })()",
      20000), 'share: picks were not restored');
    const atApply = await page.ev("({ build: document.querySelector('meta[name=build-commit]')?.content, hash: location.hash, notice: document.querySelector('.bench-notice')?.innerText ?? '' })");
    assert(atApply.build === aCommit, 'share: update reloaded before the handoff finished');
    assert(atApply.hash === '', 'share: share hash was not cleared after the handoff');
    assert(atApply.notice.includes('Opened shared picks'), 'share: handoff notice missing');
    assert(await page.waitBuild(b, 20000), 'share: update did not reload after the handoff');
    const after = await page.ev("(() => { try { return JSON.parse(localStorage.getItem('poe-regex.workbench.v2.poe2')).Slots.helmet.Included.length; } catch { return 0; } })()");
    assert(after > 0, 'share: shared picks were lost across the update reload');

    const savedBefore = await page.ev("localStorage.getItem('poe-regex.saved-picks.v2.poe2') ?? ''");
    await page.send('Page.navigate', { url: `${baseUrl}?shared=2#r=${token}` });
    assert(await page.wait("!!document.querySelector('#mod-search')", 30000), 'share: retry did not load');
    assert(await page.wait("location.hash === ''", 10000), 'share: repeated link did not clear the hash');
    const savedAfter = await page.ev("localStorage.getItem('poe-regex.saved-picks.v2.poe2') ?? ''");
    assert(savedAfter === savedBefore, 'share: repeated link created a duplicate saved pick');
  });

  console.log('pwa-update-check: all scenarios passed');
} catch (error) {
  console.error(`pwa-update-check: ${error.message}`);
  process.exitCode = 1;
} finally {
  server.close();
}
