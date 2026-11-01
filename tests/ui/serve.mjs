// Static file server for the UI tests. Serves src/ReplayViewer/wwwroot unchanged;
// no bundler and no build step, matching how the app ships the files.
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

// Mirrors ListenerHost.ApplySecurityHeaders so the tests run under the same policy the
// app serves with; otherwise a CSP that breaks the UI would pass unnoticed.
const securityHeaders = {
  "x-content-type-options": "nosniff",
  "content-security-policy":
    "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
};

const root = resolve(fileURLToPath(new URL("../../src/ReplayViewer/wwwroot/", import.meta.url)));
const types = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".ico": "image/x-icon",
};

createServer(async (req, res) => {
  const pathname = decodeURIComponent(new URL(req.url ?? "/", "http://localhost").pathname);
  const file = resolve(root, "." + (pathname === "/" ? "/index.html" : pathname));
  if (!file.startsWith(root + sep)) {
    res.writeHead(403).end();
    return;
  }
  try {
    const body = await readFile(file);
    res.writeHead(200, {
      "content-type": types[extname(file)] ?? "application/octet-stream",
      ...securityHeaders,
    });
    res.end(body);
  } catch {
    res.writeHead(404, securityHeaders).end();
  }
}).listen(Number(process.env.PORT ?? 4321), "127.0.0.1");
