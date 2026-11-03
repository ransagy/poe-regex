// Branded PR/branch previews for renlys.tools.
//
//   https://preview-<branch>.renlys.tools
//     -> https://<branch>.renlys-recipes.pages.dev
//
// The branch label is the same one Cloudflare Pages uses for its branch
// alias: lowercased, with non-alphanumeric characters replaced by hyphens.
// www.renlys.tools permanently redirects to the apex.
const APEX = "renlys.tools";
const PAGES_PROJECT = "renlys-recipes";
const PREVIEW_PREFIX = "preview-";
const PREVIEW_KEY_HEADER = "X-Renlys-Preview-Key";

// In service-worker syntax, secret bindings are global variables. Keep the
// header optional so a missing secret fails closed in the Pages middleware.
function previewKey() {
  if (typeof PREVIEW_KEY === "string") return PREVIEW_KEY;
  return typeof globalThis.PREVIEW_KEY === "string" ? globalThis.PREVIEW_KEY : "";
}

addEventListener("fetch", (event) => {
  event.respondWith(handle(event.request));
});

async function handle(request) {
  const url = new URL(request.url);
  const host = url.hostname.toLowerCase();

  if (host === `www.${APEX}`) {
    return Response.redirect(`https://${APEX}${url.pathname}${url.search}`, 301);
  }

  if (request.method !== "GET" && request.method !== "HEAD") {
    return new Response("Method not allowed", {
      status: 405,
      headers: { Allow: "GET, HEAD" },
    });
  }

  const suffix = `.${APEX}`;
  if (!host.endsWith(suffix)) {
    return new Response("Not found", { status: 404 });
  }

  const label = host.slice(0, -suffix.length);
  if (!label.startsWith(PREVIEW_PREFIX)) {
    return new Response("Not found", { status: 404 });
  }

  const branch = label.slice(PREVIEW_PREFIX.length);
  if (!/^[a-z0-9-]+$/.test(branch)) {
    return new Response("Not found", { status: 404 });
  }

  const target = new URL(request.url);
  target.protocol = "https:";
  target.hostname = `${branch}.${PAGES_PROJECT}.pages.dev`;

  const headers = new Headers(request.headers);
  headers.delete("host");
  const key = previewKey();
  if (key) headers.set(PREVIEW_KEY_HEADER, key);

  const response = await fetch(target.toString(), {
    method: request.method,
    headers,
    redirect: "manual",
  });

  const out = new Response(response.body, response);
  out.headers.set("X-Robots-Tag", "noindex");
  out.headers.set("Cache-Control", "no-store");
  return out;
}
