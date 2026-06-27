/* ============================================================================
 * Web-Test-Suite (automatisiert, optional) – Playwright
 * ----------------------------------------------------------------------------
 * Lädt die GETEILTEN Test-Dateien (../runner.js + ../testcases.js) per
 * addScriptTag in die echte navi-/ausfluege-Seite und führt sie aus – exakt
 * dieselben Cases wie die MAUI-Suite.
 *
 * Voraussetzung:  npm install   (installiert playwright)  und  npx playwright install chromium
 * Aufruf:         BASE_URL=http://192.168.2.178:8090 node run-web.mjs
 *                 (Default-BASE_URL = http://192.168.2.178:8090, Dev-Login wird genutzt)
 * Exit-Code 0 = alle bestanden, 1 = mind. ein Test fehlgeschlagen.
 * ========================================================================== */
import { chromium } from "playwright";
import { fileURLToPath } from "url";
import { dirname, resolve } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const RUNNER = resolve(__dirname, "..", "runner.js");
const CASES = resolve(__dirname, "..", "testcases.js");
const BASE = (process.env.BASE_URL || "http://192.168.2.178:8090").replace(/\/$/, "");

const TARGETS = [
    { page: "navi", url: BASE + "/navi/ziel/?lat=52.50&lng=13.42&name=Test&demo=1" },
    { page: "osm", url: BASE + "/ausfluege/" },
];

async function runOne(context, t) {
    const page = await context.newPage();
    page.on("dialog", (d) => d.dismiss().catch(() => {})); // keine Dialoge blockieren
    await page.goto(t.url, { waitUntil: "domcontentloaded" });
    await page.waitForTimeout(4000);                       // JS-Init / Karte
    await page.addScriptTag({ path: RUNNER });
    await page.addScriptTag({ path: CASES });
    const res = await page.evaluate((pk) => window.SPIN.run(pk), t.page);
    await page.close();
    return res;
}

(async () => {
    const browser = await chromium.launch();
    const context = await browser.newContext({
        permissions: ["geolocation"],
        geolocation: { latitude: 52.5, longitude: 13.40 },
        locale: "de-DE",
    });
    // Dev-Login (nur DEBUG) – meldet als „edgar" an, damit /navi/ erreichbar ist.
    try {
        const p = await context.newPage();
        await p.goto(BASE + "/dev-login/", { waitUntil: "domcontentloaded" });
        await p.close();
    } catch (e) { /* falls kein Dev-Login: weiter (Seite ggf. öffentlich) */ }

    let failed = 0;
    for (const t of TARGETS) {
        const r = await runOne(context, t);
        console.log(`\n=== ${r.page.toUpperCase()} : ${r.passed}/${r.total} bestanden (${r.durationMs} ms) ===`);
        for (const c of r.cases) {
            console.log(`  ${c.ok ? "✓" : "✗"}  [${c.group}] ${c.name}${c.ok ? "" : "  → " + c.error}`);
        }
        failed += r.failed;
    }
    await browser.close();
    console.log(`\nGesamt: ${failed === 0 ? "ALLE BESTANDEN" : failed + " FEHLGESCHLAGEN"}`);
    process.exit(failed === 0 ? 0 : 1);
})();
