#!/usr/bin/env node
// Browser acceptance check for the offline shell.
//
// Usage:
//   dotnet publish web/web.csproj -c Release -o /tmp/webpwa \
//     -p:BlazorEnableCompression=false
//   node tools/pwa-check/run.mjs --root /tmp/webpwa/wwwroot
//
// Serves the published site on a non-localhost origin, waits for the service
// worker, verifies the manifest/installability, and reloads with the network
// offline.

import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

const args = Object.fromEntries(process.argv.slice(2).map((a, i, all) =>
  a.startsWith('--') ? [a.slice(2), all[i + 1]?.startsWith('--') ? '1' : all[i + 1] ?? '1'] : null
).filter(Boolean));

const root = path.resolve(args.root ?? '/tmp/webpwa/wwwroot');
const port = Number(args.port ?? 8117);
const debugPort = Number(args.debugPort ?? 9240);
const host = args.host ?? 'pwa.test';
const url = `http://${host}:${port}/`;

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`published site not found: ${root}/index.html`);
  process.exit(2);
}

const mime = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
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

const server = http.createServer((req, res) => {
  const pathname = decodeURIComponent((req.url ?? '/').split('?')[0]);
  let file = path.join(root, pathname === '/' ? 'index.html' : pathname);
  if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
  if (!fs.existsSync(file) && !path.extname(file)) file = path.join(root, 'index.html');
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404).end(); return; }
  res.writeHead(200, {
    'Content-Type': mime[path.extname(file)] ?? 'application/octet-stream',
    'Cache-Control': 'no-store',
  });
  fs.createReadStream(file).pipe(res);
});

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
let chrome;
let ws;
let messageId = 0;
const pending = new Map();

function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++messageId;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

async function evaluate(expression, awaitPromise = true) {
  const result = await send('Runtime.evaluate', {
    expression, awaitPromise, returnByValue: true, userGesture: true,
  });
  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
  }
  return result.result?.value;
}

async function waitFor(expression, timeoutMs = 60000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try { if (await evaluate(expression)) return; } catch { /* navigations */ }
    await sleep(100);
  }
  throw new Error(`timeout waiting for: ${expression}`);
}

async function waitForHttp(target) {
  for (let i = 0; i < 300; i++) {
    try { const response = await fetch(target); if (response.ok) return; } catch { /* not ready */ }
    await sleep(100);
  }
  throw new Error(`timeout waiting for ${target}`);
}

try {
  await new Promise((resolve) => server.listen(port, '127.0.0.1', resolve));
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'pwa-check-'));
  chrome = spawn('chromium', [
    '--headless=new', '--no-sandbox', '--disable-gpu',
    '--window-size=1280,900',
    `--remote-debugging-port=${debugPort}`,
    `--user-data-dir=${profile}`,
    `--host-resolver-rules=MAP ${host} 127.0.0.1`,
    `--unsafely-treat-insecure-origin-as-secure=${url}`,
    url,
  ], { stdio: 'ignore' });

  await waitForHttp(`http://127.0.0.1:${debugPort}/json/version`);
  const pages = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
  const page = pages.find((x) => x.type === 'page');
  ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  ws.onmessage = (event) => {
    const message = JSON.parse(typeof event.data === 'string' ? event.data : Buffer.from(event.data).toString());
    if (message.id && pending.has(message.id)) {
      const entry = pending.get(message.id);
      pending.delete(message.id);
      message.error ? entry.reject(new Error(message.error.message)) : entry.resolve(message.result);
    }
  };

  await send('Runtime.enable');
  await send('Page.enable');
  await send('Network.enable');
  await waitFor(`document.readyState === 'complete' && !!document.querySelector('#mod-search')`);
  if (await evaluate(`!!document.querySelector('#intro-dismiss')`)) {
    await evaluate(`document.querySelector('#intro-dismiss').click()`);
    await waitFor(`!document.querySelector('#intro-card')`);
  }

  const registration = await evaluate(`(async () => {
    const reg = await navigator.serviceWorker.ready;
    return {
      scope: reg.scope,
      active: reg.active?.scriptURL ?? null,
      controller: !!navigator.serviceWorker.controller,
    };
  })()`);
  const cache = await evaluate(`(async () => {
    const keys = await caches.keys();
    const name = keys.find((key) => key.startsWith('renlys-tools-'));
    if (!name) return { keys, entries: 0, hasIndex: false, hasFramework: false };
    const entries = (await (await caches.open(name)).keys()).map((request) => new URL(request.url).pathname);
    return {
      keys,
      entries: entries.length,
      hasIndex: entries.includes('/index.html'),
      hasFramework: entries.some((entry) => entry.startsWith('/_framework/')),
    };
  })()`);
  const manifest = await send('Page.getAppManifest');
  const installability = await send('Page.getInstallabilityErrors').catch((error) => ({ error: String(error) }));

  await send('Network.emulateNetworkConditions', {
    offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0,
  });
  await send('Page.reload', { ignoreCache: false });
  await waitFor(`document.readyState === 'complete' && !!document.querySelector('#mod-search')`);
  const offline = await evaluate(`({
    title: document.title,
    picker: !!document.querySelector('#mod-search'),
    online: navigator.onLine,
    controller: !!navigator.serviceWorker.controller,
  })`);

  const report = {
    registration,
    cache,
    manifest: { url: manifest.url, errors: manifest.errors },
    installability,
    offline,
  };
  console.log(JSON.stringify(report, null, 2));

  const ok = registration.active &&
    cache.hasIndex && cache.hasFramework &&
    !manifest.errors?.length &&
    !(installability.installabilityErrors?.length) &&
    offline.title === "Renly's Tools" && offline.picker && offline.online === false;
  process.exitCode = ok ? 0 : 1;
} finally {
  if (ws) ws.close();
  if (chrome) chrome.kill('SIGTERM');
  server.close();
}
