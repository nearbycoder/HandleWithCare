// Checks the browser build on phones and tablets (and that desktops never see the touch controls), headless.
//
//   node Tools/check-mobile.mjs --serve Builds/Pages --device iphone            iPhone 15 in WebKit, landscape
//   node Tools/check-mobile.mjs --serve Builds/Pages --device ipad --play       iPad Pro 11, and a touch session
//   node Tools/check-mobile.mjs --serve Builds/Pages --device pixel --play      Pixel 7 in Chromium
//   node Tools/check-mobile.mjs --serve Builds/Pages --device desktop           desktop Chromium: no touch controls
//   node Tools/check-mobile.mjs --serve Builds/Pages --device firefox           desktop Firefox: no touch controls
//
// Devices: iphone, iphone-portrait, ipad, ipad-portrait, pixel, pixel-portrait (Playwright's profiles, landscape
// unless -portrait), desktop, firefox. Measures the load (time, bytes), the peak memory while it runs (the wasm
// heap, the JavaScript heap where the browser reports it, every WebGL texture, buffer and renderbuffer the game
// allocates, by format, and the browser processes' resident memory), and the frame rate on the title and in
// play. --play then drives a short session with real touch events through the on-screen controls: the title,
// START SHIFT, items dragged from the shelf into the box, ROTATE, a padding tool, ASK MABEL, pause and resume,
// SEAL & SHIP, the journey's controls and the review. --session also measures memory over a longer run
// (several deliveries).
//
// WebKit and Chromium here run on a desktop GPU, which offers the texture formats phones lack (S3TC/DXT, BPTC,
// RGTC). Unless --desktop-textures is given, the phone and tablet profiles hide those, as iOS and Android do, so
// the page picks the textures a phone would get. Nothing here enforces iOS's per-tab memory limit: compare the peaks.
// WebKit's sound needs GStreamer's autoaudiosink, which this machine lacks: its web process aborts a few seconds
// after the game starts its audio. So WebKit runs without WebAudio (the game carries on silent) unless
// --webkit-audio is given; the sound-after-the-first-tap check runs in Chromium.
//
// --serve copies the folder to Logs/pages-root/HandleWithCare and serves Logs/pages-root with python3 on a free
// port, stopped by its PID at the end. Logs, screenshots and summary.json go to Logs/mobile-check/<device>.
// Needs playwright-core (PLAYWRIGHT_CORE, else ~/Sites/blog's 1.63, else the newest under ~/Sites/*/node_modules); WebKit from
// WEBKIT_PATH (default ~/.cache/webkit-libs/webkit-2359/pw_run.sh), Chromium from ~/.cache/ms-playwright.
import { createRequire } from "node:module";
import { spawn } from "node:child_process";
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";

const require = createRequire(import.meta.url);
const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), "..");

// ---- arguments -------------------------------------------------------------------------------------------
const args = process.argv.slice(2);
const opt = { device: "iphone", play: false, session: false, serve: null, out: null, timeout: 300, url: null, desktopTextures: false, webkitAudio: false };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--device") opt.device = args[++i];
  else if (a === "--play") opt.play = true;
  else if (a === "--session") { opt.play = true; opt.session = true; }
  else if (a === "--serve") opt.serve = args[++i];
  else if (a === "--out") opt.out = args[++i];
  else if (a === "--timeout") opt.timeout = Number(args[++i]);
  else if (a === "--desktop-textures") opt.desktopTextures = true;
  else if (a === "--webkit-audio") opt.webkitAudio = true;
  else if (a === "-h" || a === "--help") { console.log(readFileSync(new URL(import.meta.url), "utf8").split("\nimport")[0]); process.exit(0); }
  else if (!a.startsWith("--")) opt.url = a;
  else { console.error("unknown option " + a); process.exit(2); }
}
const DEVICES = {
  "iphone": { browser: "webkit", profile: "iPhone 15 landscape" },
  "iphone-portrait": { browser: "webkit", profile: "iPhone 15" },
  "ipad": { browser: "webkit", profile: "iPad Pro 11 landscape" },
  "ipad-portrait": { browser: "webkit", profile: "iPad Pro 11" },
  "pixel": { browser: "chromium", profile: "Pixel 7 landscape" },
  "pixel-portrait": { browser: "chromium", profile: "Pixel 7" },
  "desktop": { browser: "chromium", profile: null },
  "firefox": { browser: "firefox", profile: null },
  "webkit-desktop": { browser: "webkit", profile: null },   // for comparison: WebKit with a mouse at 1600x900
};
const dev = DEVICES[opt.device];
if (!dev) { console.error("--device " + Object.keys(DEVICES).join(" | ")); process.exit(2); }
if (!opt.url && !opt.serve) { console.error("usage: node Tools/check-mobile.mjs <url> | --serve <dir> [--device ...] [--play]"); process.exit(2); }
const out = path.resolve(opt.out || path.join(root, "Logs", "mobile-check", opt.device));
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

function findPlaywright() {
  if (process.env.PLAYWRIGHT_CORE) return process.env.PLAYWRIGHT_CORE;
  const blog = path.join(os.homedir(), "Sites/blog/node_modules/playwright-core");   // 1.63, whose WebKit is webkit-2359
  if (existsSync(blog)) return blog;
  try { return path.dirname(require.resolve("playwright-core/package.json")); } catch (e) { /* not installed here */ }
  const sites = path.join(os.homedir(), "Sites");
  let best = null, bestVer = [0, 0, 0];
  for (const d of existsSync(sites) ? readdirSync(sites) : []) {
    const p = path.join(sites, d, "node_modules", "playwright-core");
    try {
      const v = JSON.parse(readFileSync(path.join(p, "package.json"), "utf8")).version.split(".").map(Number);
      if (v[0] > bestVer[0] || (v[0] === bestVer[0] && (v[1] > bestVer[1] || (v[1] === bestVer[1] && v[2] > bestVer[2])))) { best = p; bestVer = v; }
    } catch (e) { /* none here */ }
  }
  if (!best) { console.error("playwright-core not found: set PLAYWRIGHT_CORE"); process.exit(2); }
  return best;
}
function cachedChromium() {
  const cache = path.join(os.homedir(), ".cache", "ms-playwright");
  const dirs = existsSync(cache) ? readdirSync(cache).filter((d) => d.startsWith("chromium_headless_shell-")) : [];
  dirs.sort((a, b) => Number(b.split("-")[1]) - Number(a.split("-")[1]));
  for (const d of dirs) {
    const exe = path.join(cache, d, "chrome-headless-shell-linux64", "chrome-headless-shell");
    if (existsSync(exe)) return exe;
  }
  return undefined;
}
const pwPath = findPlaywright();
const pw = require(pwPath);

// ---- results ---------------------------------------------------------------------------------------------
const lines = [];
function say(s) { console.log(s); lines.push(s); }
const results = [];
function check(ok, what) {
  results.push({ ok: !!ok, what });
  say(`[Check] ${ok ? "PASS" : "FAIL"} ${what}`);
  return ok;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const MB = (b) => (b / 1048576).toFixed(1) + " MB";
function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, "127.0.0.1", () => { const p = s.address().port; s.close(() => resolve(p)); });
  });
}

// The resident memory of a process and everything it started (the browser's content and GPU processes).
function processTree(pid) {
  const kids = new Map();
  for (const d of readdirSync("/proc")) {
    if (!/^\d+$/.test(d)) continue;
    try {
      const stat = readFileSync(`/proc/${d}/stat`, "utf8");
      const ppid = Number(stat.slice(stat.lastIndexOf(")") + 2).split(" ")[1]);
      if (!kids.has(ppid)) kids.set(ppid, []);
      kids.get(ppid).push(Number(d));
    } catch (e) { /* gone */ }
  }
  const all = [], stack = [pid];
  while (stack.length) { const p = stack.pop(); all.push(p); for (const k of kids.get(p) || []) stack.push(k); }
  const procs = [];
  for (const p of all) {
    try {
      const status = readFileSync(`/proc/${p}/status`, "utf8");
      const rss = Number((status.match(/VmRSS:\s+(\d+)/) || [0, 0])[1]) * 1024;
      const name = (status.match(/Name:\s+(\S+)/) || [0, "?"])[1];
      let pss = 0;
      try { pss = Number((readFileSync(`/proc/${p}/smaps_rollup`, "utf8").match(/Pss:\s+(\d+)/) || [0, 0])[1]) * 1024; } catch (e) { /* no access */ }
      procs.push({ pid: p, name, rss, pss });
    } catch (e) { /* gone */ }
  }
  return procs;
}

// ---- in the page -----------------------------------------------------------------------------------------
// Counts every WebGL allocation (textures by level, buffers, renderbuffers) and frames; optionally hides the
// desktop-only texture formats, as an iPhone's WebGL lacks them.
const pageProbe = ({ hideDesktopFormats, noAudio }) => {
  if (noAudio) { delete window.AudioContext; delete window.webkitAudioContext; }
  // every AudioContext is remembered, held suspended until a real tap, click or key (as on a phone; headless
  // browsers would let it start by itself), with an analyser before the speakers: is the game making sound?
  window.__hwcAudio = { contexts: [], analysers: [], activated: false };
  for (const ev of ["touchend", "pointerup", "mousedown", "click", "keydown"])
    window.addEventListener(ev, (e) => { if (e.isTrusted) window.__hwcAudio.activated = true; }, true);
  for (const name of ["AudioContext", "webkitAudioContext"]) {
    const Orig = window[name];
    if (!Orig) continue;
    window[name] = class extends Orig {
      constructor(...a) { super(...a); window.__hwcAudio.contexts.push(this); if (!window.__hwcAudio.activated) super.suspend(); }
      resume() { return window.__hwcAudio.activated ? super.resume() : Promise.resolve(); }
    };
  }
  if (window.AudioNode) {
    const connect = AudioNode.prototype.connect;
    AudioNode.prototype.connect = function (dest, ...rest) {
      const r = connect.call(this, dest, ...rest);
      try {
        if (dest instanceof AudioDestinationNode) {
          const ctx = dest.context;
          if (!ctx.__probe) {
            ctx.__probe = ctx.createAnalyser();
            const mute = ctx.createGain();
            mute.gain.value = 0;
            connect.call(ctx.__probe, mute);
            connect.call(mute, ctx.destination);
            window.__hwcAudio.analysers.push(ctx.__probe);
          }
          connect.call(this, ctx.__probe);
        }
      } catch (e) { /* not audio we can probe */ }
      return r;
    };
  }
  const P = window.__probe = { tex: new Map(), buf: new Map(), rb: new Map(), frames: 0, formats: {}, hidden: hideDesktopFormats, ext: null };
  const tick = () => { P.frames++; requestAnimationFrame(tick); };
  requestAnimationFrame(tick);
  const desktopOnly = /s3tc|bptc|rgtc/i;
  const G = WebGL2RenderingContext.prototype;
  if (hideDesktopFormats) {
    const ge = G.getExtension, gs = G.getSupportedExtensions;
    G.getExtension = function (n) { return desktopOnly.test(n) ? null : ge.call(this, n); };
    G.getSupportedExtensions = function () { return (gs.call(this) || []).filter((n) => !desktopOnly.test(n)); };
  }
  // bytes per pixel (compressed: per pixel of a 4x4 or larger block) by internal format
  const BPP = {
    0x8058: 4, 0x8C43: 4, 0x1908: 4, 0x8051: 3, 0x1907: 3, 0x881A: 8, 0x8814: 16, 0x8C3A: 4, 0x8C3D: 4, 0x822B: 2, 0x8229: 1,
    0x822D: 2, 0x822F: 4, 0x822E: 4, 0x8230: 8, 0x88F0: 4, 0x81A6: 3, 0x81A5: 2, 0x8CAC: 4, 0x8CAD: 8, 0x8D48: 1, 0x1909: 1, 0x190A: 2, 0x8056: 2, 0x8D62: 2, 0x8059: 4,
    0x83F0: 0.5, 0x83F1: 0.5, 0x83F2: 1, 0x83F3: 1, 0x8C4C: 0.5, 0x8C4D: 0.5, 0x8C4E: 1, 0x8C4F: 1, // S3TC (+sRGB)
    0x8E8C: 1, 0x8E8D: 1, 0x8E8E: 1, 0x8E8F: 1, 0x8DBB: 0.5, 0x8DBC: 0.5, 0x8DBD: 1, 0x8DBE: 1,    // BPTC, RGTC
    0x9274: 0.5, 0x9275: 0.5, 0x9278: 1, 0x9279: 1, 0x9270: 0.5, 0x9272: 1, 0x9276: 0.5, 0x9277: 0.5, // ETC2/EAC
    0x93B0: 1, 0x93D0: 1, 0x93B4: 0.445, 0x93D4: 0.445, 0x93B7: 0.25, 0x93D7: 0.25, 0x93B2: 0.64, 0x93D2: 0.64, // ASTC 4x4 6x6 8x8 5x5
  };
  const NAME = { 0x83F0: "DXT1", 0x83F1: "DXT1A", 0x83F3: "DXT5", 0x8C4C: "DXT1-sRGB", 0x8C4F: "DXT5-sRGB", 0x8E8C: "BC7", 0x8E8D: "BC7-sRGB",
    0x9274: "ETC2", 0x9275: "ETC2-sRGB", 0x9278: "ETC2A", 0x9279: "ETC2A-sRGB", 0x93B0: "ASTC4", 0x93D0: "ASTC4-sRGB", 0x93B4: "ASTC6", 0x93D4: "ASTC6-sRGB",
    0x93B7: "ASTC8", 0x93D7: "ASTC8-sRGB", 0x8058: "RGBA8", 0x8C43: "SRGB8A8", 0x1908: "RGBA", 0x881A: "RGBA16F", 0x8C3A: "R11G11B10F", 0x88F0: "D24S8",
    0x8CAD: "D32FS8", 0x8CAC: "D32F", 0x81A6: "D24", 0x81A5: "D16", 0x8229: "R8", 0x822B: "RG8", 0x822D: "R16F", 0x822E: "R32F", 0x822F: "RG16F", 0x8814: "RGBA32F" };
  const bound = { tex: new Map(), buf: new Map(), rb: null };
  const texKey = (gl, target) => {
    const T = gl.TEXTURE_2D, unit = target === T ? "2d" : target === gl.TEXTURE_CUBE_MAP || (target >= 0x8515 && target <= 0x851A) ? "cube" : target === gl.TEXTURE_3D ? "3d" : "2da";
    return bound.tex.get(gl.__unit + ":" + unit);
  };
  const add = (map, obj, key, bytes, fmt, w, h) => {
    if (!obj) return;
    let e = map.get(obj);
    if (!e) { e = { parts: new Map(), fmt }; map.set(obj, e); }
    e.parts.set(key, bytes);
    if (fmt) e.fmt = fmt;
    if (w && (!e.w || w * h > e.w * e.h)) { e.w = w; e.h = h; }
  };
  const mips = (w, h, levels) => { let s = 0; for (let i = 0; i < levels; i++) { s += Math.max(1, w >> i) * Math.max(1, h >> i); } return s; };
  const wrap = (name, fn) => { const o = G[name]; G[name] = function (...a) { try { fn.call(this, ...a); } catch (e) { /* counting only */ } return o.apply(this, a); }; };
  wrap("activeTexture", function (u) { this.__unit = u; });
  wrap("bindTexture", function (t, tex) { const unit = t === this.TEXTURE_2D ? "2d" : t === this.TEXTURE_CUBE_MAP ? "cube" : t === this.TEXTURE_3D ? "3d" : "2da"; bound.tex.set(this.__unit + ":" + unit, tex); });
  wrap("bindBuffer", function (t, b) { bound.buf.set(t, b); });
  wrap("bindRenderbuffer", function (t, r) { bound.rb = r; });
  wrap("texStorage2D", function (t, levels, fmt, w, h) { add(P.tex, texKey(this, t), "s", mips(w, h, levels) * (BPP[fmt] || 4) * (t === this.TEXTURE_CUBE_MAP ? 6 : 1), fmt, w, h); });
  wrap("texStorage3D", function (t, levels, fmt, w, h, d) { add(P.tex, texKey(this, t), "s", mips(w, h, levels) * d * (BPP[fmt] || 4), fmt); });
  wrap("texImage2D", function (t, level, fmt, w, h) { if (typeof w !== "number") return; add(P.tex, texKey(this, t), t + ":" + level, w * h * (BPP[fmt] || 4), fmt, w, h); });
  wrap("texImage3D", function (t, level, fmt, w, h, d) { add(P.tex, texKey(this, t), t + ":" + level, w * h * d * (BPP[fmt] || 4), fmt); });
  wrap("compressedTexImage2D", function (t, level, fmt, w, h) { add(P.tex, texKey(this, t), t + ":" + level, Math.max(w, 4) * Math.max(h, 4) * (BPP[fmt] || 1), fmt, w, h); });
  wrap("bufferData", function (t, data, usage, offset, length) {
    const b = bound.buf.get(t);
    const n = typeof data === "number" ? data : !data ? 0 : length ? length * (data.BYTES_PER_ELEMENT || 1) : offset ? data.byteLength - offset * (data.BYTES_PER_ELEMENT || 1) : data.byteLength;
    add(P.buf, b, "d", n);
  });
  // the wasm heap: the memory the game's module exports
  const keep = (r) => { try { const inst = r.instance || r; for (const v of Object.values(inst.exports)) if (v instanceof WebAssembly.Memory) P.memory = v; } catch (e) { /* not a module */ } return r; };
  const wi = WebAssembly.instantiate, wis = WebAssembly.instantiateStreaming;
  WebAssembly.instantiate = function (...a) { return wi.apply(this, a).then(keep); };
  if (wis) WebAssembly.instantiateStreaming = function (...a) { return wis.apply(this, a).then(keep); };
  wrap("renderbufferStorage", function (t, fmt, w, h) { add(P.rb, bound.rb, "s", w * h * (BPP[fmt] || 4), fmt); });
  wrap("renderbufferStorageMultisample", function (t, s, fmt, w, h) { add(P.rb, bound.rb, "s", w * h * (BPP[fmt] || 4) * Math.max(1, s), fmt); });
  wrap("deleteTexture", function (o) { P.tex.delete(o); });
  wrap("deleteBuffer", function (o) { P.buf.delete(o); });
  wrap("deleteRenderbuffer", function (o) { P.rb.delete(o); });
  P.sum = () => {
    const total = (m) => { let s = 0; for (const e of m.values()) for (const b of e.parts.values()) s += b; return s; };
    const byFmt = {};
    for (const e of P.tex.values()) { let s = 0; for (const b of e.parts.values()) s += b; const n = NAME[e.fmt] || ("0x" + (e.fmt || 0).toString(16)); byFmt[n] = (byFmt[n] || 0) + s; }
    const heap = P.memory ? P.memory.buffer.byteLength : 0;
    const jsHeap = performance.memory ? performance.memory.usedJSHeapSize : 0;
    const biggest = [...P.tex.values()].map((e) => { let b = 0; for (const v of e.parts.values()) b += v; return { b, d: `${NAME[e.fmt] || "0x" + (e.fmt || 0).toString(16)} ${e.w}x${e.h}` }; })
      .sort((a, b) => b.b - a.b).slice(0, 12).map((x) => `${x.d} ${(x.b / 1048576).toFixed(1)} MB`);
    return { biggest, textures: total(P.tex), textureCount: P.tex.size, buffers: total(P.buf), renderbuffers: total(P.rb), byFmt, heap, jsHeap, frames: P.frames };
  };
};

// ---- the run ---------------------------------------------------------------------------------------------
async function main() {
  let server = null, url = opt.url, bserver = null, browser;
  if (opt.serve) {
    const site = path.resolve(opt.serve);
    if (!existsSync(path.join(site, "index.html"))) { console.error("no index.html in " + site); process.exit(2); }
    const serveRoot = path.join(root, "Logs", "pages-root-" + opt.device);
    rmSync(serveRoot, { recursive: true, force: true });
    cpSync(site, path.join(serveRoot, "HandleWithCare"), { recursive: true });
    const port = await freePort();
    server = spawn("python3", ["-m", "http.server", String(port), "--bind", "127.0.0.1", "-d", serveRoot], { stdio: "ignore" });
    say(`[Serve] python3 http.server pid ${server.pid} on port ${port}, root ${serveRoot}`);
    url = `http://127.0.0.1:${port}/HandleWithCare/`;
    await sleep(800);
  }
  const hide = !!dev.profile && !opt.desktopTextures;   // phones and tablets: no DXT, as on the real ones
  say(`[Run] ${opt.device} (${dev.profile || "desktop " + dev.browser}) ${opt.play ? "touch session" : "load check"} of ${url}${hide ? ", iOS texture formats (no S3TC/BPTC/RGTC)" : ""}${dev.browser === "webkit" && !opt.webkitAudio ? ", no WebAudio" : ""}`);
  say(`[Run] playwright-core ${pwPath}; load average ${os.loadavg().map((v) => v.toFixed(1)).join(" ")} on ${os.cpus().length} cores`);

  const launch = { headless: true };
  if (dev.browser === "webkit") launch.executablePath = process.env.WEBKIT_PATH || path.join(os.homedir(), ".cache/webkit-libs/webkit-2359/pw_run.sh");
  else if (dev.browser === "chromium") {
    launch.executablePath = process.env.CHROMIUM_PATH || cachedChromium();
    launch.args = ["--use-angle=vulkan", "--enable-features=Vulkan", "--ignore-gpu-blocklist", "--autoplay-policy=user-gesture-required"];
    launch.ignoreDefaultArgs = ["--autoplay-policy=no-user-gesture-required"];
  } else {
    launch.channel = "moz-firefox";
    launch.executablePath = process.env.FIREFOX_PATH || "/usr/bin/firefox";
    process.env.TMPDIR = path.join(root, "Logs", "tmp");
    mkdirSync(process.env.TMPDIR, { recursive: true });
    launch.firefoxUserPrefs = { "webgl.force-enabled": true };
  }

  const log = [], errors = [];
  const bytes = { total: 0, files: {} };
  const peak = { byProc: {}, rss: 0, rssProc: "", pss: 0, heap: 0, jsHeap: 0, textures: 0, buffers: 0, renderbuffers: 0, gpu: 0, byFmt: {}, textureCount: 0 };
  let sampler = null;
  const samples = [];
  let blank = null;
  try {
    // a server, so the browser's process tree can be measured
    bserver = await pw[dev.browser].launchServer(launch);
    const bpid = bserver.process().pid;
    say(`[Run] ${dev.browser} pid ${bpid}`);
    browser = await pw[dev.browser].connect(bserver.wsEndpoint());
    const ctxOpt = dev.profile ? { ...pw.devices[dev.profile] } : { viewport: { width: 1600, height: 900 } };
    delete ctxOpt.defaultBrowserType;
    if (dev.browser === "firefox") delete ctxOpt.isMobile;
    const context = await browser.newContext({ ...ctxOpt, acceptDownloads: true });
    await context.addInitScript(pageProbe, { hideDesktopFormats: hide, noAudio: dev.browser === "webkit" && !opt.webkitAudio });
    const page = await context.newPage();
    const t0 = Date.now();
    page.on("console", (m) => {
      const line = `[${((Date.now() - t0) / 1000).toFixed(1)}s ${m.type()}] ${m.text().trimEnd()}`;
      log.push(line);
      if (m.type() === "error") errors.push(line);
    });
    page.on("pageerror", (e) => { const l = "[pageerror] " + e; log.push(l); errors.push(l); });
    page.on("crash", () => { const l = "[crash] the page crashed"; log.push(l); errors.push(l); say(`[Memory] at the crash: ${samples.slice(-3).join(" | ")}`); });
    page.on("requestfailed", (r) => {
      const why = r.failure()?.errorText || "";
      const l = `[requestfailed] ${r.url()} ${why}`;
      log.push(l);
      if (!/ABORTED|NS_BINDING_ABORTED|cancel/i.test(why)) errors.push(l);
    });
    page.on("response", (r) => {
      if (r.status() >= 400) { const l = `[http ${r.status()}] ${r.url()}`; log.push(l); errors.push(l); }
      const len = Number(r.headers()["content-length"] || 0);
      if (len > 0) bytes.files[r.url().split("/").pop()] = len;
    });
    const sample = async () => {
      const procs = processTree(bpid);
      for (const p of procs) {
        if (p.rss > peak.rss) { peak.rss = p.rss; peak.rssProc = p.name; }
        // Chromium's processes by role (its renderer is the page's, its GPU process the graphics'), others by name
        let role = p.name;
        try { const cmd = readFileSync(`/proc/${p.pid}/cmdline`, "utf8"); const m = cmd.match(/--type=([a-z-]+)/); if (m) role = m[1] + (/--utility-sub-type=([\w.]+)/.exec(cmd)?.[1] ? ":" + /--utility-sub-type=([\w.]+)/.exec(cmd)[1].split(".").pop() : ""); else if (/chrom/.test(p.name)) role = "browser"; } catch (e) { /* gone */ }
        peak.byProc[role] = Math.max(peak.byProc[role] || 0, p.rss);
      }
      const pss = procs.reduce((a, p) => a + p.pss, 0);
      peak.pss = Math.max(peak.pss, pss);
      const s = await page.evaluate(() => window.__probe && window.__probe.sum()).catch(() => null);
      const big = procs.slice().sort((a, b) => b.rss - a.rss)[0];
      samples.push(`${((Date.now() - t0) / 1000).toFixed(1)}s ${big ? `${big.name} ${MB(big.rss)} resident` : "no process"}` +
        (s ? `; wasm heap ${MB(s.heap)}${s.jsHeap ? ", JS heap " + MB(s.jsHeap) : ""}, WebGL textures ${MB(s.textures)} (${s.textureCount}), buffers ${MB(s.buffers)}, renderbuffers ${MB(s.renderbuffers)}` : "; page gone"));
      if (s) {
        peak.heap = Math.max(peak.heap, s.heap);
        peak.jsHeap = Math.max(peak.jsHeap, s.jsHeap);
        const gpu = s.textures + s.buffers + s.renderbuffers;
        if (gpu > peak.gpu) { peak.biggest = s.biggest; peak.gpu = gpu; peak.textures = s.textures; peak.buffers = s.buffers; peak.renderbuffers = s.renderbuffers; peak.byFmt = s.byFmt; peak.textureCount = s.textureCount; }
      }
      return s;
    };
    sampler = setInterval(() => { sample().catch(() => {}); }, 500);
    const shot = (name) => process.env.NO_SHOT ? Promise.resolve() : page.screenshot({ path: path.join(out, name + ".png") }).catch((e) => say(`[Shot] ${name} failed: ${e.message}`));
    const state = () => page.evaluate(() => window.hwcState || null).catch(() => null);
    const waitPhase = async (seconds) => {
      const until = Date.now() + seconds * 1000;
      let s = null;
      while (Date.now() < until) {
        s = await state();
        if (s && (s.phase === "ready" || s.phase === "error" || s.phase === "rotate")) { if (s.phase !== "rotate") return s; }
        await sleep(250);
      }
      return s;
    };
    const waitLog = async (re, seconds, from = 0) => {
      const until = Date.now() + seconds * 1000;
      while (Date.now() < until) {
        for (let i = from; i < log.length; i++) if (re.test(log[i])) return log[i];
        await sleep(200);
      }
      return null;
    };
    const fps = async (seconds) => {
      const a = await page.evaluate(() => window.__probe.frames);
      await sleep(seconds * 1000);
      const b = await page.evaluate(() => window.__probe.frames);
      return (b - a) / seconds;
    };

    // what the browser holds before the game: an empty page with a WebGL 2 context
    await page.setContent("<canvas id=c></canvas><script>document.getElementById('c').getContext('webgl2')</script>");
    await sleep(2500);
    blank = processTree(bpid).sort((a, b) => b.rss - a.rss)[0];
    say(`[Memory] an empty page with WebGL: largest process ${blank ? `${blank.name} ${MB(blank.rss)}` : "?"} resident`);
    // -- load to the title
    await page.goto(url, { waitUntil: "load", timeout: 60000 });
    const env = await page.evaluate(() => ({
      coarse: matchMedia("(pointer: coarse)").matches, fine: matchMedia("(any-pointer: fine)").matches, hover: matchMedia("(hover: hover)").matches,
      touchPoints: navigator.maxTouchPoints, dpr: devicePixelRatio, w: innerWidth, h: innerHeight,
      webgl2: !!document.createElement("canvas").getContext("webgl2"), webgpu: !!navigator.gpu,
    }));
    say(`[Env] ${JSON.stringify(env)}`);
    const s = await waitPhase(opt.timeout);
    const loadSeconds = (Date.now() - t0) / 1000;
    bytes.total = Object.values(bytes.files).reduce((a, b) => a + b, 0);
    await sleep(2000);
    await shot("01-title");
    const ready = log.find((l) => l.includes("[Web] ready"));
    check(s && s.phase === "ready" && ready, `reaches the title screen (${s ? s.phase : "no page state"}${s && s.error ? ": " + s.error : ""}) in ${loadSeconds.toFixed(1)} s, ${MB(bytes.total)} downloaded`);
    if (ready) say("[Run] " + ready.replace(/^.*\[Web\] /, ""));
    const ext = await page.evaluate(() => { const gl = document.createElement("canvas").getContext("webgl2"); return gl ? gl.getSupportedExtensions().filter((n) => /compressed/.test(n)) : []; });
    say(`[Env] compressed texture extensions the page sees: ${ext.join(", ") || "none"}`);
    const fpsTitle = s && s.phase === "ready" ? await fps(5) : 0;
    say(`[Perf] title: ${fpsTitle.toFixed(1)} frames/s`);
    if (s && s.phase === "ready") {
      // the engine's own memory counters (WebPlatform.LogMemory), a few seconds in
      const m0 = log.length;
      await page.evaluate(() => window.unityInstance.SendMessage("TouchBridge", "Command", "memory:on the title")).catch(() => {});
      const mem = await waitLog(/\[Web\] memory on the title/, 5, m0);
      if (mem) say("[Memory] engine " + mem.replace(/^.*\[Web\] memory /, ""));
    }
    const touchUi = await page.evaluate(() => {
      const el = document.querySelector("#touch-ui");
      return { exists: !!el, shown: !!el && getComputedStyle(el).display !== "none", mode: document.documentElement.dataset.input || null };
    });
    say(`[Touch] controls: ${JSON.stringify(touchUi)}`);
    if (!dev.profile) check(!touchUi.shown, `no touch controls on desktop ${dev.browser} (${JSON.stringify(touchUi)})`);

    let fpsPlay = null;
    if (opt.play && s && s.phase === "ready" && dev.profile) fpsPlay = await play({ page, context, log, shot, waitLog, fps, env });
    if (opt.play && s && s.phase === "ready" && !dev.profile) await desktopInputs({ page, shot });

    await sample();
    clearInterval(sampler); sampler = null;
    say(`[Memory] peak: wasm heap ${MB(peak.heap)}${peak.jsHeap ? ", JS heap " + MB(peak.jsHeap) : ""}; WebGL ${MB(peak.gpu)} (textures ${MB(peak.textures)} in ${peak.textureCount}, buffers ${MB(peak.buffers)}, renderbuffers ${MB(peak.renderbuffers)})`);
    say(`[Memory] textures by format at the peak: ${Object.entries(peak.byFmt).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k} ${MB(v)}`).join(", ")}`);
    say(`[Memory] largest textures at the peak: ${(peak.biggest || []).join(", ")}`);
    say(`[Memory] browser processes: largest resident ${MB(peak.rss)} (${peak.rssProc}), all together ${MB(peak.pss)} proportional; ` +
        `peak by process: ${Object.entries(peak.byProc).filter(([, v]) => v > 50 * 1048576).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k} ${MB(v)}`).join(", ")}`);
    if (dev.browser === "webkit") say("[Memory] (WebKit here runs WebGL and its shader compiler inside the web process, so its resident size includes what an iPhone does in its GPU process)");
    // without WebAudio (WebKit here) the engine says it can't load its sounds: expected, not a failure
    const real = errors.filter((e) => !(dev.browser === "webkit" && !opt.webkitAudio && /Loading FSB failed/.test(e)));
    check(real.length === 0, `no console errors, page errors, crashes or failed requests (${real.length}${real.length < errors.length ? `; ${errors.length - real.length} sound-loading errors from running without WebAudio` : ""})`);
    for (const e of real.slice(0, 20)) say("  " + e);
    writeFileSync(path.join(out, "summary.json"), JSON.stringify({ device: opt.device, profile: dev.profile, browser: dev.browser, url, env, blank, loadSeconds, bytes, peak, fpsTitle, fpsPlay, touchUi, results, loadavg: os.loadavg() }, null, 2));
  } catch (e) {
    check(false, "the check ran: " + (e && e.stack ? e.stack : e));
  } finally {
    if (sampler) clearInterval(sampler);
    writeFileSync(path.join(out, "console.log"), log.join("\n") + "\n");
    writeFileSync(path.join(out, "memory.log"), samples.join("\n") + "\n");
    if (samples.length) say(`[Memory] last sample: ${samples[samples.length - 1]} (every sample in memory.log)`);
    if (browser) await browser.close().catch(() => {});
    if (bserver) await bserver.close().catch(() => {});
    if (server) { server.kill("SIGTERM"); say(`[Serve] stopped pid ${server.pid}`); }
    const failed = results.filter((r) => !r.ok).length;
    say(`[Done] ${results.length - failed}/${results.length} passed; log ${path.join(out, "console.log")}`);
    writeFileSync(path.join(out, "check.log"), lines.join("\n") + "\n");
    process.exit(failed === 0 && results.length > 0 ? 0 : 1);
  }
}

// ---- the touch session -------------------------------------------------------------------------------------
// Taps are Playwright's (real, trusted touches in both browsers). Drags and long presses, which Playwright can't
// make, are Chromium's own touch events (DevTools Input.dispatchTouchEvent) or, in WebKit, TouchEvents sent to
// the canvas (untrusted, but the engine reads them the same). The game reports where its buttons, the shelf items and the box cells are (TouchBridge "report"),
// so every touch lands on a real place, never a guessed pixel.
async function play({ page, context, log, shot, waitLog, fps, env }) {
  const cdp = dev.browser === "chromium" ? await context.newCDPSession(page) : null;
  const game = () => page.evaluate(() => window.hwcState.game || {}).catch(() => ({}));
  const waitGame = async (pred, seconds, what) => {
    const until = Date.now() + seconds * 1000;
    let g = {};
    while (Date.now() < until) { g = await game(); if (pred(g)) return g; await sleep(150); }
    say(`[Wait] gave up waiting for ${what}: ${JSON.stringify(g)}`);
    return null;
  };
  const send = (cmd) => page.evaluate((c) => window.unityInstance.SendMessage("TouchBridge", "Command", c), cmd);
  const report = async () => {
    const mark = log.length;
    await send("report");
    const line = await waitLog(/\[Web\] buttons:/, 10, mark);
    const at = {};
    if (line) for (const m of line.matchAll(/ (\S+) (-?\d+),(-?\d+);/g)) at[m[1]] = [Number(m[2]), Number(m[3])];
    return at;
  };
  const tap = async (x, y) => { await page.touchscreen.tap(x, y); await sleep(350); };
  const tapEl = async (sel) => {
    const box = await page.locator(sel).first().boundingBox().catch(() => null);
    if (!box) { say(`[Tap] ${sel} is not on screen`); return false; }
    await tap(box.x + box.width / 2, box.y + box.height / 2);
    return true;
  };
  const shown = (sel) => page.evaluate((s) => { const e = document.querySelector(s); if (!e) return false; const r = e.getBoundingClientRect(); return getComputedStyle(e).display !== "none" && r.width > 0; }, sel);
  // one finger down, moved in steps, held and lifted
  const touch = async (points, holdMs = 0) => {
    const step = dev.browser === "webkit" ? 90 : 40;
    if (cdp) {
      const tp = (p) => [{ x: p[0], y: p[1], id: 7, radiusX: 8, radiusY: 8, force: 1 }];
      await cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: tp(points[0]) });
      await sleep(holdMs || step * 2);
      for (const p of points.slice(1)) { await cdp.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: tp(p) }); await sleep(step); }
      await sleep(step * 2);
      await cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
    } else {
      const fire = (type, p) => page.evaluate(([type, x, y]) => {
        // WebKit has no Touch constructor; its own createTouch and TouchLists do the same
        const c = document.querySelector("#unity-canvas");
        const t = document.createTouch(window, c, 7, x, y, x, y);
        const one = document.createTouchList(t), none = document.createTouchList();
        const live = type === "touchend" ? none : one;
        c.dispatchEvent(new TouchEvent(type, { touches: live, targetTouches: live, changedTouches: one, bubbles: true, cancelable: true }));
      }, [type, p[0], p[1]]);
      await fire("touchstart", points[0]);
      await sleep(holdMs || step * 2);
      for (const p of points.slice(1)) { await fire("touchmove", p); await sleep(step); }
      await sleep(step * 2);
      await fire("touchend", points[points.length - 1]);
    }
    await sleep(400);
  };
  const stroke = (a, b, n = 10) => Array.from({ length: n + 1 }, (_, i) => [a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n]);
  const LIFT = 70;   // TouchInput.Lift: a carried item rides this many CSS pixels above the finger

  // -- the title, by touch
  const ui0 = await page.evaluate(() => {
    const gl = document.createElement("canvas").getContext("webgl2");
    return { input: document.documentElement.dataset.input, variant: window.hwcState.variant, shown: getComputedStyle(document.querySelector("#touch-ui")).display,
             etc: !!(gl && gl.getExtension("WEBGL_compressed_texture_etc")) };
  });
  // phones decode ETC2 (iOS and Android both offer it); this machine's Chromium offers no ETC once DXT is hidden,
  // so there the page rightly falls back to the DXT set
  const wantSet = ui0.etc ? "etc2" : "dxt";
  check(ui0.input === "touch" && ui0.variant === wantSet,
        `a touch-first ${dev.profile} gets the touch controls and the ${wantSet === "etc2" ? "ETC2" : "DXT (no ETC2 in this browser's GPU here)"} textures (input ${ui0.input}, textures ${ui0.variant}, controls layer ${ui0.shown})`);
  let title = await report();
  const aBefore = await page.evaluate(() => (window.__hwcAudio ? window.__hwcAudio.contexts.map((c) => c.state) : []));
  await tap(env.w * 0.75, env.h * 0.15);   // an empty part of the title screen: sound starts with the first tap
  let aAfter = null;
  for (let i = 0; i < 20; i++) {
    await sleep(500);
    aAfter = await page.evaluate(() => {
      const a = window.__hwcAudio; let peak = 0;
      for (const an of a ? a.analysers : []) { const b = new Float32Array(an.fftSize); an.getFloatTimeDomainData(b); for (const v of b) peak = Math.max(peak, Math.abs(v)); }
      return { states: a ? a.contexts.map((c) => c.state) : [], peak };
    });
    if (aAfter.peak > 0.001 || dev.browser !== "chromium") break;
  }
  if (dev.browser === "chromium")
    check(!aBefore.includes("running") && aAfter.states.includes("running") && aAfter.peak > 0.001,
          `sound waits for the first tap, then plays (audio ${JSON.stringify(aBefore)} before, ${JSON.stringify(aAfter.states)} after, peak ${aAfter.peak.toFixed(4)})`);
  else say(`[Note] sound isn't checked in WebKit here (no audio sink on this machine; see the header)`);
  const start = title.START_SHIFT || title.CONTINUE;
  check(start, `the title reports its buttons (${Object.keys(title).join(", ")})`);
  if (!start) return null;
  // the settings by tap: open, then DONE
  if (title.SETTINGS) {
    await tap(...title.SETTINGS);
    await sleep(900);
    const st = await report();
    await shot("02-settings");
    const done = st.DONE;
    if (done) { await tap(...done); await sleep(900); }
    check(st.DONE && (await game()).menu, `SETTINGS opens and closes by tap (${Object.keys(st).length} buttons on it)`);
  }
  title = await report();
  await tap(...(title.START_SHIFT || title.CONTINUE));
  let g = await waitGame((x) => x.phase === "packing" && x.tool !== undefined && !x.card, 40, "the bench");
  check(g, `START SHIFT by tap opens the bench (${g ? `seal hint "${g.sealHint}"` : "no bench"})`);
  if (!g) return null;
  await sleep(1500);
  await shot("03-bench");
  const fpsPlay = await fps(5);
  say(`[Perf] the bench: ${fpsPlay.toFixed(1)} frames/s`);
  const onScreen = {};
  for (const id of ["#tc-pause", "#tc-left", "#tc-right", "#tc-hand"]) onScreen[id] = await shown(id);
  check(onScreen["#tc-pause"] && onScreen["#tc-left"] && onScreen["#tc-right"] && !onScreen["#tc-hand"], `the bench shows PAUSE, UNDO/REDO and SEAL & SHIP, not the in-hand buttons (${JSON.stringify(onScreen)})`);
  const sizes = await page.evaluate(() => [...document.querySelectorAll("#touch-ui .cluster.on .tb:not(.hide)")].map((b) => { const r = b.getBoundingClientRect(); return { t: b.textContent.trim() || b.getAttribute("aria-label"), w: Math.round(r.width), h: Math.round(r.height) }; }));
  check(sizes.length && sizes.every((b) => b.w >= 44 && b.h >= 44), `every touch button is at least 44 points (${sizes.map((b) => `${b.t} ${b.w}x${b.h}`).join(", ")})`);

  // -- Mabel's packing for delivery 1: where each item is on the shelf and where it goes
  const mark0 = log.length;
  await page.evaluate(() => window.unityInstance.SendMessage("Packing", "WebReportBench"));
  const benchLine = await waitLog(/\[Web\] bench/, 10, mark0);
  const moves = benchLine ? [...benchLine.matchAll(/(\w+) (-?\d+),(-?\d+) > (-?\d+),(-?\d+)( rotated)?;/g)]
    .map((m) => ({ kind: m[1], from: [m[2] * env.w / 1920, m[3] * env.h / 1080], to: [m[4] * env.w / 1920, m[5] * env.h / 1080], rotated: !!m[6] })) : [];
  check(moves.length > 0, `the bench reports its items (${benchLine ? benchLine.replace(/^.*\[Web\] /, "") : "none"})`);
  if (!moves.length) return fpsPlay;
  const mv = moves[0];

  // tap the item on the shelf: it is in hand, with TURN and BACK
  await tap(...mv.from);
  g = await waitGame((x) => x.tool === "item", 6, "an item in hand");
  await shot("04-in-hand");
  check(g && await shown("#tc-hand") && await shown("#turn-btn") && !(await shown("#tc-right")), `a tap on the ${mv.kind} picks it up and shows TURN and BACK (${g ? g.tool : "nothing in hand"})`);
  let mark = log.length;
  await tapEl("#turn-btn");
  check(await waitLog(/\[Web\] touch rotate/, 4, mark), "TURN by tap reaches the game ([Web] touch rotate)");
  if (g && g.turn) await tapEl("#turn-btn");   // and back, as Mabel packs it
  await tapEl("#back-btn");
  g = await waitGame((x) => x.tool === "none", 6, "the item back on the shelf");
  check(g, "BACK puts it back on the shelf");

  // long press on the shelf item: its card, nothing picked up
  await touch([mv.from], 900);
  await shot("05-long-press-card");
  g = await game();
  check(g.tool === "none", `a long press on the ${mv.kind} shows its card without picking it up (tool ${g.tool})`);

  // drag it from the shelf into the box: it rides above the finger, so the finger ends below the spot
  const fingerEnd = [mv.to[0], mv.to[1] + LIFT];
  await touch(stroke(mv.from, fingerEnd, 12));
  g = await waitGame((x) => x.tool === "none" && x.undo, 6, "the item in the box");
  await shot("06-dragged-in");
  check(g && g.undo, `dragging the ${mv.kind} from the shelf drops it in the box (${g ? `seal hint "${g.sealHint}", seal ${g.seal}` : "not placed"})`);
  // UNDO and REDO by tap
  await tapEl('[data-k="undo"]');
  g = await waitGame((x) => x.redo, 5, "undo");
  await tapEl('[data-k="redo"]');
  g = await waitGame((x) => x.undo && !x.redo, 5, "redo");
  check(g, "UNDO and REDO by tap take the drop back and put it back");

  // the other items (later deliveries), dragged the same way
  for (const m of moves.slice(1)) {
    await touch(stroke(m.from, [m.to[0], m.to[1] + LIFT], 12));
    await sleep(500);
  }

  // padding: tap PAPER on the game's toolbar, paint by dragging, ERASE, DONE
  let at = await report();
  if (at.PAPER) {
    await tap(...at.PAPER);
    g = await waitGame((x) => x.tool === "padding", 5, "paper in hand");
    const cells = Object.entries(at).filter(([k]) => k.startsWith("cell")).map(([k, v]) => ({ k, v }));
    const bottom = cells.filter((c) => /_0$/.test(c.k)).map((c) => c.v);
    if (g && bottom.length >= 2) {
      const undoBefore = (await game()).undo;
      await touch(stroke(bottom[0], bottom[bottom.length - 1], 8));
      await sleep(600);
      await shot("07-painted");
      await tapEl("#erase-btn");
      g = await waitGame((x) => x.erase, 4, "ERASE");
      check(g && g.erase, "ERASE by tap lights up");
      await touch([bottom[bottom.length - 1], bottom[bottom.length - 1]]);
      await tapEl("#erase-btn");
      await tapEl("#back-btn");
      g = await waitGame((x) => x.tool === "none", 5, "padding put down");
      check(g, `paper by tap on the toolbar, painted by a drag, rubbed out with ERASE, put down with DONE (undo ${undoBefore} → ${g && g.undo})`);
    }
  }

  // ASK MABEL
  g = await game();
  if (g.hint) {
    mark = log.length;
    await tapEl("#hint-btn");
    check(await waitLog(/\[Web\] touch hint/, 4, mark), "ASK MABEL by tap");
    await sleep(800);
    await shot("08-mabel");
  }

  // pause and resume
  await tapEl("#tc-pause .tb");
  g = await waitGame((x) => x.paused, 5, "the pause menu");
  await shot("09-paused");
  at = await report();
  if (at.RESUME) await tap(...at.RESUME);
  g = await waitGame((x) => !x.paused, 5, "resumed");
  check(g && at.RESUME, "PAUSE by tap opens the pause menu, RESUME by tap closes it");

  // a portrait turn asks for landscape; back again
  const vp = page.viewportSize();
  await page.setViewportSize({ width: vp.height, height: vp.width });
  await sleep(1200);
  const rot = await shown("#rotate");
  await shot("10-portrait");
  await page.setViewportSize(vp);
  await sleep(1200);
  check(rot && !(await shown("#rotate")), "in portrait the page asks to turn the device sideways, and the game is back in landscape");

  // a key press hides the touch controls; the next touch brings them back
  await page.keyboard.press("Shift");
  await sleep(500);
  const afterKey = await shown("#touch-ui");
  await tap(env.w * 0.5, env.h * 0.3);
  await sleep(500);
  check(!afterKey && await shown("#touch-ui"), `a key press hides the touch controls and a touch brings them back (after the key: ${afterKey ? "shown" : "hidden"})`);

  // seal and ship
  g = await waitGame((x) => x.seal, 6, "ready to seal");
  mark = log.length;
  await tapEl("#seal-btn");
  const shipped = await waitLog(/\[Web\] journey/, 30, mark);
  check(g && shipped, `SEAL & SHIP by tap (${shipped ? shipped.replace(/^.*\[Web\] /, "") : "not shipped"})`);
  if (!shipped) return fpsPlay;
  await sleep(2500);
  await shot("11-journey");
  say(`[Perf] the journey: ${(await fps(4)).toFixed(1)} frames/s`);
  check(await shown("#tc-skip"), "the journey shows SKIP");
  await tapEl("#tc-skip .tb");
  g = await waitGame((x) => x.phase === "reveal" || x.phase === "results", 30, "the unboxing");
  if (g && g.phase === "reveal") {
    await sleep(1500);
    await shot("12-unboxing");
    if (await shown("#tc-skip")) await tapEl("#tc-skip .tb");
  }
  const review = await waitLog(/\[Web\] review/, 90, mark);
  await sleep(2500);
  await shot("13-review");
  check(review, `the unboxing and the review (${review ? review.replace(/^.*\[Web\] /, "") : "no review"})`);
  if (!review) return fpsPlay;

  // the replay: scrub bar, NEXT TROUBLE, speed, camera, DONE
  at = await report();
  if (at.REPLAY) {
    await tap(...at.REPLAY);
    g = await waitGame((x) => x.phase === "journey" && x.replay, 20, "the replay");
    await sleep(1000);
    check(g && await shown("#tc-replay"), "REPLAY by tap shows the scrub bar and the replay buttons");
    // a first delivery's trip lasts seconds: stop it first, so the replay is still there for every button
    await tapEl("#play-btn");
    g = await waitGame((x) => x.stopped, 4, "the replay paused");
    check(g, "play/pause by tap stops the replay");
    const sb = await page.locator("#scrub .track").boundingBox();
    if (sb) {
      await tap(sb.x + sb.width * 0.6, sb.y + sb.height / 2);
      g = await waitGame((x) => Math.abs(x.t - 0.6) < 0.08, 5, "the scrub to 60%");
      check(g, `a tap on the scrub bar jumps there (t ${g ? g.t : "?"})`);
      // and dragged along it (the page's own control: Chromium's touch events, or a mouse-free pointer drag)
      const y = sb.y + sb.height / 2;
      if (cdp) {
        const tp = (x) => [{ x, y, id: 9, radiusX: 8, radiusY: 8, force: 1 }];
        await cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: tp(sb.x + sb.width * 0.2) });
        for (let i = 1; i <= 6; i++) { await cdp.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: tp(sb.x + sb.width * (0.2 + i * 0.05)) }); await sleep(60); }
        await cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
        g = await waitGame((x) => Math.abs(x.t - 0.5) < 0.08, 5, "the scrub dragged to 50%");
        check(g, `dragging along the scrub bar follows the finger (t ${g ? g.t : "?"})`);
      }
    }
    await tapEl("#cam-btn");
    await tapEl("#play-btn");   // (a speed starts it again: stop it after)
    await tapEl("#speed-btn");
    g = await game();
    await shot("14-replay");
    check(g.speed !== 1 && /CLOSE|WIDE/.test(g.cam), `speed and camera by tap (speed ${g.speed}, camera ${g.cam})`);
    if (g.phase === "journey" && await shown("#trouble-btn")) { mark = log.length; await tapEl("#trouble-btn"); check(await waitLog(/\[Web\] touch trouble/, 4, mark), "NEXT TROUBLE by tap"); }
    await tapEl('#tc-replay [data-cmd="skip"]');
    g = await waitGame((x) => x.phase === "results", 15, "back to the review");
    check(g, "DONE by tap goes back to the review");
  }

  // -- more deliveries (--session): NEXT, Mabel's packing by drags, seal, skip; memory over a longer run
  if (opt.session) {
    for (let n = 2; n <= 6; n++) {
      at = await report();
      if (!at.NEXT) break;
      await tap(...at.NEXT);
      g = await waitGame((x) => x.phase === "packing" && x.tool !== undefined && !x.card, 40, "the next bench");
      if (!g) break;
      await sleep(1200);
      const m0 = log.length;
      await page.evaluate(() => window.unityInstance.SendMessage("Packing", "WebReportBench"));
      const bl = await waitLog(/\[Web\] bench/, 10, m0);
      const ms = bl ? [...bl.matchAll(/(\w+) (-?\d+),(-?\d+) > (-?\d+),(-?\d+)( rotated)?;/g)] : [];
      for (const m of ms) {
        const from = [m[2] * env.w / 1920, m[3] * env.h / 1080], to = [m[4] * env.w / 1920, m[5] * env.h / 1080];
        if (m[6]) { await tap(...from); await tapEl("#turn-btn"); await touch(stroke(from, [to[0], to[1] + LIFT], 12)); }
        else await touch(stroke(from, [to[0], to[1] + LIFT], 12));
        await sleep(300);
      }
      g = await game();
      mark = log.length;
      if (g.seal) await tapEl("#seal-btn");
      else { say(`[Session] delivery ${n}: not sealable by drags alone (${g.sealHint}); MEMORY only`); break; }
      if (!(await waitLog(/\[Web\] journey/, 30, mark))) break;
      await sleep(1500);
      await tapEl("#tc-skip .tb");
      await waitGame((x) => x.phase === "reveal" || x.phase === "results", 30, "the unboxing");
      if (await shown("#tc-skip")) await tapEl("#tc-skip .tb");
      const rv = await waitLog(/\[Web\] review/, 90, mark);
      say(`[Session] delivery ${n}: ${rv ? rv.replace(/^.*\[Web\] /, "") : "no review"}`);
      if (!rv) break;
      await sleep(2000);
    }
    await shot("15-session-end");
  }
  return fpsPlay;
}

// Desktop: the touch controls never show, whatever the mouse and keyboard do.
async function desktopInputs({ page, shot }) {
  const hidden = () => page.evaluate(() => getComputedStyle(document.querySelector("#touch-ui")).display === "none" && !document.querySelector("#touch-ui .cluster.on .tb:not(.hide)")?.offsetParent);
  const seen = [];
  seen.push(await hidden());
  await page.mouse.move(800, 400, { steps: 5 });
  await page.mouse.click(1300, 150);
  await sleep(800);
  seen.push(await hidden());
  await page.keyboard.press("Shift");
  await page.mouse.click(284 * 1600 / 1920, 480 * 900 / 1080);   // START SHIFT
  await sleep(5000);
  seen.push(await hidden());
  await shot("02-desktop-bench");
  check(seen.every(Boolean), `no touch controls on desktop at the title, after mouse moves and clicks, and on the bench (${seen.join(", ")})`);
}

main();
