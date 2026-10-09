// Checks the browser build the way GitHub Pages serves it, in headless Chromium or Firefox.
//
//   node Tools/check-pages.mjs https://nearbycoder.github.io/HandleWithCare/      the live site: title check
//   node Tools/check-pages.mjs --serve Builds/Pages                                 a local copy under /HandleWithCare/
//   node Tools/check-pages.mjs --serve Builds/Pages --browser firefox --play        and the longer play check
//
// Exits 0 only when the game reaches its title screen (the page's window.hwcState.phase "ready", set when the game
// logs "[Web] ready") with no console errors, page errors or failed requests. --play goes on to check that sound
// starts only after the first click, that a settings change is saved to IndexedDB and is back after a reload, and
// plays the first delivery: pack it, ship it, watch the journey, see the review.
//
// --serve copies the folder to Logs/pages-root/HandleWithCare and serves Logs/pages-root with python3's static
// server on a free port, stopped (by its PID) when the check ends. Logs, screenshots and summary.json go to
// Logs/pages-check/<browser> (or --out DIR).
//
// Needs playwright-core (PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core, else the newest one found under
// ~/Sites/*/node_modules). Chromium comes from ~/.cache/ms-playwright (CHROMIUM_PATH overrides); Firefox is the
// system one (FIREFOX_PATH, default /usr/bin/firefox), driven over WebDriver BiDi.
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
const opt = { browser: "chromium", play: false, serve: null, out: null, timeout: 240, url: null };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--browser") opt.browser = args[++i];
  else if (a === "--play") opt.play = true;
  else if (a === "--serve") opt.serve = args[++i];
  else if (a === "--out") opt.out = args[++i];
  else if (a === "--timeout") opt.timeout = Number(args[++i]);
  else if (a === "-h" || a === "--help") { console.log(readFileSync(new URL(import.meta.url), "utf8").split("\nimport")[0]); process.exit(0); }
  else if (!a.startsWith("--")) opt.url = a;
  else { console.error("unknown option " + a); process.exit(2); }
}
if (!opt.url && !opt.serve) { console.error("usage: node Tools/check-pages.mjs <url> | --serve <dir> [--browser chromium|firefox] [--play]"); process.exit(2); }
if (!["chromium", "firefox"].includes(opt.browser)) { console.error("--browser chromium or firefox"); process.exit(2); }
const out = path.resolve(opt.out || path.join(root, "Logs", "pages-check", opt.browser));
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

function findPlaywright() {
  if (process.env.PLAYWRIGHT_CORE) return process.env.PLAYWRIGHT_CORE;
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
// The newest headless shell in Playwright's cache, which need not be the revision this playwright-core expects.
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

function freePort() {
  return new Promise((resolve) => {
    const s = net.createServer().listen(0, "127.0.0.1", () => { const p = s.address().port; s.close(() => resolve(p)); });
  });
}

// ---- in the page -----------------------------------------------------------------------------------------
// Every AudioContext is remembered, and an analyser sits before its speakers: whether the game is producing sound.
// Headless Chromium and Firefox under automation let audio start without any input (their autoplay rules don't
// apply), so the rule desktop browsers ship is played here: an AudioContext stays suspended until a real click,
// tap or key press on the page, as in Chrome. The game has to resume it then.
const audioProbe = () => {
  window.__hwcAudio = { contexts: [], analysers: [], activated: false };
  for (const ev of ["pointerdown", "mousedown", "mouseup", "click", "keydown", "keyup", "touchend"])
    window.addEventListener(ev, (e) => { if (e.isTrusted) window.__hwcAudio.activated = true; }, true);
  for (const name of ["AudioContext", "webkitAudioContext"]) {
    const Orig = window[name];
    if (!Orig) continue;
    window[name] = class extends Orig {
      constructor(...a) { super(...a); window.__hwcAudio.contexts.push(this); if (!window.__hwcAudio.activated) super.suspend(); }
      resume() { return window.__hwcAudio.activated ? super.resume() : Promise.resolve(); }
    };
  }
  const connect = AudioNode.prototype.connect;
  AudioNode.prototype.connect = function (dest, ...rest) {
    const r = connect.call(this, dest, ...rest);
    try {
      if (typeof AudioDestinationNode !== "undefined" && dest instanceof AudioDestinationNode) {
        const ctx = dest.context;
        if (!ctx.__probe) {
          ctx.__probe = ctx.createAnalyser();
          ctx.__probe.fftSize = 2048;
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
};
const audioLevel = () => {
  const a = window.__hwcAudio;
  if (!a) return { states: [], peak: 0 };
  let peak = 0;
  for (const an of a.analysers) {
    const buf = new Float32Array(an.fftSize);
    an.getFloatTimeDomainData(buf);
    for (const v of buf) peak = Math.max(peak, Math.abs(v));
  }
  return { states: a.contexts.map((c) => c.state), peak };
};
// The save as the page's IndexedDB holds it (Unity's IDBFS: database "/idbfs", store "FILE_DATA").
const readSave = () => new Promise((resolve) => {
  const req = indexedDB.open("/idbfs");
  req.onerror = () => resolve(null);
  req.onsuccess = () => {
    const db = req.result;
    if (!db.objectStoreNames.contains("FILE_DATA")) { db.close(); resolve(null); return; }
    const store = db.transaction("FILE_DATA", "readonly").objectStore("FILE_DATA");
    const cur = store.openCursor();
    let found = null;
    cur.onsuccess = () => {
      const c = cur.result;
      if (!c) { db.close(); resolve(found); return; }
      if (String(c.key).endsWith("/save.json") && c.value && c.value.contents) found = new TextDecoder().decode(c.value.contents);
      c.continue();
    };
    cur.onerror = () => { db.close(); resolve(null); };
  };
});

// ---- the run ---------------------------------------------------------------------------------------------
async function main() {
  let server = null, url = opt.url;
  if (opt.serve) {
    const site = path.resolve(opt.serve);
    if (!existsSync(path.join(site, "index.html"))) { console.error("no index.html in " + site); process.exit(2); }
    const serveRoot = path.join(root, "Logs", "pages-root");
    rmSync(serveRoot, { recursive: true, force: true });
    cpSync(site, path.join(serveRoot, "HandleWithCare"), { recursive: true });
    const port = await freePort();
    server = spawn("python3", ["-m", "http.server", String(port), "--bind", "127.0.0.1", "-d", serveRoot], { stdio: "ignore" });
    say(`[Serve] python3 http.server pid ${server.pid} on port ${port}, root ${serveRoot}`);
    url = `http://127.0.0.1:${port}/HandleWithCare/`;
    await sleep(800);
  }
  say(`[Run] ${opt.browser} ${opt.play ? "play check" : "title check"} of ${url} (playwright-core ${pwPath})`);
  say(`[Run] load average ${os.loadavg().map((v) => v.toFixed(1)).join(" ")} on ${os.cpus().length} cores`);

  const launch = { headless: true };
  if (opt.browser === "chromium") {
    launch.executablePath = process.env.CHROMIUM_PATH || cachedChromium();
    // the GPU through ANGLE/Vulkan rather than SwiftShader; and sound waits for a click, as in a normal browser
    launch.args = ["--use-angle=vulkan", "--enable-features=Vulkan", "--ignore-gpu-blocklist", "--autoplay-policy=user-gesture-required"];
    launch.ignoreDefaultArgs = ["--autoplay-policy=no-user-gesture-required"];   // Playwright's own default
  } else {
    launch.channel = "moz-firefox";
    launch.executablePath = process.env.FIREFOX_PATH || "/usr/bin/firefox";
    // the profile under Logs, not the shared /tmp; audible autoplay blocked as Firefox ships it
    process.env.TMPDIR = path.join(root, "Logs", "tmp");
    mkdirSync(process.env.TMPDIR, { recursive: true });
    launch.firefoxUserPrefs = { "media.autoplay.default": 1, "media.autoplay.block-webaudio": true, "media.autoplay.blocking_policy": 0, "webgl.force-enabled": true };
  }

  const log = [];
  const errors = [];
  const bytes = { total: 0, files: {} };
  let browser;
  try {
    browser = await pw[opt.browser].launch(launch);
    const context = await browser.newContext({ viewport: { width: 1600, height: 900 }, acceptDownloads: true });
    await context.addInitScript(audioProbe);
    const page = await context.newPage();
    const t0 = Date.now();
    page.on("console", (m) => {
      const line = `[${((Date.now() - t0) / 1000).toFixed(1)}s ${m.type()}] ${m.text().trimEnd()}`;
      log.push(line);
      if (m.type() === "error") errors.push(line);
    });
    page.on("pageerror", (e) => { const l = "[pageerror] " + e; log.push(l); errors.push(l); });
    page.on("requestfailed", (r) => {
      const why = r.failure()?.errorText || "";
      const l = `[requestfailed] ${r.url()} ${why}`;
      log.push(l);
      if (!/ABORTED|NS_BINDING_ABORTED|cancel/i.test(why)) errors.push(l);   // a reload cancels what was in flight
    });
    page.on("response", (r) => {
      if (r.status() >= 400) { const l = `[http ${r.status()}] ${r.url()}`; log.push(l); errors.push(l); }
      const len = Number(r.headers()["content-length"] || 0);
      if (len > 0) { bytes.files[r.url().split("/").pop()] = len; }
    });
    const shot = (name) => page.screenshot({ path: path.join(out, name + ".png") }).catch((e) => say(`[Shot] ${name} failed: ${e.message}`));
    const state = () => page.evaluate(() => window.hwcState || null).catch(() => null);
    const waitPhase = async (seconds) => {
      const until = Date.now() + seconds * 1000;
      let s = null;
      while (Date.now() < until) {
        s = await state();
        if (s && (s.phase === "ready" || s.phase === "error")) return s;
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
    // the game's UI is laid out on 1920x1080 and scaled to the 1600x900 page
    const ui = (x, y) => [x * 1600 / 1920, y * 900 / 1080];
    const click = async (x, y, button = "left") => { const [cx, cy] = ui(x, y); await page.mouse.click(cx, cy, { button }); };

    // -- load to the title
    await page.goto(url, { waitUntil: "load", timeout: 60000 });
    const s = await waitPhase(opt.timeout);
    const loadSeconds = (Date.now() - t0) / 1000;
    bytes.total = Object.values(bytes.files).reduce((a, b) => a + b, 0);
    await sleep(1500);
    await shot("01-title");
    const ready = log.find((l) => l.includes("[Web] ready"));
    check(s && s.phase === "ready" && ready, `reaches the title screen (${s ? s.phase : "no page state"}${s && s.error ? ": " + s.error : ""}) in ${loadSeconds.toFixed(1)} s, ${(bytes.total / 1048576).toFixed(1)} MB downloaded`);
    if (ready) say("[Run] " + ready);

    if (opt.play && s && s.phase === "ready") await play({ page, log, click, shot, waitPhase, waitLog, state });

    await sleep(500);
    check(errors.length === 0, `no console errors, page errors or failed requests (${errors.length})`);
    for (const e of errors.slice(0, 20)) say("  " + e);
    writeFileSync(path.join(out, "summary.json"), JSON.stringify({ browser: opt.browser, url, loadSeconds, bytes, results, loadavg: os.loadavg() }, null, 2));
  } catch (e) {
    check(false, "the check ran: " + (e && e.stack ? e.stack : e));
  } finally {
    writeFileSync(path.join(out, "console.log"), log.join("\n") + "\n");
    if (browser) await browser.close().catch(() => {});
    if (server) { server.kill("SIGTERM"); say(`[Serve] stopped pid ${server.pid}`); }
    const failed = results.filter((r) => !r.ok).length;
    say(`[Done] ${results.length - failed}/${results.length} passed; log ${path.join(out, "console.log")}`);
    writeFileSync(path.join(out, "check.log"), lines.join("\n") + "\n");
    process.exit(failed === 0 && results.length > 0 ? 0 : 1);
  }
}

// ---- the play check --------------------------------------------------------------------------------------
async function play({ page, log, click, shot, waitPhase, waitLog, state }) {
  // sound: nothing before the first input, music after it
  const before = await page.evaluate(audioLevel);
  check(before.peak === 0 && before.states.length > 0 && !before.states.includes("running"),
        `no sound before any input, with Chrome's autoplay rule played by the page (audio contexts ${JSON.stringify(before.states)}, peak ${before.peak.toFixed(4)})`);
  await click(1500, 160);   // an empty part of the title screen
  let after = null;
  for (let i = 0; i < 20; i++) {
    await sleep(500);
    after = await page.evaluate(audioLevel);
    if (after.peak > 0.001) break;
  }
  check(after.peak > 0.001 && after.states.includes("running"), `sound after the first click (audio contexts ${JSON.stringify(after.states)}, peak ${after.peak.toFixed(4)})`);

  // a setting: GRAPHICS FIDELITY to LOW and SCREEN SHAKE off, then DONE writes the save
  const saved0 = await page.evaluate(readSave);
  await click(254, 678);    // SETTINGS
  await sleep(1500);
  await shot("02-settings");
  await click(1099, 525);   // GRAPHICS FIDELITY: LOW
  await sleep(400);
  await click(858, 480);    // SCREEN SHAKE
  await sleep(600);
  await shot("03-settings-changed");
  await click(960, 942);    // DONE
  await sleep(2500);
  const savedText = await page.evaluate(readSave);
  let saved = null;
  try { saved = JSON.parse(savedText); } catch (e) { /* none yet */ }
  check(saved && saved.Fidelity === 0 && saved.ScreenShake === false,
        `the setting is in IndexedDB after DONE (save.json ${saved ? `Fidelity ${saved.Fidelity}, ScreenShake ${saved.ScreenShake}` : "missing"}${saved0 ? "" : ", none before"})`);

  // the first delivery: pick up each item from the tray and drop it in the box, ship it, watch it
  const from = log.length;
  await click(284, 480);    // START SHIFT
  await sleep(4000);   // the camera settles on the bench
  await shot("04-bench");
  await packFirstDelivery({ page, log, click, shot, waitLog });

  // reload: the setting is back
  const mark = log.length;
  await page.reload({ waitUntil: "load" });
  const s2 = await waitPhase(180);
  const ready2 = await waitLog(/\[Web\] ready/, 5, mark);
  await sleep(1500);
  await shot("08-reloaded-title");
  check(s2 && s2.phase === "ready" && ready2 && /fidelity LOW/.test(ready2) && /shake off/.test(ready2),
        `after a reload the game starts with the saved setting (${ready2 ? ready2.replace(/^.*\[Web\] /, "") : "no ready line"})`);
  await click(1500, 160);
  await sleep(500);
  await click(254, 678);    // SETTINGS, to see it
  await sleep(1500);
  await shot("09-reloaded-settings");
  // FULLSCREEN asks the browser (headless browsers may refuse; that is reported, not failed)
  await click(1628, 260);
  await sleep(1500);
  const fs = await page.evaluate(() => !!(document.fullscreenElement || document.webkitFullscreenElement));
  say(`[Note] FULLSCREEN toggle: the page is ${fs ? "" : "not "}fullscreen`);
  if (fs) { await page.keyboard.press("Escape"); await sleep(800); }
  await click(960, 942);
  void from; void state;
}

// The first delivery, packed with real clicks: the game reports (asked through SendMessage) where each item of
// Mabel's packing lies in the tray and where it goes in the box, so nothing here depends on guessed pixels.
async function packFirstDelivery({ page, log, click, shot, waitLog }) {
  const mark0 = log.length;
  await page.evaluate(() => window.unityInstance.SendMessage("Packing", "WebReportBench"));
  const benchLine = await waitLog(/\[Web\] bench/, 10, mark0);
  if (!benchLine) { check(false, "the bench reports its items ([Web] bench line)"); return; }
  say("[Run] " + benchLine.replace(/^.*\[Web\] /, ""));
  // "bench: delivery 1 | Kind x,y > x,y[ rotated]; ..." in 1920x1080 UI units
  const moves = [...benchLine.matchAll(/(\w+) (-?\d+),(-?\d+) > (-?\d+),(-?\d+)( rotated)?;/g)]
    .map((m) => ({ kind: m[1], from: [Number(m[2]), Number(m[3])], to: [Number(m[4]), Number(m[5])], rotated: !!m[6] }));
  for (const mv of moves) {
    await click(mv.from[0], mv.from[1]);   // pick it up from the tray
    await sleep(600);
    if (mv.rotated) { await click(mv.to[0], mv.to[1] - 300, "right"); await sleep(400); }
    await page.mouse.move(...[mv.to[0] * 1600 / 1920, mv.to[1] * 900 / 1080], { steps: 8 });
    await sleep(400);
    await click(mv.to[0], mv.to[1]);       // and into the box
    await sleep(900);
  }
  await shot("05-packed");
  const mark = log.length;
  await page.keyboard.press("Space");   // SEAL & SHIP
  const shipped = await waitLog(/\[Web\] journey/, 20, mark);
  await sleep(4000);
  await shot("06-journey");
  const review = await waitLog(/\[Web\] review/, 150, mark);
  await sleep(2500);
  await shot("07-review");
  check(moves.length > 0 && shipped && review,
        `delivery 1 packed with ${moves.length} items by mouse, shipped (${shipped ? shipped.replace(/^.*\[Web\] /, "") : "no journey line"}) and reviewed (${review ? review.replace(/^.*\[Web\] /, "") : "no review line"})`);
  if (!review) return;
  // SAVE GIF: the clip arrives as a browser download
  const download = page.waitForEvent("download", { timeout: 120000 }).catch(() => null);
  await click(1782, 1020);   // SAVE GIF
  const d = await download;
  let gifBytes = 0;
  if (d) {
    const file = path.join(out, "clip.gif");
    await d.saveAs(file).catch(() => {});
    try { gifBytes = readFileSync(file).length; } catch (e) { /* not saved */ }
  }
  check(d && /\.gif$/.test(d.suggestedFilename()) && gifBytes > 10000,
        `SAVE GIF downloads the clip (${d ? d.suggestedFilename() : "no download"}, ${(gifBytes / 1024).toFixed(0)} KB)`);
  await sleep(1500);
  await shot("07b-after-gif");
}

main();
