/* ============================================================================
 * spin1more – GETEILTE Test-Cases (Single Source of Truth)
 * ----------------------------------------------------------------------------
 * Deckt ALLE Buttons/Funktionen der bestehenden Web-App ab. Web-Suite UND
 * MAUI-Suite laden GENAU diese Datei → exakte 1:1-Korrespondenz.
 *
 * Seiten/Gruppen:
 *   "navi"     – Navigation (Ziel/Demo): /navi/ziel/?lat=..&lng=..&demo=1
 *   "osm"      – Übersicht + OSM-Kontrolle: /ausfluege/  (#ausfluege-karte)
 *   "navitour" – Navigation im TOUR-Modus: /navi/tour/<pk>/?demo=1
 *
 * Case: { id, name, group, fn(U, SPIN) [, cleanup(U, SPIN)] }  – besteht, wenn fn NICHT wirft.
 * ========================================================================== */
(function () {
    "use strict";
    var SPIN = (window.SPIN = window.SPIN || {});

    // ---- Helfer -------------------------------------------------------------
    async function sheetAuf(U) {
        var s = U.assertEl("nv-settings", "Einstellungs-Sheet fehlt");
        if (s.hidden) {
            U.click("nv-settings-btn");
            await U.waitFor(function () { return !s.hidden; }, 4000);
        }
        U.assert(!s.hidden, "Einstellungs-Sheet ließ sich nicht öffnen");
        return s;
    }
    async function modusWaehlen(U, wahl) {
        await sheetAuf(U);
        var b = U.assertEl('#nv-optionen button[data-modus-wahl="' + wahl + '"]', "Modus-Knopf fehlt: " + wahl);
        U.click(b);
        await U.waitFor(function () { return b.classList.contains("aktiv"); }, 3000);
        return b;
    }

    // Mittlere Maustaste muss von der Karte abgefangen werden, sonst „blättert" die Seite.
    // Prüft BEIDE Event-Modelle: pointerdown (Android-WebView!) UND mousedown (Desktop).
    // Reproduziert den Bug: ohne Pointer-Behandlung ist pd.defaultPrevented = false.
    function mittelTasteAbgefangen(U, containerId, pruefeScroll) {
        var c = U.assertEl(containerId, "Kartencontainer fehlt: " + containerId);
        var r = c.getBoundingClientRect();
        var cx = Math.round(r.left + r.width / 2), cy = Math.round(r.top + r.height / 2);
        var Ptr = window.PointerEvent || MouseEvent;
        var ziel = document.elementFromPoint(cx, cy) || c;
        var sVor = window.scrollY || document.documentElement.scrollTop || 0;
        var pd = new Ptr("pointerdown", { button: 1, buttons: 4, clientX: cx, clientY: cy, bubbles: true, cancelable: true, pointerId: 1, pointerType: "mouse" });
        ziel.dispatchEvent(pd);
        var md = new MouseEvent("mousedown", { button: 1, buttons: 4, clientX: cx, clientY: cy, bubbles: true, cancelable: true });
        ziel.dispatchEvent(md);
        U.assert(pd.defaultPrevented, "Pointer-Mittelklick NICHT abgefangen → Seite blättert (Android-WebView)");
        U.assert(md.defaultPrevented, "Maus-Mittelklick NICHT abgefangen → Seite blättert (Desktop)");
        for (var i = 0; i < 6; i++)
            window.dispatchEvent(new Ptr("pointermove", { button: 1, buttons: 4, clientX: cx, clientY: cy - (i + 1) * 6, bubbles: true, cancelable: true, pointerId: 1 }));
        window.dispatchEvent(new Ptr("pointerup", { button: 1, clientX: cx, clientY: cy - 36, bubbles: true, cancelable: true, pointerId: 1 }));
        if (pruefeScroll) {
            var sNach = window.scrollY || document.documentElement.scrollTop || 0;
            U.assert(sNach === sVor, "Seite hat gescrollt (soll nicht): " + sVor + " → " + sNach);
        }
    }

    // ============================== NAVI (Ziel/Demo) =========================
    var navi = [
        // --- Vorhandensein ---
        { id: "nv-exists-karte", group: "Vorhandensein", name: "Kartencontainer", fn: function (U) { U.assertEl("navi-karte"); } },
        { id: "nv-exists-zurueck", group: "Vorhandensein", name: "Beenden-✕", fn: function (U) { U.assertEl("nv-zurueck"); } },
        { id: "nv-exists-zentrieren", group: "Vorhandensein", name: "Zentrieren/Kompass", fn: function (U) { U.assertEl("nv-zentrieren"); } },
        { id: "nv-exists-ton", group: "Vorhandensein", name: "Ton-Knopf", fn: function (U) { U.assertEl("nv-ton"); } },
        { id: "nv-exists-suche", group: "Vorhandensein", name: "POI-Suche-Knopf", fn: function (U) { U.assertEl("nv-suche-btn"); } },
        { id: "nv-exists-voll", group: "Vorhandensein", name: "Vollbild-Knopf", fn: function (U) { U.assertEl("nv-voll"); } },
        { id: "nv-exists-los", group: "Vorhandensein", name: "Navigation-starten-Knopf", fn: function (U) { U.assertEl("nv-los"); } },
        { id: "nv-exists-griff", group: "Vorhandensein", name: "Panel-Griff", fn: function (U) { U.assertEl("nv-panel-griff"); } },
        { id: "nv-exists-settings-btn", group: "Vorhandensein", name: "Einstellungen-Knopf", fn: function (U) { U.assertEl("nv-settings-btn"); } },
        { id: "nv-exists-lang", group: "Vorhandensein", name: "Sprach-Knopf", fn: function (U) { U.assertEl("nv-lang"); } },

        // --- Karten-Steuerung ---
        { id: "nv-voll-toggle", group: "Karten-Steuerung", name: "Vollbild an/aus (nv-voll-an)",
          fn: async function (U) {
              var n = U.assertEl("navi"); var vor = n.classList.contains("nv-voll-an");
              U.click("nv-voll");
              U.assert(await U.waitFor(function () { return n.classList.contains("nv-voll-an") !== vor; }, 3000), "Vollbild togglet nicht");
          },
          cleanup: async function (U) { var n = U.el("navi"); if (n && n.classList.contains("nv-voll-an")) { U.click("nv-voll"); await U.sleep(150); } } },
        { id: "nv-ton-toggle", group: "Karten-Steuerung", name: "Ton-Knopf togglet",
          fn: async function (U) {
              var b = U.assertEl("nv-ton"); var vor = b.classList.contains("aktiv");
              U.click(b);
              U.assert(await U.waitFor(function () { return b.classList.contains("aktiv") !== vor; }, 3000), "Ton togglet nicht");
          },
          cleanup: async function (U) { var b = U.el("nv-ton"); if (b && b.classList.contains("aktiv")) { U.click(b); await U.sleep(120); } } },
        { id: "nv-suche-toggle", group: "Karten-Steuerung", name: "POI-Suchpanel auf/zu",
          fn: async function (U) {
              var p = U.assertEl("nv-suche-panel");
              U.click("nv-suche-btn");
              U.assert(await U.waitFor(function () { return !p.hidden; }, 3000), "Suchpanel öffnet nicht");
              U.click("nv-suche-btn");
              U.assert(await U.waitFor(function () { return p.hidden; }, 3000), "Suchpanel schließt nicht");
          },
          cleanup: function (U) { var p = U.el("nv-suche-panel"); if (p) p.hidden = true; } },
        { id: "nv-mittel-zoom", group: "Karten-Steuerung", name: "Mittlere Maustaste abgefangen (kein Blättern)",
          fn: function (U) { mittelTasteAbgefangen(U, "navi-karte"); } },

        // --- POI-Suche (Felder + Knöpfe) ---
        { id: "nv-suche-funktionen", group: "POI-Suche", name: "Suchfeld + Suchen/In-der-Nähe/Leeren",
          fn: async function (U) {
              var p = U.assertEl("nv-suche-panel"), feld = U.assertEl("nv-suche-feld");
              U.click("nv-suche-btn"); await U.waitFor(function () { return !p.hidden; }, 3000);
              feld.value = "Berlin"; feld.dispatchEvent(new Event("input", { bubbles: true }));
              U.click("nv-suche-los"); await U.sleep(200);     // löst POI-Suche aus (Netz) – darf nicht werfen
              U.click("nv-suche-nah"); await U.sleep(200);     // „In der Nähe"
              U.click("nv-suche-weg");                          // leert Feld + POIs
              U.assert(await U.waitFor(function () { return feld.value === ""; }, 2000), "Suchfeld wurde nicht geleert");
          },
          cleanup: function (U) { var p = U.el("nv-suche-panel"); if (p) p.hidden = true; } },

        // --- Panel ---
        { id: "nv-panel-toggle", group: "Panel", name: "Panel ein-/ausklappen (minimiert)",
          fn: async function (U) {
              var p = U.assertEl("nv-panel"); var vor = p.classList.contains("minimiert");
              U.click("nv-panel-griff");
              U.assert(await U.waitFor(function () { return p.classList.contains("minimiert") !== vor; }, 3000), "Panel togglet nicht");
              U.click("nv-panel-griff");
          } },

        // --- Einstellungs-Sheet ---
        { id: "nv-settings-open", group: "Einstellungen", name: "Sheet öffnet", fn: async function (U) { await sheetAuf(U); } },
        { id: "nv-karten-alle", group: "Einstellungen", name: "Alle 4 Kartenmodi umschaltbar",
          fn: async function (U) {
              await sheetAuf(U);
              var modi = ["wandern", "standard", "satellit", "dunkel"];
              for (var i = 0; i < modi.length; i++) {
                  var b = U.assertEl('#nv-karten button[data-karte="' + modi[i] + '"]', "Kartenmodus fehlt: " + modi[i]);
                  U.click(b);
                  U.assert(await U.waitFor((function (bb) { return function () { return bb.classList.contains("aktiv"); }; })(b), 2500),
                      "Kartenmodus nicht aktiv: " + modi[i]);
              }
          },
          cleanup: async function (U) { var w = U.el('#nv-karten button[data-karte="wandern"]'); if (w) { U.click(w); await U.sleep(120); } } },
        { id: "nv-wanderwege-toggle", group: "Einstellungen", name: "Wanderwege-Overlay an/aus",
          fn: async function (U) {
              await sheetAuf(U);
              var c = U.assertEl("nv-wanderwege"); var vor = c.checked; c.click();
              U.assert(await U.waitFor(function () { return c.checked !== vor; }, 2500), "Wanderwege togglet nicht");
              if (c.checked !== vor) c.click();
          } },
        { id: "nv-modus-alle", group: "Einstellungen", name: "Fortbewegung Fuß/Rad/Auto umschaltbar",
          fn: async function (U) {
              await modusWaehlen(U, "pedestrian");
              await modusWaehlen(U, "bicycle");
              await modusWaehlen(U, "auto");
          },
          cleanup: async function (U) { try { await modusWaehlen(U, "pedestrian"); } catch (e) {} } },
        { id: "nv-weg-alle", group: "Einstellungen", name: "Wegtyp Befestigt/Neutral/Offroad",
          fn: async function (U) {
              await modusWaehlen(U, "pedestrian");
              var wege = ["fest", "neutral", "natur"];
              for (var i = 0; i < wege.length; i++) {
                  var b = U.assertEl('[data-weg="' + wege[i] + '"]', "Wegtyp fehlt: " + wege[i]);
                  U.click(b);
                  U.assert(await U.waitFor((function (bb) { return function () { return bb.classList.contains("aktiv"); }; })(b), 2500),
                      "Wegtyp nicht aktiv: " + wege[i]);
              }
          },
          cleanup: function (U) { var n = U.el('[data-weg="neutral"]'); if (n) n.click(); } },
        { id: "nv-opt-checkboxen", group: "Einstellungen", name: "Vermeidungs-Optionen (Autobahn/Wege/Oberfläche)",
          fn: async function (U) {
              await sheetAuf(U);
              var ids = ["opt-autobahn", "opt-auto-unbefestigt", "opt-oberflaeche"], n = 0;
              for (var i = 0; i < ids.length; i++) {
                  var c = U.el(ids[i]); if (!c) continue; n++;
                  var vor = c.checked; c.click();
                  U.assert(await U.waitFor((function (cc, v) { return function () { return cc.checked !== v; }; })(c, vor), 2000),
                      "Checkbox togglet nicht: " + ids[i]);
                  c.click(); // zurück
              }
              U.assert(n >= 1, "Keine Vermeidungs-Checkbox gefunden");
          } },
        { id: "nv-sprache-ton", group: "Einstellungen", name: "Ton im Sheet togglet (synct mit nv-ton)",
          fn: async function (U) {
              await sheetAuf(U);
              var s = U.assertEl("nv-sprache"); var vor = s.classList.contains("aktiv"); s.click();
              U.assert(await U.waitFor(function () { return s.classList.contains("aktiv") !== vor; }, 2500), "Ton-Knopf (Sheet) togglet nicht");
              var t = U.el("nv-ton");
              if (t) U.assert(t.classList.contains("aktiv") === s.classList.contains("aktiv"), "nv-ton nicht synchron");
              s.click();
          } },
        { id: "nv-lang-toggle", group: "Einstellungen", name: "Sprache umschaltbar (nv-lang)",
          fn: async function (U) {
              await sheetAuf(U);
              var b = U.assertEl("nv-lang"); var vor = b.textContent; b.click();
              U.assert(await U.waitFor(function () { return b.textContent !== vor; }, 2500), "Sprache wechselt nicht");
          } },
        { id: "nv-settings-close", group: "Einstellungen", name: "Sheet schließt",
          fn: async function (U) {
              var s = U.assertEl("nv-settings"); if (s.hidden) await sheetAuf(U);
              U.click("nv-set-zu");
              U.assert(await U.waitFor(function () { return s.hidden; }, 3000), "Sheet schließt nicht");
          } },

        // --- Navigations-Lebenszyklus (Demo) ---
        { id: "nv-start", group: "Navigation (Demo)", name: "Navigation startet (→ beenden)",
          fn: async function (U) {
              var los = U.assertEl("nv-los");
              U.assert(/starten/i.test(los.textContent), "Erwartet Startzustand");
              U.click(los);
              U.assert(await U.waitFor(function () { return /beenden/i.test(los.textContent); }, 15000), "Knopf wechselt nicht auf beenden");
          } },
        { id: "nv-richtungspfeil", group: "Navigation (Demo)", name: "Lila Richtungspfeil erscheint",
          fn: async function (U) { U.assert(await U.waitFor(function () { return !!U.q(".nv-richtung"); }, 15000), "Richtungspfeil fehlt"); } },
        { id: "nv-mitfahren", group: "Navigation (Demo)", name: "Zentrieren/Mitfahren aktiviert sich",
          fn: async function (U) {
              U.click("nv-zentrieren");
              U.assert(await U.waitFor(function () { return U.$("nv-zentrieren").classList.contains("aktiv"); }, 4000), "Zentrieren nicht aktiv");
          } },
        { id: "nv-kompass-toggle", group: "Navigation (Demo)", name: "Nord/Fahrtrichtung-Umschaltung",
          fn: async function (U) {
              var b = U.assertEl("nv-zentrieren"); var vor = b.classList.contains("nv-nord"); U.click(b);
              U.assert(await U.waitFor(function () { return b.classList.contains("nv-nord") !== vor; }, 4000), "Nord/Fahrtrichtung togglet nicht");
          } },
        { id: "nv-stop", group: "Navigation (Demo)", name: "Navigation beendet (→ starten)",
          fn: async function (U) {
              var los = U.assertEl("nv-los"); if (/beenden/i.test(los.textContent)) U.click(los);
              U.assert(await U.waitFor(function () { return /starten/i.test(los.textContent); }, 6000), "Knopf wechselt nicht auf starten");
          } },

        // --- Vorhandensein (Tiefe: Manöver/Panel/Plan/Ziele/Aufnahme) ---
        { id: "nv-exists-pfeil", group: "Vorhandensein+", name: "Abbiege-Pfeil (nv-pfeil)", fn: function (U) { U.assertEl("nv-pfeil"); } },
        { id: "nv-exists-dist-naechst", group: "Vorhandensein+", name: "Distanz nächstes Manöver", fn: function (U) { U.assertEl("nv-dist-naechst"); } },
        { id: "nv-exists-danach", group: "Vorhandensein+", name: "„Danach\"-Vorschau", fn: function (U) { U.assertEl("nv-danach"); } },
        { id: "nv-exists-hoehe", group: "Vorhandensein+", name: "Höhenprofil-Container", fn: function (U) { U.assertEl("nv-hoehe"); } },
        { id: "nv-exists-dist", group: "Vorhandensein+", name: "Distanz-Wert (Panel)", fn: function (U) { U.assertEl("nv-dist"); } },
        { id: "nv-exists-zeit", group: "Vorhandensein+", name: "Fahrzeit-Wert (Panel)", fn: function (U) { U.assertEl("nv-zeit"); } },
        { id: "nv-exists-ankunft", group: "Vorhandensein+", name: "Ankunftszeit (Panel)", fn: function (U) { U.assertEl("nv-ankunft"); } },
        { id: "nv-exists-ziele", group: "Vorhandensein+", name: "Letzte Ziele/Favoriten", fn: function (U) { U.assertEl("nv-ziele"); } },
        { id: "nv-exists-alt-chip", group: "Vorhandensein+", name: "Alternativrouten-Chip", fn: function (U) { U.assertEl("nv-alt-chip"); } },
        { id: "nv-exists-plan", group: "Vorhandensein+", name: "Routenplan-Panel", fn: function (U) { U.assertEl("nv-plan"); } },
        { id: "nv-exists-suche-feld", group: "Vorhandensein+", name: "POI-Suchfeld", fn: function (U) { U.assertEl("nv-suche-feld"); } },
        { id: "nv-exists-chev", group: "Vorhandensein+", name: "Panel-Chevron", fn: function (U) { U.assertEl("nv-chev"); } },
        { id: "nv-exists-rec-band", group: "Vorhandensein+", name: "Aufnahme-Band (Track)", fn: function (U) { U.assertEl("nv-rec-band"); } },
        { id: "nv-exists-rec-modal", group: "Vorhandensein+", name: "Aufnahme-Speichern-Dialog", fn: function (U) { U.assertEl("nv-rec-modal"); } },

        // --- Einstellungen (Tiefe) ---
        { id: "nv-set-karten-4", group: "Einstellungen+", name: "4 Kartenmodi im Sheet",
          fn: async function (U) { await sheetAuf(U); U.assert(U.qa('#nv-karten button[data-karte]').length === 4, "Erwartet 4 Kartenmodi"); },
          cleanup: function (U) { var s = U.el("nv-settings"); if (s) s.hidden = true; } },
        { id: "nv-set-modi-3", group: "Einstellungen+", name: "Fuß/Rad/Auto im Sheet",
          fn: async function (U) { await sheetAuf(U); U.assert(U.qa('#nv-optionen [data-modus-wahl]').length >= 3, "Erwartet 3 Modi"); },
          cleanup: function (U) { var s = U.el("nv-settings"); if (s) s.hidden = true; } },
        { id: "nv-set-wegtyp-3", group: "Einstellungen+", name: "Wegtypen fest/neutral/natur",
          fn: async function (U) { await sheetAuf(U); U.assert(U.qa('[data-weg]').length >= 3, "Erwartet 3 Wegtypen"); },
          cleanup: function (U) { var s = U.el("nv-settings"); if (s) s.hidden = true; } },

        // --- Demo-Verhalten: echte Turn-by-Turn-Ausgabe nach Start ---
        { id: "nv-demo-pfeil-svg", group: "Navigation (Demo)+", name: "Abbiege-Pfeil zeigt Symbol",
          fn: async function (U) {
              var los = U.assertEl("nv-los");
              if (/starten/i.test(los.textContent)) { U.click(los); await U.waitFor(function () { return /beenden/i.test(los.textContent); }, 15000); }
              U.assert(await U.waitFor(function () { var p = U.el("nv-pfeil"); return p && p.querySelector("svg"); }, 12000), "Kein Pfeil-SVG");
          },
          cleanup: async function (U) { var l = U.el("nv-los"); if (l && /beenden/i.test(l.textContent)) { U.click(l); await U.sleep(300); } } },
        { id: "nv-demo-anweisung", group: "Navigation (Demo)+", name: "Abbiege-Text erscheint",
          fn: async function (U) {
              var los = U.assertEl("nv-los");
              if (/starten/i.test(los.textContent)) { U.click(los); await U.waitFor(function () { return /beenden/i.test(los.textContent); }, 15000); }
              U.assert(await U.waitFor(function () { var t = U.el("nv-text"); return t && t.textContent.trim().length > 0; }, 12000), "Keine Abbiege-Anweisung");
          },
          cleanup: async function (U) { var l = U.el("nv-los"); if (l && /beenden/i.test(l.textContent)) { U.click(l); await U.sleep(300); } } }
    ];

    // ============================== OSM (Übersicht) =========================
    var osm = [
        // --- Vorhandensein ---
        { id: "osm-exists-karte", group: "Vorhandensein", name: "Kartencontainer",
          fn: function (U) { U.assert(U.q("#ausfluege-karte") || U.q("#karte"), "Kein Kartencontainer"); } },
        { id: "osm-exists-control", group: "Vorhandensein", name: "Rechte Knopfleiste (.karte-rechts)", fn: function (U) { U.assertEl(".karte-rechts"); } },
        { id: "osm-exists-ort", group: "Vorhandensein", name: "Zentrieren/Kompass (.krb-ort)", fn: function (U) { U.assertEl(".krb-ort"); } },
        { id: "osm-exists-voll", group: "Vorhandensein", name: "Vollbild (.krb-voll)", fn: function (U) { U.assertEl(".krb-voll"); } },
        { id: "osm-exists-layers", group: "Vorhandensein", name: "Ebenen-Umschalter (.leaflet-control-layers)", fn: function (U) { U.assertEl(".leaflet-control-layers"); } },
        { id: "osm-exists-foto", group: "Vorhandensein", name: "Kamera-Knopf (.foto-ctl)", fn: function (U) { U.assertEl(".foto-ctl"); } },
        { id: "osm-exists-suchfeld", group: "Vorhandensein", name: "Suchfeld", fn: function (U) { U.assertEl("suchfeld"); } },
        { id: "osm-exists-filter", group: "Vorhandensein", name: "Kategorie-Filter", fn: function (U) { U.assertEl("#kat-filter .filter-chip"); } },
        { id: "osm-exists-standort", group: "Vorhandensein", name: "Standort-Knopf", fn: function (U) { U.assertEl("standort-btn"); } },
        { id: "osm-exists-umkreis", group: "Vorhandensein", name: "Umkreis-Schalter", fn: function (U) { U.assertEl("umkreis-an"); } },

        // --- Steuerung / Funktionen ---
        { id: "osm-voll-toggle", group: "Steuerung", name: "OSM-Vollbild an/aus (.karte-voll)",
          fn: async function (U) {
              U.click(".krb-voll");
              U.assert(await U.waitFor(function () { return !!U.q(".karte-voll"); }, 3000), "Vollbild nicht aktiviert");
              U.assert(U.q(".krb-voll").classList.contains("aktiv"), "Vollbild-Knopf nicht aktiv");
              U.click(".krb-voll");
              U.assert(await U.waitFor(function () { return !U.q(".karte-voll"); }, 3000), "Vollbild nicht verlassen");
          },
          cleanup: async function (U) { if (U.q(".karte-voll")) { var b = U.q(".krb-voll"); if (b) b.click(); await U.sleep(150); } } },
        { id: "osm-ort-toggle", group: "Steuerung", name: "OSM Nord/Fahrtrichtung (.nord)",
          fn: async function (U) {
              var b = U.assertEl(".krb-ort"); var vor = b.classList.contains("nord"); b.click();
              U.assert(await U.waitFor(function () { return b.classList.contains("nord") !== vor; }, 4000), "Nord/Fahrtrichtung togglet nicht");
          },
          cleanup: async function (U) { var b = U.q(".krb-ort"); if (b && b.classList.contains("nord")) { b.click(); await U.sleep(120); } } },
        { id: "osm-layer-satellit", group: "Steuerung", name: "Ebenen-Umschalter auf Satellit",
          fn: async function (U) {
              var labels = U.qa(".leaflet-control-layers label");
              var sat = labels.filter(function (l) { return /satellit/i.test(l.textContent); })[0];
              U.assert(sat, "Satellit-Ebene nicht gefunden");
              var inp = sat.querySelector("input"); U.assert(inp, "Satellit-Radio fehlt");
              inp.click();
              U.assert(await U.waitFor(function () { return inp.checked; }, 2500), "Satellit nicht ausgewählt");
          },
          cleanup: function (U) {
              var std = U.qa(".leaflet-control-layers label").filter(function (l) { return /standard/i.test(l.textContent); })[0];
              if (std) { var i = std.querySelector("input"); if (i) i.click(); }
          } },
        { id: "osm-foto-toggle", group: "Steuerung", name: "Kamera-Knopf (Fotos ein/aus)",
          fn: async function (U) {
              var f = U.assertEl(".foto-ctl"); f.click(); await U.sleep(300);
              U.assert(U.q(".foto-ctl"), "Kamera-Knopf nach Klick verschwunden");
          },
          cleanup: async function (U) { /* zweiter Klick = wieder aus */ var f = U.q(".foto-ctl"); if (f) { f.click(); await U.sleep(150); } } },
        { id: "osm-suche", group: "Steuerung", name: "Suchfeld nimmt Eingabe an",
          fn: async function (U) {
              var feld = U.assertEl("suchfeld");
              feld.value = "Berlin"; feld.dispatchEvent(new Event("input", { bubbles: true }));
              await U.sleep(300);
              U.assert(feld.value === "Berlin", "Suchfeld übernahm Eingabe nicht");
          },
          cleanup: function (U) { var f = U.el("suchfeld"); if (f) { f.value = ""; f.dispatchEvent(new Event("input", { bubbles: true })); } } },
        { id: "osm-filter-chip", group: "Steuerung", name: "Kategorie-Filter umschaltbar",
          fn: async function (U) {
              var chips = U.qa("#kat-filter .filter-chip");
              U.assert(chips.length >= 2, "Zu wenige Filter-Chips");
              var ziel = chips[1];
              ziel.click();
              U.assert(await U.waitFor(function () { return ziel.classList.contains("is-active"); }, 3000), "Filter-Chip nicht aktiv");
          },
          cleanup: async function (U) { var alle = U.qa("#kat-filter .filter-chip")[0]; if (alle) { alle.click(); await U.sleep(150); } } },
        { id: "osm-umkreis-toggle", group: "Steuerung", name: "Umkreis-Suche einschaltbar",
          fn: async function (U) {
              var cb = U.assertEl("umkreis-an"), steu = U.el("umkreis-steuerung");
              if (!cb.checked) cb.click();
              if (steu) U.assert(await U.waitFor(function () { return !steu.hidden; }, 3000), "Umkreis-Steuerung nicht sichtbar");
          },
          cleanup: function (U) { var cb = U.el("umkreis-an"); if (cb && cb.checked) cb.click(); } },
        { id: "osm-tour-dialog", group: "Steuerung", name: "Tour-Detail-Dialog öffnet/schließt",
          fn: async function (U) {
              U.assert(await U.waitFor(function () { return !!U.q(".tour-visual"); }, 8000), "Keine Tour-Kachel geladen");
              U.q(".tour-visual").click();
              U.assert(await U.waitFor(function () { var d = U.q(".tdlg"); return d && !d.hidden; }, 5000), "Dialog öffnet nicht");
              U.assertEl(".tdlg-close").click();
              U.assert(await U.waitFor(function () { var d = U.q(".tdlg"); return !d || d.hidden; }, 3000), "Dialog schließt nicht");
          },
          cleanup: function (U) { var d = U.q(".tdlg"); if (d && !d.hidden) { var c = d.querySelector(".tdlg-close"); if (c) c.click(); } } },
        { id: "osm-mittel-zoom", group: "Steuerung", name: "Mittlere Maustaste abgefangen (kein Blättern)",
          fn: function (U) { mittelTasteAbgefangen(U, U.q("#ausfluege-karte") ? "ausfluege-karte" : "karte", true); } },

        // --- Vorhandensein (Tiefe: Entdecken-Werkzeuge) ---
        { id: "osm-exists-ortsuche", group: "Vorhandensein+", name: "Ortssuche (Geocoding)", fn: function (U) { U.assertEl("ort-suche"); U.assertEl("ort-suche-btn"); } },
        { id: "osm-exists-bahn", group: "Vorhandensein+", name: "Bahnhof-am-Start-Filter", fn: function (U) { U.assertEl("bahn-an"); } },
        { id: "osm-exists-radius-slider", group: "Vorhandensein+", name: "Radius-Slider", fn: function (U) { U.assertEl("radius"); } },
        { id: "osm-exists-radius-chips", group: "Vorhandensein+", name: "Radius-Chips (4)", fn: function (U) { U.assert(U.qa('#radius-chips button[data-km]').length === 4, "Erwartet 4 Radius-Chips"); } },
        { id: "osm-exists-grid", group: "Vorhandensein+", name: "Kachel-Grid", fn: function (U) { U.assertEl("grid"); } },
        { id: "osm-exists-mehr", group: "Vorhandensein+", name: "Mehr-laden-Knopf", fn: function (U) { U.assertEl("mehr-laden"); } },
        { id: "osm-exists-treffer", group: "Vorhandensein+", name: "Treffer-Anzeige", fn: function (U) { U.assertEl("treffer"); } },
        { id: "osm-exists-spin", group: "Vorhandensein+", name: "Spin/Zufalls-Auswahl", fn: function (U) { U.assertEl("spin-auswahl"); } },
        { id: "osm-exists-umkreis-reset", group: "Vorhandensein+", name: "Umkreis-Reset", fn: function (U) { U.assertEl("umkreis-reset"); } },

        // --- Steuerung (Tiefe) ---
        { id: "osm-bahn-toggle", group: "Steuerung+", name: "Bahnhof-Filter togglet",
          fn: async function (U) { var c = U.assertEl("bahn-an"); var v = c.checked; c.click(); U.assert(await U.waitFor(function () { return c.checked !== v; }, 2500), "Bahnhof-Filter togglet nicht"); },
          cleanup: function (U) { var c = U.el("bahn-an"); if (c && c.checked) c.click(); } },
        { id: "osm-ortsuche-eingabe", group: "Steuerung+", name: "Ortssuche nimmt Eingabe an",
          fn: async function (U) { var f = U.assertEl("ort-suche"); f.value = "Potsdam"; f.dispatchEvent(new Event("input", { bubbles: true })); await U.sleep(150); U.assert(f.value === "Potsdam", "Ortssuche übernahm Eingabe nicht"); },
          cleanup: function (U) { var f = U.el("ort-suche"); if (f) f.value = ""; } },
        { id: "osm-radius-chip", group: "Steuerung+", name: "Radius-Chip wählbar",
          fn: async function (U) { var cb = U.el("umkreis-an"); if (cb && !cb.checked) cb.click(); var b = U.q('#radius-chips button[data-km="30"]'); U.assert(b, "30-km-Chip fehlt"); b.click(); await U.sleep(250); U.assert(U.$("radius-wert").textContent.indexOf("30") >= 0, "Radius-Wert nicht 30"); },
          cleanup: function (U) { var cb = U.el("umkreis-an"); if (cb && cb.checked) cb.click(); } },
        { id: "osm-grid-kacheln", group: "Steuerung+", name: "Kacheln werden geladen",
          fn: async function (U) { U.assert(await U.waitFor(function () { return U.qa("#grid .tour-visual").length > 0; }, 9000), "Keine Kacheln geladen"); } },
        { id: "osm-mehr-laden", group: "Steuerung+", name: "„Mehr laden\" lädt weitere Kacheln",
          fn: async function (U) {
              await U.waitFor(function () { return U.qa("#grid .tour-visual").length > 0; }, 9000);
              var b = U.el("mehr-laden");
              if (b && !b.hidden) { var vor = U.qa("#grid .tour-visual").length; U.click(b); U.assert(await U.waitFor(function () { return U.qa("#grid .tour-visual").length > vor; }, 4000), "Lädt nicht nach"); }
          } },
        { id: "osm-dialog-aktionen", group: "Steuerung+", name: "Tour-Dialog: GPX-/Navi-Link",
          fn: async function (U) {
              U.assert(await U.waitFor(function () { return !!U.q(".tour-visual"); }, 9000), "Keine Tour-Kachel");
              U.q(".tour-visual").click();
              U.assert(await U.waitFor(function () { return !!U.q(".tdlg-links"); }, 6000), "Dialog ohne Aktionen");
              // GPX-Link kommt aus details.json (lazy) → darauf warten
              U.assert(await U.waitFor(function () { return U.qa('.tdlg-links a[href*=".gpx"]').length >= 1; }, 8000), "Kein GPX-Download-Link");
          },
          cleanup: function (U) { var c = U.q(".tdlg-close"); if (c) c.click(); } }
    ];

    // ============================== NAVITOUR (Tour-Modus) ===================
    var navitour = [
        { id: "tour-exists-karte", group: "Vorhandensein", name: "Kartencontainer", fn: function (U) { U.assertEl("navi-karte"); } },
        { id: "tour-modus", group: "Vorhandensein", name: "Seite ist im Tour-Modus",
          fn: function (U) { U.assert(U.assertEl("navi").dataset.modus === "tour", "data-modus ist nicht 'tour'"); } },
        { id: "tour-exists-zentrieren", group: "Vorhandensein", name: "Zentrieren/Kompass", fn: function (U) { U.assertEl("nv-zentrieren"); } },
        { id: "tour-exists-ton", group: "Vorhandensein", name: "Ton-Knopf", fn: function (U) { U.assertEl("nv-ton"); } },
        { id: "tour-exists-voll", group: "Vorhandensein", name: "Vollbild-Knopf", fn: function (U) { U.assertEl("nv-voll"); } },
        { id: "tour-exists-los", group: "Vorhandensein", name: "Navigation-starten-Knopf", fn: function (U) { U.assertEl("nv-los"); } },
        // Tour-spezifische Funktionen (nur im Tour-Modus vorhanden)
        { id: "tour-exists-anfahrt", group: "Tour-Funktionen", name: "Anfahrt-Bereich (nv-anfahrt)", fn: function (U) { U.assertEl("nv-anfahrt"); } },
        { id: "tour-exists-zum-start", group: "Tour-Funktionen", name: "„Zum Startpunkt\" (nv-zum-start)", fn: function (U) { U.assertEl("nv-zum-start"); } },
        { id: "tour-exists-zur-route", group: "Tour-Funktionen", name: "„Zum nächsten Punkt\" (nv-zur-route)", fn: function (U) { U.assertEl("nv-zur-route"); } },
        { id: "tour-exists-umkehr", group: "Tour-Funktionen", name: "Umkehr-Knopf (nv-umkehr)",
          fn: async function (U) { await sheetAuf(U); U.assertEl("nv-umkehr"); },
          cleanup: function (U) { var s = U.el("nv-settings"); if (s) s.hidden = true; } },

        // --- Tour-Modus Tiefe: Anzeige + echte Manöver (trace) ---
        { id: "tour-exists-pfeil", group: "Tour-Funktionen", name: "Abbiege-Pfeil (nv-pfeil)", fn: function (U) { U.assertEl("nv-pfeil"); } },
        { id: "tour-exists-hoehe", group: "Tour-Funktionen", name: "Höhenprofil-Container", fn: function (U) { U.assertEl("nv-hoehe"); } },
        { id: "tour-exists-ankunft", group: "Tour-Funktionen", name: "Ankunftszeit (Panel)", fn: function (U) { U.assertEl("nv-ankunft"); } },
        { id: "tour-manoever", group: "Tour-Funktionen", name: "Tour-Manöver (trace) erscheinen",
          fn: async function (U) {
              var los = U.assertEl("nv-los");
              if (/starten/i.test(los.textContent)) { U.click(los); await U.waitFor(function () { return /beenden/i.test(los.textContent); }, 15000); }
              U.assert(await U.waitFor(function () { var t = U.el("nv-text"); return t && t.textContent.trim().length > 0; }, 15000), "Keine Tour-Abbiegeanweisung (trace)");
          },
          cleanup: async function (U) { var l = U.el("nv-los"); if (l && /beenden/i.test(l.textContent)) { U.click(l); await U.sleep(300); } } }
    ];

    SPIN.testcases = { navi: navi, osm: osm, navitour: navitour };
})();
