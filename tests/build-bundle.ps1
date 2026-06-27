# Erzeugt tests/web/bundle.js aus den GETEILTEN Quellen runner.js + testcases.js.
# So bleibt der Konsolen-Bundle driftfrei (Single Source of Truth = die zwei Dateien).
# Aufruf:  pwsh A:\spin1more\App\tests\build-bundle.ps1
$ErrorActionPreference = 'Stop'
$here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$runner = Get-Content (Join-Path $here 'runner.js')    -Raw -Encoding UTF8
$cases  = Get-Content (Join-Path $here 'testcases.js') -Raw -Encoding UTF8
$header = @'
/* AUTO-GENERIERT von build-bundle.ps1 – NICHT direkt bearbeiten!
 * Quelle: tests/runner.js + tests/testcases.js (Single Source of Truth).
 * Verwendung: Inhalt in die DevTools-Konsole der navi-/ausfluege-Seite einfügen,
 * danach:  await SPIN.runConsole();   // erkennt die Seite automatisch
 */
'@
$out = $header + "`n" + $runner + "`n" + $cases + "`n" +
       "console.log('%c spin1more-Tests geladen – jetzt:  await SPIN.runConsole() ', 'background:#0d9488;color:#fff;padding:2px 6px;border-radius:4px');`n"
$dest = Join-Path $here 'web\bundle.js'
Set-Content -Path $dest -Value $out -Encoding UTF8
Write-Host "geschrieben: $dest  ($([math]::Round((Get-Item $dest).Length/1kb,1)) kB)"

# Served-Kopien für die Django-Seite Hilfe→Tests synchronisieren (Single Source = hier).
# App liegt jetzt unter djangoCode\App → static\tests ist zwei Ebenen höher.
$served = Resolve-Path (Join-Path $here '..\..\static\tests') -ErrorAction SilentlyContinue
if ($served) {
    Copy-Item (Join-Path $here 'runner.js')    (Join-Path $served 'runner.js')    -Force
    Copy-Item (Join-Path $here 'testcases.js') (Join-Path $served 'testcases.js') -Force
    Write-Host "synchronisiert nach: $served"
}
