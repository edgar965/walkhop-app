# spin1more – Test-Suite (Web **und** MAUI, eine Quelle)

Diese Suite prüft **alle Buttons und Funktionen** der bestehenden Web-App und
läuft – mit **denselben Test-Cases** – sowohl im Browser als auch in der
.NET-MAUI-App. Damit gibt es die vom Auftrag geforderte *„genau eine
Korrespondenz der Test-Cases"*.

## Single Source of Truth

| Datei            | Inhalt                                                                 |
|------------------|------------------------------------------------------------------------|
| `testcases.js`   | **Alle** Test-Cases: `navi` (29) · `osm` (18) · `navitour` (10) = **57**. |
| `runner.js`      | Runner + Helfer; `run(group)` / `runOne(group,id)`, liefert JSON.       |

Dieselben Dateien werden **unverändert** von DREI Stellen geladen:

- **MAUI-Suite:** als gebündelte Assets (`MauiAsset` in `SpinNaviApp.csproj`,
  `LogicalName=tests/…`) → die Selbsttest-Seite injiziert sie in den App-WebView.
- **Web-Suite:** per Konsole-Bundle oder Playwright (siehe unten).
- **Django Hilfe → Tests (UI-Tab):** `static/tests/{runner,testcases}.js`
  (von `build-bundle.ps1` aus dieser Quelle synchronisiert) – läuft client-seitig im Iframe.

> Ändert man einen Test, ändert man ihn **einmal** in `testcases.js` – beide
> Suiten erben die Änderung automatisch.

## Was wird getestet?

**Navigation (`navi`, 29 Cases)** – Seite `/navi/ziel/?…&demo=1`
- *Vorhandensein:* Karte, Beenden-✕, Zentrieren/Kompass, Ton, POI-Suche, Vollbild,
  „Navigation starten", Panel-Griff, Einstellungen, Sprache.
- *Karten-Steuerung:* Vollbild-Toggle (`nv-voll-an`), Ton-Toggle, POI-Suchpanel.
- *POI-Suche:* Suchfeld + Suchen/In-der-Nähe/Leeren.
- *Panel:* Ein-/Ausklappen (`minimiert`).
- *Einstellungen:* Sheet auf/zu, **alle 4 Kartenmodi**, Wanderwege, **Fuß/Rad/Auto**,
  Wegtyp (Befestigt/Neutral/Offroad), Vermeidungs-Optionen, Ton-im-Sheet, Sprachwechsel.
- *Navigations-Lebenszyklus (Demo):* Start → lila Richtungspfeil → Mitfahren →
  Nord/Fahrtrichtung-Toggle → Beenden.

**OSM-Übersicht (`osm`, 18 Cases)** – Seite `/ausfluege/` (`#ausfluege-karte`)
- *Vorhandensein:* Karte, rechte Knopfleiste, Zentrieren/Kompass, Vollbild,
  Ebenen-Umschalter, Kamera, Suchfeld, Kategorie-Filter, Standort, Umkreis.
- *Steuerung:* Vollbild-Toggle, Nord/Fahrtrichtung, Ebene→Satellit, Kamera,
  Suche, Kategorie-Filter, Umkreis-Suche, Tour-Detail-Dialog auf/zu.

**Tour-Modus (`navitour`, 10 Cases)** – Seite `/navi/tour/<pk>/?demo=1`
- *Vorhandensein* der Navi-Bedienelemente + Tour-Modus-Erkennung.
- *Tour-Funktionen:* Anfahrt-Bereich, „Zum Startpunkt", „Zum nächsten Punkt", Umkehr.

## Web-Suite ausführen

Die Navigations-Seite erfordert Login. Lokal genügt der **Dev-Login**
(`/dev-login/`, nur `DEBUG`); live vorher normal anmelden.

### A) Schnell – per DevTools-Konsole (kein Setup)
1. `tests/web/bundle.js` neu erzeugen (falls `runner.js`/`testcases.js` geändert):
   ```powershell
   pwsh A:\spin1more\App\tests\build-bundle.ps1
   ```
2. Im Browser die **navi**- oder **ausfluege**-Seite öffnen (eingeloggt).
3. DevTools-Konsole öffnen, **kompletten Inhalt von `tests/web/bundle.js`** einfügen.
4. Ausführen:
   ```js
   await SPIN.runConsole();   // erkennt die Seite automatisch, zeigt eine Tabelle
   ```

### B) Automatisiert – Playwright (CI-tauglich)
```powershell
cd A:\spin1more\App\tests\web
npm install                      # installiert Playwright + Chromium
$env:BASE_URL = "http://192.168.2.178:8090"   # Default; oder https://spin1more.com
npm test                         # läuft navi + osm, Exit-Code 0 = alles grün
```

## MAUI-Suite ausführen

In der App: Tab **„Selbsttest"** → der Tab ist selbst in **Tabs** gegliedert
(**Navigation / OSM-Übersicht / Tour**). Jeder Einzeltest hat einen **Run**-Knopf,
oben **„Alle ausführen"** für die ganze Gruppe. Die App lädt die Zielseite im WebView,
injiziert `runner.js` + `testcases.js` und zeigt Pass/Fail je Test. Siehe `../SpinNaviApp`.

## Django Hilfe → Tests

Unter `/hilfe/tests/` (Staff) liegen ALLE Tests in Tabs: **Unit · Component ·
Integration · UI · Suiten** – jeder Test einzeln mit Run-Knopf. Der **UI**-Tab führt
genau diese 57 Cases client-seitig im Iframe aus (Python-Tests laufen serverseitig).

## Verifiziert

Gegen den Dev-Server (Stand 0.3.18) – im Browser, in der MAUI-App (Emulator) und auf der
Hilfe-Tests-Seite:
- `navi`: **29/29 bestanden**
- `osm`: **18/18 bestanden**
- `navitour`: **10/10 bestanden**
