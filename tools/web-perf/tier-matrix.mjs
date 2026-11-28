#!/usr/bin/env node
// Breadth matrix for the picker's first/middle/last tier, exact and >=.
//
// This complements run.mjs (which measures the main UX flow). It walks every
// expanded family in every gear slot, picks the first, middle, and last tier
// both exactly and as "or better", clears between actions, and records the
// click-to-query latency. Use it as a release check; it is intentionally
// separate from the merge-gating run because a full pass takes minutes.

import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

const args = Object.fromEntries(process.argv.slice(2).map((a, i, all) =>
  a.startsWith('--') ? [a.slice(2), all[i + 1]?.startsWith('--') ? '1' : all[i + 1] ?? '1'] : null
).filter(Boolean));
const root = path.resolve(args.root ?? 'out/web/wwwroot');
const outPath = path.resolve(args.out ?? '/tmp/opencode/tier-matrix.json');
const maxMs = Number(args['max-ms'] ?? 500);
const maxFamilies = args['max-families'] ? Number(args['max-families']) : Infinity;
const port = Number(args.port ?? 8098);
const debugPort = port + 1;

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`tier-matrix: published wwwroot not found at ${root}`);
  process.exit(2);
}

const mime = {
  '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.wasm': 'application/wasm',
  '.dat': 'application/octet-stream', '.woff': 'font/woff', '.woff2': 'font/woff2',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon',
  '.txt': 'text/plain', '.md': 'text/markdown',
};
const server = http.createServer((req, res) => {
  let p = decodeURIComponent((req.url ?? '/').split('?')[0]);
  let f = path.join(root, p === '/' ? 'index.html' : p);
  if (!f.startsWith(root)) { res.writeHead(403).end(); return; }
  if (!fs.existsSync(f) && !path.extname(f)) f = path.join(root, 'index.html');
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404).end(); return; }
  res.writeHead(200, {
    'Content-Type': mime[path.extname(f)] ?? 'application/octet-stream',
    'Cache-Control': 'no-store',
  });
  fs.createReadStream(f).pipe(res);
});
await new Promise(r => server.listen(port, '127.0.0.1', r));

const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'poe-tier-matrix-'));
const chrome = spawn(process.env.CHROME_BIN ?? 'chromium', [
  '--headless=new', '--no-sandbox', '--disable-gpu',
  '--window-size=1600,1100', `--remote-debugging-port=${debugPort}`,
  `--user-data-dir=${profile}`, 'about:blank',
], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let ws;
try {
  for (let i = 0; i < 100; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
      const page = list.find(x => x.type === 'page');
      if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); break; }
    } catch {}
    await sleep(100);
  }
  if (!ws) throw new Error('Chromium did not expose a page');
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (!m.id || !pending.has(m.id)) return;
    const p = pending.get(m.id); pending.delete(m.id);
    m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result);
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const i = ++id; pending.set(i, { resolve, reject });
    ws.send(JSON.stringify({ id: i, method, params }));
  });
  await send('Page.enable');
  await send('Runtime.enable');
  const ev = async expression => (await send('Runtime.evaluate', {
    expression, returnByValue: true, awaitPromise: true, userGesture: true,
  })).result?.value;
  const wait = async (expr, ms = 30000) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      if (await ev(`!!(${expr})`)) return true;
      await sleep(40);
    }
    return false;
  };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  await wait(`document.querySelector('#mod-search')`, 30000);
  const intro = '#intro-dismiss-top, #intro-dismiss-side';
  if (await ev(`!!document.querySelector('${intro}')`)) {
    await ev(`document.querySelector('${intro}').click()`);
    await wait(`!document.querySelector('#intro-card-top') && !document.querySelector('#intro-card-side')`);
  }
  await ev(`document.querySelector('#mode-advanced').click()`);
  await wait(`document.querySelector('#slot-gloves')`);

  // Warm the first class/range path once. The cold first pick is measured by
  // run.mjs separately; the matrix is a breadth check of the pick paths.
  await ev(`document.querySelector('#slot-gloves').click()`);
  await wait(`document.querySelector('#modpicker-gloves #mod-search')`);
  await ev(`(() => { const e=document.querySelector('#modpicker-gloves #mod-search'); e.value='fire resistance'; e.dispatchEvent(new Event('input',{bubbles:true})); })()`);
  await wait(`document.querySelector('#pick-tier-up-FireResist6')`, 10000);
  await ev(`document.querySelector('#pick-tier-up-FireResist6')?.click()`);
  await wait(`document.querySelector('[id^="chip-inc-gloves"]')`, 5000);
  await ev(`document.querySelector('#chips-clear-gloves')?.click()`);
  await wait(`!document.querySelector('[id^="chip-inc-gloves"]')`, 5000);

  const slots = ['gloves', 'helmet', 'body', 'boots', 'belt', 'amulet',
    'ring1', 'ring2', 'weapon', 'offhand'];
  const results = [];
  const errors = [];

  async function clear(slot) {
    await ev(`document.querySelector('#chips-clear-${slot}')?.click()`);
    await wait(`!document.querySelector('[id^="chip-inc-${slot}"]')`, 5000);
    await sleep(40);
  }

  async function measure(slot, familyId, tierIndex, mode) {
    const block = `document.getElementById(${JSON.stringify(familyId)})?.closest('.fam-block')`;
    if (!await ev(`!!(${block})`)) return null;
    if (await ev(`${block}.querySelector('.fam-toggle')?.getAttribute('aria-expanded') !== 'true'`)) {
      await ev(`${block}.querySelector('.fam-toggle')?.click()`);
      await wait(`${block}.querySelector('.tier-list')`, 5000);
    }
    const tierRows = await ev(`(() => {
      const b=${block}; if(!b) return 0;
      return b.querySelectorAll('.tier-row').length;
    })()`);
    if (tierIndex >= tierRows) return null;
    const button = mode === 'up'
      ? `document.querySelectorAll('.tier-row')[${tierIndex}]?.querySelector('button.up')`
      : `document.querySelectorAll('.tier-row')[${tierIndex}]?.querySelector('button.inc')`;
    // The global selector above can cross families; resolve inside the block.
    const click = mode === 'up'
      ? `${block}.querySelectorAll('.tier-row')[${tierIndex}]?.querySelector('button.up')`
      : `${block}.querySelectorAll('.tier-row')[${tierIndex}]?.querySelector('button.inc')`;
    if (!await ev(`!!(${click})`) || await ev(`!!(${click})?.disabled`)) return null;

    const ms = await ev(`(async () => {
      const q=()=>document.querySelector('#slot-query-advanced')?.innerText||'';
      const chips=()=>document.querySelectorAll('[id^="chip-inc-${slot}"]').length;
      const beforeQ=q(), beforeC=chips();
      const t0=performance.now();
      (${click}).click();
      const end=t0+10000;
      while(performance.now()<end){
        if(q()!==beforeQ || chips()!==beforeC) return performance.now()-t0;
        await new Promise(r=>requestAnimationFrame(r));
      }
      return -1;
    })()`);
    if (typeof ms !== 'number' || ms < 0) {
      errors.push({ slot, familyId, tierIndex, mode, error: 'no query/chip change' });
      await clear(slot);
      return null;
    }
    const query = await ev(`document.querySelector('#slot-query-advanced')?.innerText || ''`);
    await clear(slot);
    return { slot, familyId, tierIndex, mode, ms: Number(ms.toFixed(1)), query };
  }

  for (const slot of slots) {
    if (!await ev(`!!document.querySelector('#slot-${slot}')`)) continue;
    await ev(`document.querySelector('#slot-${slot}').click()`);
    if (!await wait(`document.querySelector('#modpicker-${slot} #mod-search')`, 5000)) continue;
    await ev(`(() => { const e=document.querySelector('#modpicker-${slot} #mod-search'); e.value=''; e.dispatchEvent(new Event('input',{bubbles:true})); })()`);
    await sleep(100);
    const showAll = `#modpicker-${slot} #mod-show-all`;
    if (await ev(`!!document.querySelector(${JSON.stringify(showAll)})`)) {
      const text = await ev(`document.querySelector(${JSON.stringify(showAll)})?.innerText || ''`);
      if (/show all/i.test(text)) {
        await ev(`document.querySelector(${JSON.stringify(showAll)}).click()`);
        await sleep(250);
      }
    }
    await wait(`document.querySelector('#modpicker-${slot} .fam-row')`, 10000);

    const families = await ev(`[...document.querySelectorAll('#modpicker-${slot} .fam-block')]
      .map(b => b.querySelector('.fam-toggle')?.id)
      .filter(Boolean)`);
    const limited = Number.isFinite(maxFamilies) ? families.slice(0, maxFamilies) : families;
    for (const familyId of limited) {
      const count = await ev(`(() => {
        const b=document.getElementById(${JSON.stringify(familyId)})?.closest('.fam-block');
        return b ? b.querySelectorAll('.tier-row').length : 0;
      })()`);
      if (count === 0) {
        // Expand only to count tiers, then continue.
        await ev(`document.getElementById(${JSON.stringify(familyId)})?.click()`);
        await wait(`document.getElementById(${JSON.stringify(familyId)})?.closest('.fam-block')?.querySelector('.tier-list')`, 5000);
      }
      const tierCount = await ev(`(() => {
        const b=document.getElementById(${JSON.stringify(familyId)})?.closest('.fam-block');
        return b ? b.querySelectorAll('.tier-row').length : 0;
      })()`);
      if (tierCount === 0) continue;
      const indexes = [...new Set([0, Math.floor((tierCount - 1) / 2), tierCount - 1])];
      for (const index of indexes) {
        for (const mode of ['exact', 'up']) {
          const result = await measure(slot, familyId, index, mode);
          if (result) results.push(result);
        }
      }
      // Collapse to keep the DOM bounded.
      await ev(`(() => {
        const b=document.getElementById(${JSON.stringify(familyId)})?.closest('.fam-block');
        const t=b?.querySelector('.fam-toggle');
        if(t?.getAttribute('aria-expanded')==='true') t.click();
      })()`);
      await sleep(20);
    }
  }

  const numbers = mode => results.filter(r => r.mode === mode).map(r => r.ms).sort((a, b) => a - b);
  const pct = (values, p) => values.length
    ? values[Math.min(values.length - 1, Math.max(0, Math.ceil(values.length * p) - 1))]
    : null;
  const summary = {};
  for (const mode of ['exact', 'up']) {
    const values = numbers(mode);
    summary[mode] = {
      count: values.length,
      p50: pct(values, 0.5),
      p95: pct(values, 0.95),
      max: values.length ? values[values.length - 1] : null,
    };
  }
  const slowest = [...results].sort((a, b) => b.ms - a.ms).slice(0, 25);
  const payload = {
    generatedAt: new Date().toISOString(),
    root,
    viewport: { width: 1600, height: 1100 },
    maxMs,
    summary,
    slowest,
    errors,
    results,
  };
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, JSON.stringify(payload, null, 2));
  console.log(JSON.stringify({ summary, slowest, errors: errors.length }, null, 2));
  const worst = Math.max(summary.exact.max ?? 0, summary.up.max ?? 0);
  if (errors.length > 0 || worst > maxMs) {
    console.error(`tier-matrix: worst ${worst} ms (limit ${maxMs}), errors ${errors.length}`);
    process.exitCode = 1;
  }
} finally {
  try { ws?.close(); } catch {}
  try { chrome.kill('SIGKILL'); } catch {}
  server.close();
  try { fs.rmSync(profile, { recursive: true, force: true }); } catch {}
}
