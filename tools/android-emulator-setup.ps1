<#
.SYNOPSIS
    Richtet einen Android-Emulator für die spin1more-App ein und startet ihn –
    optional baut und installiert es auch gleich die App.

.BESCHREIBUNG
    Der Android-SDK ist über Visual Studio bereits installiert, ABER ohne
    Emulator-Paket und System-Image. Dieses Skript ergänzt beides via sdkmanager,
    legt ein AVD an und startet den Emulator. Hardware-Beschleunigung (WHPX /
    „Windows Hypervisor Platform" bzw. Hyper-V) sollte aktiviert sein – sonst
    läuft der Emulator sehr langsam.

.BEISPIEL
    pwsh A:\spin1more\App\tools\android-emulator-setup.ps1
    pwsh A:\spin1more\App\tools\android-emulator-setup.ps1 -InstallApp
#>
param(
    [int]$ApiLevel = 35,
    [string]$Tag = "google_apis",       # google_apis = mit Google-APIs (Standort/Play-Services light)
    [string]$Abi = "x86_64",
    [string]$Device = "pixel_6",
    [string]$AvdName = "spin1more_avd",
    [switch]$InstallApp                  # zusätzlich APK bauen + installieren + starten
)

$ErrorActionPreference = "Stop"

# --- SDK + JDK finden ---------------------------------------------------------
$sdk = $env:ANDROID_HOME
if (-not $sdk -or -not (Test-Path $sdk)) { $sdk = "$env:ProgramFiles\Android\android-sdk" }
if (-not (Test-Path $sdk)) { $sdk = "${env:ProgramFiles(x86)}\Android\android-sdk" }
if (-not (Test-Path $sdk)) { $sdk = "$env:LOCALAPPDATA\Android\Sdk" }
if (-not (Test-Path $sdk)) { throw "Android-SDK nicht gefunden. ANDROID_HOME setzen." }
$env:ANDROID_HOME = $sdk
$env:ANDROID_SDK_ROOT = $sdk
Write-Host "Android-SDK: $sdk"

# Die cmdline-tools (sdkmanager/avdmanager) brauchen JDK 17+. Der .NET-Workload bringt
# nur JDK 11 mit → bei Bedarf ein portables JDK 17 nach tools\jdk17 holen.
function Get-JavaMajor([string]$javaHome) {
    $exe = Join-Path $javaHome 'bin\java.exe'
    if (-not (Test-Path $exe)) { return 0 }
    $v = (& $exe -version 2>&1)[0]
    if ($v -match '"(\d+)') { return [int]$Matches[1] }
    return 0
}
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$portableJdk = Join-Path $toolsDir 'jdk17'
$jdkCandidates = @($env:JAVA_HOME, $portableJdk) +
    (Get-ChildItem "$env:ProgramFiles\Microsoft\jdk-*","$env:ProgramFiles\Eclipse Adoptium\*" -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
$java17 = $null
foreach ($c in $jdkCandidates) { if ($c -and (Get-JavaMajor $c) -ge 17) { $java17 = $c; break } }
if (-not $java17) {
    Write-Host "Kein JDK 17+ gefunden – lade portables JDK 17 (Adoptium) nach $portableJdk …" -ForegroundColor Yellow
    $zip = Join-Path $env:TEMP 'jdk17.zip'
    Invoke-WebRequest "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk" `
        -OutFile $zip -MaximumRedirection 5 -UseBasicParsing
    $tmp = Join-Path $env:TEMP 'jdk17x'; if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip $tmp -Force
    if (Test-Path $portableJdk) { Remove-Item $portableJdk -Recurse -Force }
    Move-Item (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName $portableJdk
    Remove-Item $zip,$tmp -Recurse -Force -ErrorAction SilentlyContinue
    $java17 = $portableJdk
}
$env:JAVA_HOME = $java17
$env:PATH = "$java17\bin;$env:PATH"
Write-Host "JAVA_HOME: $env:JAVA_HOME  (Java $(Get-JavaMajor $java17))"

function Find-Tool([string]$name) {
    $hit = Get-ChildItem $sdk -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}
$sdkmanager = Find-Tool "sdkmanager.bat"
$avdmanager = Find-Tool "avdmanager.bat"
if (-not $sdkmanager) { throw "sdkmanager.bat nicht gefunden (cmdline-tools fehlen)." }
Write-Host "sdkmanager: $sdkmanager"

$image = "system-images;android-$ApiLevel;$Tag;$Abi"
$platform = "platforms;android-$ApiLevel"

# --- Pakete installieren (idempotent) ----------------------------------------
Write-Host "`n>>> Installiere Emulator + System-Image ($image) …" -ForegroundColor Cyan
# Lizenzen automatisch akzeptieren
"y`ny`ny`ny`ny`ny`ny`n" | & $sdkmanager --licenses | Out-Null
& $sdkmanager "emulator" "platform-tools" $platform $image
if ($LASTEXITCODE -ne 0) { throw "sdkmanager-Installation fehlgeschlagen." }

# --- AVD anlegen --------------------------------------------------------------
$emulator = Join-Path $sdk "emulator\emulator.exe"
$avdList = & $emulator -list-avds 2>$null
if ($avdList -contains $AvdName) {
    Write-Host "AVD '$AvdName' existiert bereits."
} else {
    Write-Host "`n>>> Lege AVD '$AvdName' an …" -ForegroundColor Cyan
    "no" | & $avdmanager create avd -n $AvdName -k $image --device $Device --force
    if ($LASTEXITCODE -ne 0) { throw "AVD-Erstellung fehlgeschlagen." }
}

# Hardware-Tastatur aktivieren – sonst tippt die PC-Tastatur NICHT in WebView-Felder
# (Standard mancher AVDs ist hw.keyboard=no). Greift nach einem Kaltstart.
$avdCfg = Join-Path $env:USERPROFILE ".android\avd\$AvdName.avd\config.ini"
if (Test-Path $avdCfg) {
    $c = Get-Content $avdCfg
    if ($c -match '^hw\.keyboard\s*=') { $c = $c -replace '^hw\.keyboard\s*=.*', 'hw.keyboard = yes' }
    else { $c += 'hw.keyboard = yes' }
    $c | Set-Content $avdCfg -Encoding ascii
    Write-Host "hw.keyboard = yes gesetzt (PC-Tastatur aktiv)."
}

# --- Beschleunigung prüfen ----------------------------------------------------
Write-Host "`n>>> Hardware-Beschleunigung:" -ForegroundColor Cyan
& $emulator -accel-check

# --- Emulator starten ---------------------------------------------------------
Write-Host "`n>>> Starte Emulator '$AvdName' (eigenes Fenster) …" -ForegroundColor Cyan
Start-Process -FilePath $emulator -ArgumentList "-avd", $AvdName, "-netdelay", "none", "-netspeed", "full"

# Auf „boot completed" warten
$adb = Join-Path $sdk "platform-tools\adb.exe"
Write-Host "Warte auf Geräte-Boot (max. 180 s) …"
& $adb wait-for-device
$booted = $false
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Seconds 2
    $b = (& $adb shell getprop sys.boot_completed 2>$null) -replace '\s',''
    if ($b -eq "1") { $booted = $true; break }
}
Write-Host ($booted ? "Emulator ist gestartet." : "Emulator bootet noch – ggf. kurz warten.")

# --- Optional: App bauen + installieren + starten -----------------------------
if ($InstallApp) {
    Write-Host "`n>>> Baue + installiere App auf dem Emulator …" -ForegroundColor Cyan
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
    $proj = "A:\spin1more\App\SpinNaviApp\SpinNaviApp.csproj"
    dotnet build $proj -f net10.0-android -t:Run -p:AndroidSdkDirectory="$sdk"
}

Write-Host "`nFertig. App manuell installieren/starten:" -ForegroundColor Green
Write-Host "  dotnet build A:\spin1more\App\SpinNaviApp\SpinNaviApp.csproj -f net10.0-android -t:Run -p:AndroidSdkDirectory=`"$sdk`""
