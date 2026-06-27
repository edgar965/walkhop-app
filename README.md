# Spin1More – Mobile App (.NET MAUI)

Native App-Hülle (iOS · Android · Windows) um die bestehende **spin1more-Navigation**
und **OSM-Kontrolle**. App-Name (Launcher): **Spin1More**. Sie liefert den **vollen
Funktionsumfang** ohne native Neuimplementierung, indem sie die getestete Web-Navigation
(MapLibre + Leaflet) in einem WebView hostet – plus native Anbindung für GPS, Kompass
und „Bildschirm an". Projektordner `SpinNaviApp/`, Bundle-ID `com.companyname.spinnaviapp`.

## Warum WebView-Hybrid?

Die Navigation (`navi.js`, ~2000 Zeilen MapLibre-Logik) und die OSM-Kontrolle
(`karte_controls.js` + Leaflet/leaflet-rotate) sind ausgereift und getestet. Eine
native C#-Neuimplementierung wäre Monate Arbeit und würde das Verhalten nicht 1:1
treffen. Der Hybrid-Ansatz gibt **sofort vollen Funktionsumfang** und erlaubt die
geforderte **exakte Test-Korrespondenz**: dieselbe Test-Suite läuft im Browser
**und** im App-WebView (siehe `tests/`).

> Hinweis App-Store: Ein reiner Web-Wrapper kann bei Apple Prüfung erfordern. Falls
> nötig, lässt sich die Hülle später um native Elemente erweitern (z. B. nativer
> Start-/Auswahlscreen) – die Architektur ist dafür offen.

## Projektstruktur

```
A:\spin1more\App\
├─ SpinNaviApp\            # die MAUI-App
│  ├─ AppConfig.cs         # Basis-URL, Start-/Test-URLs (hier umstellen: live ↔ dev)
│  ├─ AppShell.xaml        # untere Tabs: „Navigation" + „Selbsttest"
│  ├─ MainPage.xaml(.cs)   # vollflächiger WebView (Start: /ausfluege/)
│  ├─ TestPage.xaml(.cs)   # MAUI-Test-Suite: injiziert tests/*.js in den WebView
│  ├─ MauiProgram.cs       # Android-WebView: JS/DOM-Storage/Geolocation aktiv
│  └─ Platforms\
│     ├─ Android\          # Standort-Permissions, Geolocation-ChromeClient
│     └─ iOS\Info.plist    # Standort-/Bewegungs-Nutzungstexte
├─ tests\                  # GETEILTE Test-Suite (Single Source of Truth)
│  ├─ testcases.js         # alle Cases (navi + osm)
│  ├─ runner.js            # Runner/Helfer
│  ├─ web\                 # Web-Suite (Konsolen-Bundle + Playwright)
│  └─ README.md            # Details + verifizierte Ergebnisse
└─ tools\
   └─ android-emulator-setup.ps1   # Emulator einrichten + starten (+ App installieren)
```

## Voraussetzungen

- .NET 10 SDK (vorhanden) mit Workloads **android**, **ios**, **maui-windows** (vorhanden).
- Android-SDK (über Visual Studio installiert unter
  `C:\Program Files (x86)\Android\android-sdk`). Emulator + System-Image ergänzt das
  Setup-Skript.
- iOS-Builds benötigen einen Mac (auf Windows nicht baubar) – Code/Plist sind vorbereitet.

## Bauen & Starten

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
cd A:\spin1more\App\SpinNaviApp

# Windows (schnellster Smoke-Test auf diesem PC)
dotnet build -f net10.0-windows10.0.19041.0

# Android – auf laufendem Emulator/Gerät installieren & starten
$sdk = "C:\Program Files (x86)\Android\android-sdk"
dotnet build -f net10.0-android -t:Run -p:AndroidSdkDirectory="$sdk"
```

### Android-Emulator

```powershell
pwsh A:\spin1more\App\tools\android-emulator-setup.ps1            # einrichten + starten
pwsh A:\spin1more\App\tools\android-emulator-setup.ps1 -InstallApp # + App bauen/installieren
```
Das Skript ergänzt das fehlende **Emulator-Paket** und ein **System-Image**
(`android-35;google_apis;x86_64`), legt das AVD `spin1more_avd` an und startet es.
Für brauchbare Geschwindigkeit muss **WHPX / „Windows Hypervisor Platform"** (oder
Hyper-V) aktiviert sein (`emulator -accel-check`).

## Konfiguration (`AppConfig.cs`)

- `BaseUrl` – Standard `https://spin1more.com`. Für lokale Tests auf den Dev-Server
  stellen (z. B. `http://192.168.2.178:8090`) und `UseDevLogin = true`.
- `StartPath` – Startseite (Standard `/ausfluege/`: OSM-Übersicht → Tour → Navigation).

## Tests

Siehe `tests/README.md`. Kurz:
- **Web:** Konsolen-Bundle (`tests/web/bundle.js`) einfügen → `await SPIN.runConsole()`,
  oder Playwright (`cd tests/web; npm install; npm test`).
- **MAUI:** App-Tab **„Selbsttest"** – selbst in Tabs (**Navigation / OSM / Tour**),
  jeder Einzeltest mit **Run**-Knopf + **„Alle ausführen"**.
- **Django:** `/hilfe/tests/` (Staff) – ALLE Tests in Tabs (Unit/Component/Integration/UI/Suiten),
  jeder einzeln ausführbar; der UI-Tab nutzt genau diese Cases.
- Alle nutzen **dieselbe** `tests/testcases.js`. Verifiziert: navi 29/29, osm 18/18, navitour 10/10.

## Bekannte Grenzen / nächste Schritte

- **iOS-Geolocation:** WKWebView gibt `navigator.geolocation` nicht automatisch frei.
  Für iOS ist ein kleiner nativer Bridge (CLLocationManager → in die Seite einspeisen)
  der nächste Schritt; Android funktioniert bereits über den Geolocation-ChromeClient.
- **Foto-Upload** im GPS-Aufnahme-Dialog (Android): der eigene ChromeClient müsste
  `OnShowFileChooser` ergänzen, falls Bild-Anhänge in der App gebraucht werden.
- Offline-Karten/Bundling der Web-Assets ist möglich, aktuell lädt die App die
  (immer aktuelle) Live-Seite.
