// Host guard for the Pages project.
//
// The project has two public entry points:
//
//   https://renlys.tools        the real site
//   https://preview-<branch>.renlys.tools
//                                the branded preview router
//
// The generated Pages hosts are internal. Direct requests to the project root
// are redirected to the public domain; direct requests to branch/hash hosts
// return 404 unless they carry the preview router's secret header. This keeps
// the Pages hosts usable by the router without exposing them to visitors.
const APEX = "renlys.tools";
const PAGES_PROJECT = "renlys-recipes";
const PAGES_ROOT = `${PAGES_PROJECT}.pages.dev`;
const PAGES_SUFFIX = `.${PAGES_ROOT}`;
const PREVIEW_KEY_HEADER = "X-Renlys-Preview-Key";

export async function onRequest(context) {
  const url = new URL(context.request.url);
  const host = url.hostname.toLowerCase();

  // Let certificate validation reach the origin even on the project host.
  if (url.pathname.startsWith("/.well-known/acme-challenge/")) {
    return context.next();
  }

  if (host === PAGES_ROOT) {
    return Response.redirect(`https://${APEX}${url.pathname}${url.search}`, 301);
  }

  if (host.endsWith(PAGES_SUFFIX)) {
    const expected = context.env.PREVIEW_KEY;
    const provided = context.request.headers.get(PREVIEW_KEY_HEADER);
    if (!expected || provided !== expected) {
      return new Response("Not found", { status: 404 });
    }
  }

  return context.next();
}
