// Public read-only snapshot for https://jayhasty.com/SBU-IGIT-minipilot
// The lab computer POSTs the latest trend and calculations. This process stores
// that snapshot and serves the dashboard page.
const http = require("http");
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const PORT = Number(process.env.PORT || 8787);
const LIVE = process.env.LIVE_DIR || __dirname;
const SNAPSHOT_PATH = process.env.SNAPSHOT_PATH || path.join(LIVE, "snapshot.json");
const TOKEN = process.env.MINIPILOT_INGEST_TOKEN || "";
const MAX_BODY = 48 * 1024 * 1024;
const PREFIX = "/SBU-IGIT-minipilot";

const FILES = {
  "/": { file: "index.html", type: "text/html; charset=utf-8" },
  "/index.html": { file: "index.html", type: "text/html; charset=utf-8" },
  "/igit.png": { file: "igit.png", type: "image/png" },
  "/specview-stirred.png": { file: "specview-stirred.png", type: "image/png" }
};

let snapshot = {
  connected: false,
  receivedAt: null,
  trend: null,
  points: [],
  vars: null
};

function loadSnapshot() {
  try {
    const text = fs.readFileSync(SNAPSHOT_PATH, "utf8");
    const doc = JSON.parse(text);
    if (doc && typeof doc === "object") snapshot = doc;
  } catch (err) {
    if (err.code !== "ENOENT") console.error("snapshot read:", err.message);
  }
}

function saveSnapshot() {
  const dir = path.dirname(SNAPSHOT_PATH);
  fs.mkdirSync(dir, { recursive: true });
  const tmp = SNAPSHOT_PATH + ".tmp";
  fs.writeFileSync(tmp, JSON.stringify(snapshot));
  fs.renameSync(tmp, SNAPSHOT_PATH);
}

function sameToken(got) {
  if (!TOKEN || !got) return false;
  const a = Buffer.from(TOKEN);
  const b = Buffer.from(got);
  if (a.length !== b.length) return false;
  return crypto.timingSafeEqual(a, b);
}

function routeOf(urlPath) {
  const bare = urlPath.split("?")[0];
  if (bare === PREFIX) return "/";
  if (bare.startsWith(PREFIX + "/")) return bare.slice(PREFIX.length);
  return bare;
}

function send(res, status, body, type) {
  const buf = Buffer.isBuffer(body) ? body : Buffer.from(body);
  res.writeHead(status, {
    "Content-Type": type,
    "Content-Length": buf.length,
    "Cache-Control": "no-store",
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "Content-Type, Authorization",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS"
  });
  res.end(buf);
}

function sendJson(res, status, obj) {
  send(res, status, JSON.stringify(obj), "application/json; charset=utf-8");
}

function applyIngest(body) {
  const keepTrend = snapshot ? snapshot.trend : null;
  const keepPoints = snapshot && Array.isArray(snapshot.points) ? snapshot.points : [];
  const keepVars = snapshot ? snapshot.vars : null;
  const disconnect = body.connected === false && body.trend == null;
  snapshot = {
    connected: disconnect ? false : body.connected !== false,
    receivedAt: new Date().toISOString(),
    trend: body.trend != null ? body.trend : keepTrend,
    points: Array.isArray(body.points) ? body.points : keepPoints,
    vars: body.vars !== undefined ? body.vars : keepVars
  };
  saveSnapshot();
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > MAX_BODY) {
        reject(Object.assign(new Error("body too large"), { status: 413 }));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

loadSnapshot();

const server = http.createServer(async (req, res) => {
  try {
    if (req.method === "OPTIONS") {
      send(res, 204, "", "text/plain");
      return;
    }
    const route = routeOf(req.url || "/");
    if (req.method === "GET" && route === "/api/snapshot") {
      sendJson(res, 200, snapshot);
      return;
    }
    if (req.method === "POST" && route === "/api/ingest") {
      if (!TOKEN) {
        sendJson(res, 503, { error: "ingest token is not configured" });
        return;
      }
      const header = String(req.headers.authorization || "");
      const got = header.startsWith("Bearer ") ? header.slice(7).trim() : "";
      if (!sameToken(got)) {
        sendJson(res, 401, { error: "unauthorized" });
        return;
      }
      let body;
      try {
        body = JSON.parse(await readBody(req));
      } catch (err) {
        sendJson(res, err.status || 400, { error: "invalid json" });
        return;
      }
      if (!body || typeof body !== "object" || Array.isArray(body)) {
        sendJson(res, 400, { error: "expected an object" });
        return;
      }
      if (body.trend != null && (typeof body.trend !== "object" || Array.isArray(body.trend))) {
        sendJson(res, 400, { error: "trend must be an object" });
        return;
      }
      if (body.points != null && !Array.isArray(body.points)) {
        sendJson(res, 400, { error: "points must be an array" });
        return;
      }
      applyIngest(body);
      sendJson(res, 200, { ok: true, receivedAt: snapshot.receivedAt });
      return;
    }
    if (req.method === "GET" && FILES[route]) {
      const spec = FILES[route];
      const file = path.join(LIVE, spec.file);
      const bytes = fs.readFileSync(file);
      send(res, 200, bytes, spec.type);
      return;
    }
    sendJson(res, 404, { error: "not found" });
  } catch (err) {
    console.error(err);
    try { sendJson(res, 500, { error: "server error" }); } catch (e) { /* closed */ }
  }
});

server.listen(PORT, "0.0.0.0", () => {
  console.log("minipilot public http://0.0.0.0:" + PORT + PREFIX);
});
