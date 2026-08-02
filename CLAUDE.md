# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Was das ist

**WalkHop** – native **.NET 10 MAUI**-App (Android · iOS · Windows) für Wander-Navigation:
Touren entdecken, Karte, echtes turn-by-turn-Routing, GPS/Kompass, Offline-Karten,
Live-Gruppen-Standort. Backend ist der Django-Server unter `AppConfig.ApiBase`
(`https://spin1more.com`; Routing `/navi/*`, Touren/Geocode `/ausfluege/*`, Konto `/api/*`).

> ⚠️ **`README.md` ist veraltet.** Es beschreibt eine frühere **WebView-Hybrid**-Hülle
> („SpinNaviApp", MapLibre/Leaflet im WebView). Die App ist inzwischen **komplett nativ**
> (Mapsui, eigene Navigations-Logik in C#). Für Architektur diesem CLAUDE.md folgen, nicht dem README.
> Der WebView lebt nur noch als **Debug-only Selbsttest** (`TestPage`, im Release ausgebaut).

Same-Codebase-Multibrand: `AppConfig.Marke` + `ApplicationTitle` setzen den Markennamen
(Default „WalkHop"). Die **Bundle-/App-ID `com.companyname.spinnaviapp` NICHT ändern**
(hängt an Signing, Provisioning, Appium-`resource-id`-Prefix).

## Repo-Layout

```
App/
├─ WalkHop/            # die MAUI-App (dieses ist der Kern)
├─ WalkHop.Tests/      # xUnit-Logiktests (net10.0, MAUI-frei) – normales `dotnet test`
├─ WalkHop.UITests/    # Appium/UiAutomator2 – nur mit Android-Emulator, separat
├─ tests/              # GETEILTE JS-Test-Cases (testcases.js) für den Debug-WebView-Selbsttest
├─ tools/              # android-emulator-setup.ps1
├─ sign/               # Signing-Material (iOS)
└─ .github/workflows/ios.yml   # iOS-Build → TestFlight (nur manuell)
```
`A:\WalkHop\App` ist ein **eigenes Git-Repo** (getrennt vom Django-Backend in `../djangoCode`,
das seine eigene CLAUDE.md hat). Es gibt **keine `.sln`** – pro Projekt bauen.

## Bauen · Laufen · Testen

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1

# Schnellster Kompilier-/Smoke-Check auf diesem PC:
dotnet build WalkHop/WalkHop.csproj -f net10.0-windows10.0.19041.0

# Android auf laufendem Emulator/Gerät installieren & starten:
dotnet build WalkHop/WalkHop.csproj -f net10.0-android -t:Run

# Logik-Tests (xUnit):
dotnet test WalkHop.Tests/WalkHop.Tests.csproj
# einzelner Test / Klasse:
dotnet test WalkHop.Tests/WalkHop.Tests.csproj --filter "FullyQualifiedName~GeocodeDetailTests"
```

- **iOS ist auf Windows NICHT baubar.** iOS-Builds laufen ausschließlich über
  `.github/workflows/ios.yml` (**`workflow_dispatch`, rein manuell** → signiert → TestFlight).
  Build-Nummer = `github.run_number` (kein Handbump), Anzeige-Version =
  `ApplicationDisplayVersion` aus der `.csproj`. Apple-Team `E7F22WZ26P`. Das .NET-iOS-SDK
  verlangt periodisch eine neuere Xcode-Version → dann `xcode-version` in `ios.yml` nachziehen.
- **`WalkHop.UITests` läuft NICHT im normalen `dotnet test`** – braucht Emulator + laufenden
  Appium-Server + vorab erteilte Standort-Permission. Genau **ein App-Neustart je Fixture**
  (im `[OneTimeSetUp]`), danach nur prüfen. `AutomationId` → Android-`resource-id`
  (`com.companyname.spinnaviapp:id/<id>`), angesprochen über `ResId(...)`.

## Architektur (Big Picture)

### Seiten sind Partial-Klassen, aufgeteilt nach Thema
Jede große Seite ist **eine `partial class` über mehrere Dateien** (nach Zuständigkeit, je
Datei ~200–300 Zeilen). Wer eine Seite versteht, muss alle ihre Teildateien kennen:
- **`MainPage`** = die **Navigations-/Turn-by-turn-Seite**: `MainPage.xaml.cs` (Felder,
  Lifecycle, Karten-Setup), `.Gps.cs` (Positions-Schleife, Kompass, Zeichnen),
  `.Navigation.cs` (Route/Manöver/Ansagen/Reroute), `.Karte.cs` (Layer, Zentrieren, Zoom-Glättung),
  `.Vorschlaege.cs`, `.Gruppe.cs`.
- **`UebersichtPage`** = die **Start-/Übersichtskarte** (Touren entdecken, Umkreis/Filter/Suche):
  `.xaml.cs`, `.Karte.cs`, `.Touren.cs`, `.Dialog.cs`, `.Foto.cs`, `.Gruppe.cs`.
- **`EinstellungenPage`** = Tab-Seite (Allgemein/Navigation/Karte/Gruppe/Anmeldung):
  `.xaml.cs` + `.Gruppe.cs`. Tabs sind gemalte `Border`+`Label`-Segmente (kein natives TabView),
  Umschaltung über `TabWechseln(...)`.

Shell-Routing über `AppShell` (`Shell.Current.GoToAsync("//navigation")`, `"//konto"` …).
Seitenübergänge (nicht App-Backgrounding) lösen `OnAppearing`/`OnDisappearing` aus – dort
werden Sensoren/Polling gestartet/gestoppt.

### Zwei native Mapsui-Karten mit gemeinsamem Helfer
Beide Kartenseiten nutzen **Mapsui.Maui** (GPU/SkiaSharp) und teilen sich Zeichen-Logik in
**`Services/KarteHelfer.cs`** (Positions-Beam, Gruppen-Marker, „nächste Route"-Treffer).
Muster, die auf beiden Seiten identisch auftreten:
- **Live-Position ohne 50-m-Distanzfilter:** eine `while`-Schleife ruft dauernd
  `Geolocation.GetLocationAsync(Medium)` statt Foreground-Listening.
- **Folgen vs. manuelle Geste:** `_folgen` zentriert die Karte je Fix; **eine manuelle
  Zoom-/Schwenk-Geste löst `_folgen`** (sonst „springt" die Karte zurück). Guard `KameraFrei`
  unterdrückt programmatische Kamerabewegung während/kurz nach Touch.
- **Kompass ist ein prozessweiter Singleton** (`Compass.Default`). Beim iOS-Seitenwechsel kann
  die verlassene Seite ihn stoppen, *nachdem* die neue ihn startete → `KompassSicherstellen()`
  heilt das bei jedem Fix selbst. Ohne Kompass-HW (z. B. Doogee N55) dreht die Karte nur nach GPS-Kurs.

### Services-Schicht (`Services/`) – bewusst MAUI-arm
Netzwerk/Parsing/Logik liegen in `Services/`. **`Meldung.cs` ist MAUI-frei** (nur Delegaten),
damit Services ohne MAUI-Abhängigkeit loggen/melden können – **das** erlaubt dem Test-Projekt,
einzelne Service-Dateien direkt einzulinken. Zentrale Services u. a.: `RouteService`, `NaviLogik`,
`NavGeo`, `TourService`, `GeocodeService`, `GruppeLive`/`GruppeService`, `OfflineKarte`/`OfflineManager`,
`Protokoll`, `GpsSpeicher`/`DauerGps`, `Auth`, `MapQuellen`.

### Live-Gruppe: eine Quelle der Wahrheit
`GruppeLive` (statisch) hält Code/Namen (in `Einst.GruppenCode`), pollt Mitglieder und sendet
die eigene Position gedrosselt; Seiten abonnieren `Mitglieder`/`Geaendert`. Bedienung sitzt in
**Einstellungen → Tab „Gruppe"** (kein Karten-Knopf). Deep-Link `walkhop://g/<code>` → `DeepLink.cs`
→ `GruppeLive.Beitreten`. (MainPage hat aus historischen Gründen noch eigenes Polling in
`MainPage.Gruppe.cs`, das seinen Code in `OnAppearing` mit `Einst.GruppenCode` abgleicht.)

### Plattform-Spezifika
`Platforms/{Android,iOS,Windows}/` + `#if IOS`/`#if ANDROID` in geteiltem Code.
- **iOS-Hintergrund-GPS:** MAUI-`Geolocation` setzt `AllowsBackgroundLocationUpdates` NICHT →
  ohne `UIBackgroundModes=location` (Info.plist) + eigenen `CLLocationManager`
  (`Platforms/iOS/HintergrundStandort.cs`) suspendiert iOS die App im Hintergrund
  (= „Navigation bricht ab"). App-weit gestartet über `DauerGps` (in `App.xaml.cs`).

### Lokalisierung (`Lokalisierung/`)
`Texte.cs` = de-/en-Wörterbücher, im XAML via `{loc:Translate key}` (`TranslateExtension`),
im Code `L.T("key", args…)`; Laufzeitwechsel über `Lokalisierung.Instanz` + `L.Geaendert`.

### Persistenz & Diagnose
- **`Einst.cs`** – App-Einstellungen (Preferences-basiert), eine Property je Wert.
- **`Protokoll.cs`** – festplattenpersistentes Diagnose-Protokoll; fängt
  `AppDomain.UnhandledException`/`TaskScheduler.UnobservedTaskException` und **schickt bei
  Fehlern automatisch an den Server** (Debounce/Ratelimit). Abruf: `/opt/walkhop/logs/applogs/`.

## Konventionen & Fallen (repo-spezifisch)

- **`WalkHop.Tests.csproj` globt NICHT**, sondern linkt einzelne Service-Dateien per
  `<Compile Include="..\WalkHop\Services\X.cs" />` ein (nur MAUI-freie Klassen).
  Neue MAUI-gekoppelte Dateien sind dadurch sicher ausgeschlossen; **einen neuen Service unit-testen
  = ihn dort ergänzen** (und darauf achten, dass er MAUI-frei bleibt).
- **`WalkHop.csproj` (App) globt** (`EnableDefaultCompileItems`) – neue `.cs`/`.xaml` werden
  automatisch kompiliert; nur `TestPage`/`SpinWebChromeClient` sind im Release per `Compile Remove` raus.
- **`ApplicationDisplayVersion`** soll der Django-Server-`VERSION` entsprechen; **`ApplicationVersion`
  (Build-Nr.) NICHT von Hand hochzählen** – CI setzt sie auf `github.run_number`.
- **Neue XAML-`x:Name`-Elemente** erzeugen Backing-Felder; ein entferntes `x:Name` bricht alle
  Code-Referenzen darauf – beim Umbau immer beide Seiten (XAML + Partials) mitziehen.
- Globale Regeln (Deutsch, echte Umlaute in User-Strings/Commits, kein Commit/Push/Build ohne
  ausdrückliche Ansage, iOS-/TestFlight-Build nie proaktiv) gelten wie in `~/.claude/CLAUDE.md`.
