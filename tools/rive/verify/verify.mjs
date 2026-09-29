// Loads Lumen/Assets/Rive/lumen-core.riv in the official Rive web runtime and checks the LumenCore contract.
// Usage (from tools/rive/verify):  npm install && node verify.mjs [screenshot-dir]
// CHROME=<path> overrides the Chromium executable.
import { chromium } from "playwright-core";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const shots = path.resolve(process.argv[2] || path.join(here, "shots"));
fs.mkdirSync(shots, { recursive: true });
const runtime = path.join(here, "node_modules", "@rive-app", "canvas");
const files = {
  "/": path.join(here, "index.html"),
  "/rive.js": path.join(runtime, "rive.js"),
  "/rive.wasm": path.join(runtime, "rive.wasm"),
  "/lumen-core.riv": path.resolve(here, "..", "..", "..", "Lumen", "Assets", "Rive", "lumen-core.riv"),
};
const types = { ".html": "text/html", ".js": "text/javascript", ".wasm": "application/wasm", ".riv": "application/octet-stream" };
const server = http.createServer((req, res) => {
  const file = files[req.url.split("?")[0]];
  if (!file || !fs.existsSync(file)) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { "content-type": types[path.extname(file)] || "application/octet-stream" });
  fs.createReadStream(file).pipe(res);
}).listen(0);
const port = server.address().port;

const expectedInputs = ["systemState", "health", "power", "temperature", "load", "latency", "pointerX", "pointerY",
  "pointerDistance", "interactionForce", "gravityX", "gravityY", "wake", "pulse", "fault", "recover", "explode",
  "collapse", "inspect", "acknowledge"];
const results = [];
const check = (name, ok, detail = "") => { results.push({ name, ok, detail }); console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  — " + detail : ""}`); };

const browser = await chromium.launch({ executablePath: process.env.CHROME || "/opt/pw-browsers/chromium-1194/chrome-linux/chrome" });
const page = await browser.newPage({ viewport: { width: 500, height: 500 } });
const consoleLines = [];
page.on("console", m => consoleLines.push(m.text()));
page.on("pageerror", e => consoleLines.push("pageerror: " + e.message));
await page.goto(`http://127.0.0.1:${port}/`);
await page.waitForFunction(() => window.lumen.ready || window.lumen.error, null, { timeout: 15000 });
const state = await page.evaluate(() => window.lumen);
check("file loads in the Rive runtime", state.ready && !state.error, state.error || "");
const importErrors = consoleLines.filter(l => /Failed to import|Unknown property|malformed|Malformed/i.test(l));
check("no import errors", importErrors.length === 0, importErrors.slice(0, 3).join(" | "));
const names = (state.inputs || []).map(i => i.name);
const missing = expectedInputs.filter(n => !names.includes(n));
check("all contract inputs present", missing.length === 0, missing.length ? "missing " + missing.join(", ") : `${names.length} inputs`);

const set = (n, v) => page.evaluate(([n, v]) => window.lumen.set(n, v), [n, v]);
const wait = ms => page.waitForTimeout(ms);
const takeEvents = () => page.evaluate(() => window.lumen.events.splice(0));
const shot = n => page.screenshot({ path: path.join(shots, n + ".png") });

const states = ["Offline", "Booting", "Nominal", "Elevated", "Degraded", "Critical", "Recovering"];
await set("load", 0.5); await set("health", 0.96); await set("temperature", 0.23);
for (let i = 0; i < states.length; i++) {
  await set("systemState", i);
  if (i === 4 || i === 5) await set("stressedSubsystem", 2); else await set("stressedSubsystem", -1);
  if (i === 5) { await set("temperature", 0.95); await set("health", 0.4); }
  await wait(i === 1 ? 3200 : 1600);
  await shot(`state-${i}-${states[i].toLowerCase()}`);
}
await set("systemState", 2); await set("temperature", 0.23); await set("health", 0.96); await set("stressedSubsystem", -1);
await wait(1200);
await takeEvents();

// Nominal must not be frozen: two frames 700 ms apart differ.
const a = await page.screenshot(); await wait(700); const b = await page.screenshot();
check("nominal keeps moving", !a.equals(b));

await set("pulse"); await wait(1800);
let ev = (await takeEvents()).map(e => e.name);
check("pulse → PulseCompleted", ev.includes("PulseCompleted"), ev.join(","));

await set("recover"); await wait(2300);
ev = (await takeEvents()).map(e => e.name);
check("recover → RecoveryVisualCompleted", ev.includes("RecoveryVisualCompleted"), ev.join(","));

await set("explode"); await wait(1400); await shot("exploded");
ev = (await takeEvents()).map(e => e.name);
check("explode → ExplodeCompleted", ev.includes("ExplodeCompleted"), ev.join(","));

// Select Thermal (angle 150°) by pointer.
const thermal = { x: 250 + Math.cos(150 * Math.PI / 180) * 222, y: 250 + Math.sin(150 * Math.PI / 180) * 222 };
await page.mouse.move(thermal.x, thermal.y); await page.mouse.down(); await page.mouse.up(); await wait(300);
const sel = (await takeEvents()).filter(e => e.name === "SubsystemSelected");
check("subsystem click → SubsystemSelected(id)", sel.length > 0 && sel[0].properties.id === 2, JSON.stringify(sel));

await set("collapse"); await wait(1200);
ev = (await takeEvents()).map(e => e.name);
check("collapse → CollapseCompleted", ev.includes("CollapseCompleted"), ev.join(","));

// Press, hold past 800 ms, release.
await page.mouse.move(265, 245); await page.mouse.down(); await wait(1100); await shot("charged");
await page.mouse.up(); await wait(200);
ev = (await takeEvents()).map(e => e.name);
check("press → CorePressed", ev.includes("CorePressed"), ev.join(","));
check("hold 800 ms → CoreCharged", ev.includes("CoreCharged"), ev.join(","));
check("release → CoreReleased", ev.includes("CoreReleased"), ev.join(","));

// Continuous inputs visibly change the Core.
await set("pointerX", 0); await set("pointerY", 0); await wait(600);
const before = await page.screenshot();
await set("pointerX", 1); await set("pointerY", -1); await set("pointerDistance", 0.1); await set("load", 1);
await wait(600); const after = await page.screenshot(); await shot("pointer-load");
check("pointer/load inputs change the render", !before.equals(after));

for (const t of ["wake", "fault", "inspect", "acknowledge"]) await set(t);
await wait(1000);
check("all triggers fire without errors", !consoleLines.some(l => /pageerror/.test(l)), consoleLines.filter(l => /pageerror/.test(l)).join(" | "));

fs.writeFileSync(path.join(shots, "results.json"), JSON.stringify({ results, console: consoleLines }, null, 2));
await browser.close();
server.close();
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
