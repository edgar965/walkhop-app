/* ============================================================================
 * spin1more – GETEILTER Test-Runner (Single Source of Truth)
 * ----------------------------------------------------------------------------
 * Dieser Runner wird UNVERÄNDERT von beiden Test-Suiten benutzt:
 *   1. Web-Suite   – ins laufende navi-/ausfluege-Seite injiziert (DevTools-Konsole,
 *                    Bookmarklet oder über die Chrome-Automation).
 *   2. MAUI-Suite  – als gebündeltes Asset in den App-WebView injiziert und über
 *                    EvaluateJavaScriptAsync gestartet/abgefragt.
 *
 * Dadurch laufen in BEIDEN Umgebungen exakt dieselben Test-Cases (testcases.js)
 * gegen exakt dieselbe Seite → „genau eine Korrespondenz" der Test-Cases.
 *
 * Ablauf für die MAUI-Suite (asynchron, da EvaluateJavaScript keine Promises awaitet):
 *   - testcases.js + runner.js injizieren
 *   - SPIN.run('navi') aufrufen (startet, kehrt sofort zurück)
 *   - window.__SPIN_DONE__ pollen bis true
 *   - JSON.stringify(window.__SPIN_RESULTS__) auslesen
 * ========================================================================== */
(function () {
    "use strict";
    var SPIN = (window.SPIN = window.SPIN || {});
    var U = (SPIN.util = SPIN.util || {});

    U.$ = function (id) { return document.getElementById(id); };
    U.q = function (sel) { return document.querySelector(sel); };
    U.qa = function (sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); };
    U.sleep = function (ms) { return new Promise(function (r) { setTimeout(r, ms); }); };
    U.now = function () {
        return (window.performance && performance.now) ? performance.now() : +new Date();
    };

    // Wartet, bis pred() truthy ist (oder Timeout). Gibt true/false zurück.
    U.waitFor = async function (pred, timeout, interval) {
        timeout = timeout || 8000; interval = interval || 100;
        var start = U.now();
        while (U.now() - start < timeout) {
            try { if (pred()) return true; } catch (e) { /* pred darf werfen */ }
            await U.sleep(interval);
        }
        return false;
    };

    U.assert = function (cond, msg) {
        if (!cond) throw new Error(msg || "Assertion fehlgeschlagen");
    };

    // Element per id ("nv-los") ODER Selektor ("#x", ".y"). Wirft, wenn nicht da.
    U.el = function (sel) {
        var e = (typeof sel === "string")
            ? (/^[.#\[]/.test(sel) ? U.q(sel) : U.$(sel))
            : sel;
        return e || null;
    };
    U.assertEl = function (sel, msg) {
        var e = U.el(sel);
        if (!e) throw new Error(msg || ("Element fehlt: " + sel));
        return e;
    };

    U.visible = function (sel) {
        var el = U.el(sel);
        if (!el || el.hidden) return false;
        var s = getComputedStyle(el);
        if (s.display === "none" || s.visibility === "hidden" || +s.opacity === 0) return false;
        var r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    };

    U.click = function (sel) {
        var el = U.assertEl(sel, "Klick-Ziel fehlt: " + sel);
        el.click();
        return el;
    };

    // Welche Seite ist geladen? (für die Auswahl der passenden Test-Gruppe)
    SPIN.detectPage = function () {
        var nv = U.$("navi");
        if (nv || U.$("navi-karte")) return (nv && nv.dataset && nv.dataset.modus === "tour") ? "navitour" : "navi";
        if (U.q(".karte-rechts") || U.q(".krb-ort") || U.$("ausfluege-karte") || U.$("karte")) return "osm";
        return null;
    };

    // Führt alle Test-Cases der Seite aus. Ergebnis zusätzlich in window.__SPIN_RESULTS__.
    SPIN.run = async function (pageKey) {
        pageKey = pageKey || SPIN.detectPage();
        window.__SPIN_DONE__ = false;
        window.__SPIN_RESULTS__ = null;
        var cases = (SPIN.testcases && SPIN.testcases[pageKey]) || [];
        var res = {
            page: pageKey, total: cases.length, passed: 0, failed: 0,
            cases: [], startedAt: Date.now()
        };
        for (var i = 0; i < cases.length; i++) {
            var c = cases[i], t0 = U.now(), ok = true, err = "";
            try {
                await c.fn(U, SPIN);
            } catch (e) {
                ok = false; err = (e && e.message) ? e.message : String(e);
            }
            if (c.cleanup) { try { await c.cleanup(U, SPIN); } catch (e2) { /* egal */ } }
            res.cases.push({
                id: c.id, name: c.name, group: c.group || "",
                ok: ok, error: err, ms: Math.round(U.now() - t0)
            });
            if (ok) res.passed++; else res.failed++;
        }
        res.finishedAt = Date.now();
        res.durationMs = res.finishedAt - res.startedAt;
        window.__SPIN_RESULTS__ = res;
        window.__SPIN_DONE__ = true;
        return res;
    };

    // Einen EINZELNEN Test-Case ausführen (für die Hilfe→Tests-Seite, Run-Button je Test).
    SPIN.runOne = async function (pageKey, id) {
        var cases = (SPIN.testcases && SPIN.testcases[pageKey]) || [];
        var c = cases.filter(function (x) { return x.id === id; })[0];
        if (!c) return { id: id, ok: false, error: "Unbekannter Test: " + id };
        var ok = true, err = "", t0 = U.now();
        try { await c.fn(U, SPIN); }
        catch (e) { ok = false; err = (e && e.message) ? e.message : String(e); }
        if (c.cleanup) { try { await c.cleanup(U, SPIN); } catch (e2) { /* egal */ } }
        return { id: c.id, name: c.name, group: c.group || "", ok: ok, error: err, ms: Math.round(U.now() - t0) };
    };

    // Bequemer Konsolen-Lauf: SPIN.runConsole() → Tabelle + Zusammenfassung.
    SPIN.runConsole = async function (pageKey) {
        var r = await SPIN.run(pageKey);
        try {
            console.table(r.cases.map(function (c) {
                return { Gruppe: c.group, Test: c.name, OK: c.ok ? "✓" : "✗", ms: c.ms, Fehler: c.error };
            }));
        } catch (e) { /* console.table evtl. nicht da */ }
        console.log("spin1more-Tests [" + r.page + "]: " + r.passed + "/" + r.total +
            " bestanden, " + r.failed + " fehlgeschlagen (" + r.durationMs + " ms)");
        return r;
    };
})();
