#!/usr/bin/env node
// Repeatable browser performance harness for the PoE2 web workbench.
//
// Usage:
//   node tools/web-perf/run.mjs --root /tmp/webperf --runs 5 --out /tmp/perf.json
//
// The root must be a published wwwroot (dotnet publish web/web.csproj ...).
// The harness serves it locally, drives Chromium over CDP, and records
// event-to-DOM-commit latency for the workbench's main UX actions.

import http from 'node:http';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

const args = Object.fromEntries(process.argv.slice(2).map((a, i, all) =>
  a.startsWith('--') ? [a.slice(2), all[i + 1]?.startsWith('--') ? '1' : all[i + 1] ?? '1'] : null
).filter(Boolean));
const root = path.resolve(args.root ?? '/tmp/webperf');
const runs = Number(args.runs ?? 5);
const outPath = path.resolve(args.out ?? '/tmp/poe-web-perf.json');
const screenshotPath = args.screenshot ? path.resolve(args.screenshot) : null;
const screenshotAfterPath = args.screenshotAfter ? path.resolve(args.screenshotAfter) : null;
const httpPort = Number(args.port ?? 8099);
const debugPort = Number(args.debugPort ?? 9222);
const width = Number(args.width ?? 1920);
const height = Number(args.height ?? 1080);
const throttle = Number(args.throttle ?? 1);

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`published site not found: ${root}/index.html`);
  process.exit(2);
}

const mime = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.dat': 'application/octet-stream',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8',
};

const server = http.createServer((req, res) => {
  const urlPath = decodeURIComponent((req.url ?? '/').split('?')[0]);
  let file = path.join(root, urlPath === '/' ? 'index.html' : urlPath);
  if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
  if (!fs.existsSync(file) && !path.extname(file)) file = path.join(root, 'index.html');
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404).end(); return; }
  res.writeHead(200, {
    'Content-Type': mime[path.extname(file)] ?? 'application/octet-stream',
    'Cache-Control': 'no-store',
  });
  fs.createReadStream(file).pipe(res);
});

const sleep = ms => new Promise(r => setTimeout(r, ms));
const percentile = (values, p) => {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil(sorted.length * p) - 1));
  return Number(sorted[index].toFixed(1));
};

let chrome;
let cdp;
let msgId = 0;
const pending = new Map();
const events = [];

function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++msgId;
    pending.set(id, { resolve, reject });
    cdp.send(JSON.stringify({ id, method, params }));
  });
}

async function evalValue(expression, awaitPromise = true) {
  const r = await send('Runtime.evaluate', {
    expression,
    awaitPromise,
    returnByValue: true,
    userGesture: true,
  });
  if (r.exceptionDetails) {
    throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
  }
  return r.result?.value;
}

async function waitFor(expression, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    try {
      if (await evalValue(expression)) return;
    } catch { /* transient during reload */ }
    await sleep(25);
  }
  throw new Error(`timeout waiting for: ${expression}`);
}

async function waitForHttp(url, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    try {
      const r = await fetch(url);
      if (r.ok) return;
    } catch { /* not ready */ }
    await sleep(100);
  }
  throw new Error(`timeout waiting for ${url}`);
}

async function connect() {
  await waitForHttp(`http://127.0.0.1:${debugPort}/json/version`);
  const list = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
  let page = list.find(x => x.type === 'page');
  if (!page) {
    page = await (await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`, { method: 'PUT' })).json();
  }
  cdp = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    cdp.onopen = resolve;
    cdp.onerror = reject;
  });
  cdp.onmessage = event => {
    const msg = JSON.parse(typeof event.data === 'string' ? event.data : Buffer.from(event.data).toString());
    if (msg.id && pending.has(msg.id)) {
      const p = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) p.reject(new Error(msg.error.message));
      else p.resolve(msg.result);
    } else {
      events.push(msg);
    }
  };
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Performance.enable');
  await send('Network.enable');
  await send('Network.setCacheDisabled', { cacheDisabled: true });
}

async function navigate() {
  const url = `http://127.0.0.1:${httpPort}/`;
  await send('Page.navigate', { url });
  await waitFor(`document.readyState === 'complete'`);
  await waitFor(`!!document.querySelector('#mod-search')`, 20000);
}

async function dismissIntro() {
  if (await evalValue(`!!document.querySelector('#intro-dismiss')`)) {
    await evalValue(`document.querySelector('#intro-dismiss').click()`);
    await waitFor(`!document.querySelector('#intro-card')`);
  }
}

async function metrics() {
  const r = await send('Performance.getMetrics');
  return Object.fromEntries(r.metrics.map(m => [m.name, m.value]));
}

function makeActions(sampleText) {
  const sampleJson = JSON.stringify(sampleText);
  return [
    ['mode: advanced', () => `document.querySelector('#mode-advanced').click()`,
      `!!document.querySelector('.inv-doll')`],
    ['slot focus', () => `document.querySelector('#slot-helmet').click()`,
      `!!document.querySelector('#mod-search')`],
    ['mode: simple', () => `document.querySelector('#mode-simple').click()`,
      `!document.querySelector('.inv-doll') && !!document.querySelector('#mod-search')`],
    ['picker expand', () => `document.querySelector('.fam-toggle').click()`,
      `!!document.querySelector('.tier-list')`],
    ['picker collapse', () => `document.querySelector('.fam-toggle').click()`,
      `!document.querySelector('.tier-list')`],
    ['picker filter', () => `(() => { const e=document.querySelector('#mod-search'); e.value='life'; e.dispatchEvent(new Event('input',{bubbles:true})); })()`,
      `[...document.querySelectorAll('.fam-text')].some(e => e.textContent.toLowerCase().includes('life'))`],
    ['family pick', () => `document.querySelector('.fam-row button[id^="pick-inc-"]').click()`,
      `!!document.querySelector('[id^="chip-inc-"]')`],
    ['copy result', () => `document.querySelector('#slot-copy-simple').click()`,
      `true`],
    ['exclusion pick', () => `document.querySelector('.fam-row button[id^="pick-exc-"]').click()`,
      `!!document.querySelector('[id^="chip-exc-"]')`],
    ['explain cold', () => `document.querySelector('#qcard-simple-explain').click()`,
      `!!document.querySelector('#qcard-simple-explain-panel')`, 15000],
    ['explain close', () => `document.querySelector('#qcard-simple-explain').click()`,
      `!document.querySelector('#qcard-simple-explain-panel')`],
    ['explain warm', () => `document.querySelector('#qcard-simple-explain').click()`,
      `!!document.querySelector('#qcard-simple-explain-panel')`],
    ['clear picks', () => `document.querySelector('#chips-clear-global').click()`,
      `!document.querySelector('[id^="chip-inc-"]')`],
    ['bulk add (++)', () => `(() => { const e=document.querySelector('#mod-search'); e.value='life'; e.dispatchEvent(new Event('input',{bubbles:true})); })(); setTimeout(()=>document.querySelector('#suggest-all')?.click(), 0)`,
      `[...document.querySelectorAll('[id^="chip-inc-"]')].length > 0`, 10000],
    ['import open', () => `document.querySelector('#import-open').click()`,
      `!!document.querySelector('#import-dialog')`],
    ['import parse 5 items', () => `(() => { const e=document.querySelector('#import-text'); e.value=${sampleJson}; e.dispatchEvent(new Event('input',{bubbles:true})); })(); setTimeout(()=>document.querySelector('#import-parse').click(), 0)`,
      `!!document.querySelector('#import-batch') || !!document.querySelector('#import-preview')`, 20000],
    ['import cancel', () => `document.querySelector('#import-cancel').click()`,
      `!document.querySelector('#import-dialog')`],
    ['builder open', () => `document.querySelector('#builder-open').click()`,
      `!!document.querySelector('#builder-dialog')`],
    ['builder cancel', () => `document.querySelector('#builder-cancel').click()`,
      `!document.querySelector('#builder-dialog')`],
    ['advanced: reset simple picks', () => `document.querySelector('#chips-clear-global').click()`,
      `!document.querySelector('[id^="chip-inc-"]')`],
    ['advanced: mode', () => `document.querySelector('#mode-advanced').click()`,
      `!!document.querySelector('.inv-doll')`],
    ['advanced: focus helmet', () => `document.querySelector('#slot-helmet').click()`,
      `!!document.querySelector('#mod-search')`],
    ['advanced: pick helmet', () => `(() => { const e=document.querySelector('#mod-search'); e.value='life'; e.dispatchEvent(new Event('input',{bubbles:true})); })(); setTimeout(()=>document.querySelector('.fam-row button[id^="pick-inc-"]')?.click(), 0)`,
      `!!document.querySelector('[id^="chip-inc-helmet"]')`],
    ['advanced: focus body', () => `document.querySelector('#slot-body').click()`,
      `!!document.querySelector('#mod-search')`],
    ['advanced: pick body', () => `(() => { const e=document.querySelector('#mod-search'); e.value='life'; e.dispatchEvent(new Event('input',{bubbles:true})); })(); setTimeout(()=>document.querySelector('.fam-row button[id^="pick-inc-"]')?.click(), 0)`,
      `!!document.querySelector('[id^="chip-inc-body"]')`],
    ['advanced: focus gloves', () => `document.querySelector('#slot-gloves').click()`,
      `!!document.querySelector('#mod-search')`],
    ['advanced: pick gloves', () => `(() => { const e=document.querySelector('#mod-search'); e.value='life'; e.dispatchEvent(new Event('input',{bubbles:true})); })(); setTimeout(()=>document.querySelector('.fam-row button[id^="pick-inc-"]')?.click(), 0)`,
      `!!document.querySelector('[id^="chip-inc-gloves"]')`],
    ['advanced: focus boots', () => `document.querySelector('#slot-boots').click()`,
      `!!document.querySelector('#mod-search')`],
    ['advanced: pick boots', () => `(() => { const e=document.querySelector('#mod-search'); e.value='life'; e.dispatchEvent(new Event('input',{bubbles:true})); })(); setTimeout(()=>document.querySelector('.fam-row button[id^="pick-inc-"]')?.click(), 0)`,
      `!!document.querySelector('[id^="chip-inc-boots"]')`],
    ['advanced: 4-slot paste', () => `true`,
      `!!document.querySelector('#combined-paste-query') && document.querySelector('#combined-paste-query').textContent.length > 0`],
    ['advanced: explain helmet cold', () => `document.querySelector('#qcard-helmet-explain').click()`,
      `!!document.querySelector('#qcard-helmet-explain-panel')`, 15000],
    ['advanced: focus weapon', () => `document.querySelector('#slot-weapon').click()`,
      `!!document.querySelector('#modpicker-weapon')`],
    ['advanced: pick weapon', () => `(async () => { const input=document.querySelector('#mod-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); await new Promise(r=>setTimeout(r,40)); [...document.querySelectorAll('.fam-row button[id^="pick-inc-"]')].find(b=>!b.disabled)?.click(); })()`,
      `!!document.querySelector('[id^="chip-inc-weapon"]')`, 10000],
    ['advanced: focus offhand', () => `document.querySelector('#slot-offhand').click()`,
      `!!document.querySelector('#modpicker-offhand')`],
    ['advanced: pick offhand', () => `(async () => { const input=document.querySelector('#mod-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); await new Promise(r=>setTimeout(r,40)); [...document.querySelectorAll('.fam-row button[id^="pick-inc-"]')].find(b=>!b.disabled)?.click(); })()`,
      `!!document.querySelector('[id^="chip-inc-offhand"]')`, 10000],
    ['advanced: focus belt', () => `document.querySelector('#slot-belt').click()`,
      `!!document.querySelector('#modpicker-belt')`],
    ['advanced: pick belt', () => `(async () => { const input=document.querySelector('#mod-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); await new Promise(r=>setTimeout(r,40)); [...document.querySelectorAll('.fam-row button[id^="pick-inc-"]')].find(b=>!b.disabled)?.click(); })()`,
      `!!document.querySelector('[id^="chip-inc-belt"]')`, 10000],
    ['advanced: focus amulet', () => `document.querySelector('#slot-amulet').click()`,
      `!!document.querySelector('#modpicker-amulet')`],
    ['advanced: pick amulet', () => `(async () => { const input=document.querySelector('#mod-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); await new Promise(r=>setTimeout(r,40)); [...document.querySelectorAll('.fam-row button[id^="pick-inc-"]')].find(b=>!b.disabled)?.click(); })()`,
      `!!document.querySelector('[id^="chip-inc-amulet"]')`, 10000],
    ['advanced: focus ring1', () => `document.querySelector('#slot-ring1').click()`,
      `!!document.querySelector('#modpicker-ring1')`],
    ['advanced: pick ring1', () => `(async () => { const input=document.querySelector('#mod-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); await new Promise(r=>setTimeout(r,40)); [...document.querySelectorAll('.fam-row button[id^="pick-inc-"]')].find(b=>!b.disabled)?.click(); })()`,
      `!!document.querySelector('[id^="chip-inc-ring1"]')`, 10000],
    ['advanced: focus ring2', () => `document.querySelector('#slot-ring2').click()`,
      `!!document.querySelector('#modpicker-ring2')`],
    ['advanced: pick ring2', () => `(async () => { const input=document.querySelector('#mod-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); await new Promise(r=>setTimeout(r,40)); [...document.querySelectorAll('.fam-row button[id^="pick-inc-"]')].find(b=>!b.disabled)?.click(); })()`,
      `!!document.querySelector('[id^="chip-inc-ring2"]')`, 10000],
    ['advanced: 10-slot paste', () => `true`,
      `!!document.querySelector('#combined-paste-query') && document.querySelector('#combined-paste-query').textContent.length > 0`],
    ['advanced: 10-slot paste update', () => `(async () => {
        const wait=async(fn,ms=3000)=>{const t=Date.now();while(Date.now()-t<ms){if(fn())return true;await new Promise(r=>setTimeout(r,40));}return false;};
        document.querySelector('#slot-weapon')?.click();
        await wait(()=>!!document.querySelector('#modpicker-weapon'));
        const input=document.querySelector('#mod-search');
        if (input) { input.value=''; input.dispatchEvent(new Event('input',{bubbles:true})); }
        await wait(()=>[...document.querySelectorAll('.fam-row button[id^="pick-exc-"]')].some(b=>!b.disabled));
        window.__pasteBefore=document.querySelector('#combined-paste-query')?.textContent||'';
        [...document.querySelectorAll('.fam-row button[id^="pick-exc-"]')].find(b=>!b.disabled)?.click();
        return true;
      })()`,
      `(document.querySelector('#combined-paste-query')?.textContent||'') !== (window.__pasteBefore||'')`, 20000],
    ['advanced: class change', () => `(async () => {
        const wait=async(fn,ms=3000)=>{const t=Date.now();while(Date.now()-t<ms){if(fn())return true;await new Promise(r=>setTimeout(r,40));}return false;};
        document.querySelector('#slot-weapon')?.click();
        await wait(()=>!!document.querySelector('#modpicker-weapon'));
        window.__pasteBefore=document.querySelector('#combined-paste-query')?.textContent||'';
        const sel=document.querySelector('#slot-weapon')?.closest('.gear-slot')?.querySelector('select.gear-class');
        if (sel && sel.options.length>1) { sel.selectedIndex=1; sel.dispatchEvent(new Event('change',{bubbles:true})); }
        return true;
      })()`,
      `(document.querySelector('#combined-paste-query')?.textContent||'') !== (window.__pasteBefore||'')`, 20000],
  ].map(([label, fn, wait, timeout, debug]) => ({ label, fn, wait, timeout, debug }));
}

async function main() {
  await new Promise(resolve => server.listen(httpPort, '127.0.0.1', resolve));
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'poe-web-perf-'));
  chrome = spawn('chromium', [
    '--headless=new', '--no-sandbox', '--disable-gpu',
    `--window-size=${width},${height}`,
    `--remote-debugging-port=${debugPort}`,
    `--user-data-dir=${profile}`,
    'about:blank',
  ], { stdio: 'ignore' });
  try {
    await connect();
    if (throttle > 1)
      await send('Emulation.setCPUThrottlingRate', { rate: throttle });
    const sampleText = fs.readFileSync(path.resolve('docs/samples/real-items.txt'), 'utf8');
    const actions = makeActions(sampleText);
    const samples = new Map();
    const boot = [];
    const longTasks = [];
    const metricsStart = await metrics();

    for (let run = 0; run < runs; run++) {
      await navigate();
      await evalValue('localStorage.clear()');
      const t0 = Date.now();
      await send('Page.reload', { ignoreCache: true });
      await waitFor(`document.readyState === 'complete'`);
      await waitFor(`!!document.querySelector('#mod-search')`, 20000);
      if (screenshotPath && run === 0) {
        const shot = await send('Page.captureScreenshot', { format: 'png' });
        fs.writeFileSync(screenshotPath, Buffer.from(shot.data, 'base64'));
      }
      await dismissIntro();
      const bootMs = await evalValue('performance.now()');
      boot.push({ ms: Number(bootMs.toFixed(1)), wallMs: Date.now() - t0 });
      await evalValue(`(() => {
        window.__longTasks = [];
        if (window.PerformanceObserver) {
          try { new PerformanceObserver(list => {
            for (const e of list.getEntries()) window.__longTasks.push({ start: e.startTime, duration: e.duration });
          }).observe({ type: 'longtask', buffered: false }); } catch {}
        }
        return true;
      })()`);

      for (const item of actions) {
        try {
          await evalValue(`window.__perfT0 = performance.now(); ${item.fn()}`);
          await waitFor(item.wait, item.timeout ?? 10000);
          const ms = await evalValue(`performance.now() - window.__perfT0`);
          if (!samples.has(item.label)) samples.set(item.label, []);
          samples.get(item.label).push(Number(ms.toFixed(1)));
        } catch (error) {
          if (!samples.has(item.label)) samples.set(item.label, []);
          let debug = null;
          if (item.debug) { try { debug = await evalValue(item.debug); } catch {} }
          samples.get(item.label).push({ error: String(error.message ?? error), debug });
        }
      }
      longTasks.push(...await evalValue('window.__longTasks'));
      if (screenshotAfterPath && run === 0) {
        const shot = await send('Page.captureScreenshot', { format: 'png' });
        fs.writeFileSync(screenshotAfterPath, Buffer.from(shot.data, 'base64'));
      }
      // Keep each run independent.
      await evalValue('localStorage.clear()');
    }

    const metricsEnd = await metrics();
    const summary = {};
    for (const [label, values] of samples) {
      const ok = values.filter(v => typeof v === 'number');
      summary[label] = {
        runs: ok.length,
        p50: percentile(ok, 0.5),
        p95: percentile(ok, 0.95),
        values,
      };
    }
    const longTaskValues = longTasks.map(x => x.duration);
    const result = {
      generatedAt: new Date().toISOString(),
      root,
      runs,
      viewport: { width, height },
      cpuThrottle: throttle,
      boot: {
        p50: percentile(boot.map(x => x.ms), 0.5),
        p95: percentile(boot.map(x => x.ms), 0.95),
        values: boot,
      },
      actions: summary,
      longTasks: {
        count: longTaskValues.length,
        max: longTaskValues.length ? Number(Math.max(...longTaskValues).toFixed(1)) : 0,
        over50: longTaskValues.filter(x => x > 50).length,
        over200: longTaskValues.filter(x => x > 200).length,
        values: longTaskValues.slice(0, 30),
      },
      metrics: {
        start: metricsStart,
        end: metricsEnd,
      },
    };
    fs.mkdirSync(path.dirname(outPath), { recursive: true });
    fs.writeFileSync(outPath, JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
  } finally {
    try { cdp?.close(); } catch {}
    try { chrome?.kill('SIGKILL'); } catch {}
    server.close();
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
