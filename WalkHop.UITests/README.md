# SpinNaviApp.UITests – native UI-Tests (Appium/UiAutomator2)

Native Oberflächen-Tests der MAUI-App auf dem Android-Emulator: Flyout (inkl.
„Beenden"), Icon-Leiste der Navigation (ohne Zahnrad), Suche, und alle vier
Tabs der Einstellungen-Seite (Allgemein/Navigation/Karte/Anmeldung) – inklusive
der aus dem alten Karten-Sheet **verschobenen** Steuerungen (Kartenmodus,
Fortbewegung, Wegtyp, Routenoptionen) und der neuen **Auto-Aufnahme**.

> Ergänzt die reine Logik-Suite `SpinNaviApp.Tests` (xUnit, 62 Tests). Diese hier
> prüft die **gerenderte native UI** und braucht daher Emulator + Appium.

## Voraussetzungen (einmalig)

```powershell
npm install -g appium
appium driver install uiautomator2
# ANDROID_HOME / JAVA_HOME müssen gesetzt sein (SDK + JDK 11+).
```

## Lauf (≈ 8 App-Neustarts, ~3 min)

```powershell
# 1) Emulator starten
& "$env:ANDROID_HOME\emulator\emulator.exe" -avd spin1more_avd

# 2) Frische APK bauen + installieren (Tests prüfen die INSTALLIERTE App)
dotnet build ..\SpinNaviApp\SpinNaviApp.csproj -c Release -f net10.0-android -p:AndroidSupportedAbis=x86_64
adb -s emulator-5554 install -r ..\SpinNaviApp\bin\Release\net10.0-android\com.companyname.spinnaviapp-Signed.apk

# 3) Standort-Berechtigung vorab erteilen (sonst verdeckt der Permission-Dialog
#    die Navi-Seite – autoGrantPermissions greift hier nicht zuverlässig):
adb -s emulator-5554 shell pm grant com.companyname.spinnaviapp android.permission.ACCESS_FINE_LOCATION
adb -s emulator-5554 shell pm grant com.companyname.spinnaviapp android.permission.ACCESS_COARSE_LOCATION

# 4) Appium-Server starten (eigene Konsole)
$env:ANDROID_HOME="C:\Program Files (x86)\Android\android-sdk"; appium

# 5) Tests laufen lassen
dotnet test
```

## Architektur (wichtig)

- **`Sitzung.cs`** (`[SetUpFixture]`): EINE Appium-Sitzung für die ganze Suite
  (App nur einmal über die `app`-Capability laden) + Warm-up auf den Hamburger.
- **`AppBasis.cs`**: Locator + Navigation. **`Neustart()`** (TerminateApp+ActivateApp)
  wird **nur einmal je Fixture** im `[OneTimeSetUp]` gerufen → **max. so viele
  App-Neustarts wie Fixtures (8)**, NICHT pro Test. Danach navigieren die Tests
  nicht mehr, sondern prüfen nur. Das hält den Lauf schnell und stabil
  (kein Flyout-Kaskaden-/Mehrdeutigkeits-Problem).
- MAUI bildet `AutomationId` auf Android als **resource-id** ab
  (`com.companyname.spinnaviapp:id/<AutomationId>`) → `ResId(...)`; Texte über
  `textContains` (mit Scroll-Fallback `DaText`).

## Hinweise

- Läuft NICHT im normalen `dotnet test` der Solution mit (separates Projekt,
  braucht Emulator + Appium).
- Bei einem frischen Emulator Schritte 2–3 zwingend; `noReset=true` behält
  danach App + Berechtigungen über die Sitzung.
