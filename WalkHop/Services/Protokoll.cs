using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace WalkHop;

/// <summary>Persistentes, thread-sicheres Ereignis-/Fehler-Protokoll in einer rotierenden Log-Datei
/// (App-Datenverzeichnis – NICHT System-Temp). Fängt zusätzlich unbehandelte Ausnahmen (Crashes) global
/// ab und schreibt sie noch vor dem Absturz weg. Kann das Protokoll an den Server senden (danach lokal
/// löschen) und lokal löschen. Registriert sich per <see cref="ModuleInitializer"/> selbst bei
/// <see cref="Meldung"/> und den Crash-Handlern – daher bleibt <see cref="Meldung"/> MAUI-frei
/// (diese Datei ist NICHT im Unit-Test-Projekt eingebunden).</summary>
internal static class Protokoll
{
    private static readonly object _sperre = new();
    private const long MaxBytes = 512 * 1024;   // ~0,5 MB; darüber wird die ältere Hälfte verworfen
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };

    internal static string Pfad => Path.Combine(FileSystem.AppDataDirectory, "walkhop.log");

    [ModuleInitializer]
    internal static void Registrieren()
    {
        // Jeder über Meldung gemeldete Fehler landet zusätzlich im Protokoll.
        Meldung.Protokollierer = (kontext, ex) =>
            Schreib("FEHLER", ex == null ? kontext : $"{kontext} — {ex.GetType().Name}: {ex.Message}\n{ex}");
        Meldung.Notierer = Schreib;

        // Unbehandelte Ausnahmen (Crashes) global abfangen und noch vor dem Absturz wegschreiben.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Schreib("CRASH", (e.ExceptionObject as Exception)?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unbekannter Absturz");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        { Schreib("CRASH", "Unbeobachtete Task-Ausnahme:\n" + e.Exception); e.SetObserved(); };

        Schreib("APP", $"Start – {Kopf()}");
    }

    // Kurzer Geräte-/Versions-Kopf für Startmarke + Upload-Metadaten.
    private static string Kopf()
    {
        try
        {
            return $"{AppInfo.Current.PackageName} v{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString}) · " +
                   $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString} · " +
                   $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}";
        }
        catch { return "Gerät unbekannt"; }
    }

    /// <summary>Schreibt eine Log-Zeile (Zeitstempel + Kategorie + Text). Nie werfend.
    /// Das Anhängen bleibt synchron (billig, und der Crash-Handler muss VOR dem Absturz schreiben); nur die
    /// seltene Rotation (ganze Datei umschreiben) läuft nachgelagert, damit sie den UI-Thread nicht blockiert.</summary>
    internal static void Schreib(string kategorie, string text)
    {
        try
        {
            string zeile = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kategorie}] {text}\n";
            bool ueberlauf;
            lock (_sperre)
            {
                File.AppendAllText(Pfad, zeile, Encoding.UTF8);
                ueberlauf = new FileInfo(Pfad).Length > MaxBytes;
            }
            if (ueberlauf) Task.Run(RotiereSicher);   // Rotation nicht auf dem (evtl. UI-)Thread
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }   // Logging darf NIE werfen
    }

    private static void RotiereSicher()
    {
        try { lock (_sperre) RotiereWennNoetig(); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    private static void RotiereWennNoetig()
    {
        try
        {
            var fi = new FileInfo(Pfad);
            if (!fi.Exists || fi.Length <= MaxBytes) return;
            var alle = File.ReadAllLines(Pfad);                 // auf die jüngere Hälfte kürzen
            File.WriteAllLines(Pfad, alle.Skip(alle.Length / 2));
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    internal static string Lies()
    {
        try { lock (_sperre) return File.Exists(Pfad) ? File.ReadAllText(Pfad, Encoding.UTF8) : ""; }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); return ""; }
    }

    internal static void Loesche()
    {
        try { lock (_sperre) { if (File.Exists(Pfad)) File.Delete(Pfad); } }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    internal static long Groesse()
    {
        try { var fi = new FileInfo(Pfad); return fi.Exists ? fi.Length : 0; }
        catch { return 0; }
    }

    /// <summary>Sendet das Protokoll an den Server (/navi/applog.json). Der zu sendende Inhalt wird ATOMAR
    /// (unter Sperre) entnommen (gelesen + Datei geleert) – so gehen zwischenzeitlich angehängte Zeilen
    /// nicht verloren. Schlägt der Upload fehl, wird der entnommene Inhalt WIEDER VORNE eingefügt (kein
    /// Crash-/Diagnose-Log-Verlust). Liefert true bei Erfolg (oder wenn nichts zu senden war).</summary>
    internal static async Task<bool> AnServerSendenAsync()
    {
        string inhalt;
        lock (_sperre)   // atomar entnehmen: lesen UND leeren zusammen
        {
            inhalt = File.Exists(Pfad) ? SafeRead() : "";
            if (!string.IsNullOrWhiteSpace(inhalt)) { try { File.Delete(Pfad); } catch { } }
        }
        if (string.IsNullOrWhiteSpace(inhalt)) return true;   // nichts zu senden
        try
        {
            var rumpf = new
            {
                geraet = Kopf(),
                platform = DeviceInfo.Current.Platform.ToString(),
                version = AppInfo.Current.VersionString,
                build = AppInfo.Current.BuildString,
                email = Auth.Email,
                log = inhalt,
            };
            var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiBase + "/navi/applog.json")
            { Content = new StringContent(JsonSerializer.Serialize(rumpf), Encoding.UTF8, "application/json") };
            var token = Auth.Token;
            if (!string.IsNullOrEmpty(token)) req.Headers.Add("Authorization", "Token " + token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) { WiederVornEinfuegen(inhalt); return false; }
            return true;   // Datei ist bereits geleert
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); WiederVornEinfuegen(inhalt); return false; }
    }

    private static string SafeRead()
    {
        try { return File.ReadAllText(Pfad, Encoding.UTF8); } catch { return ""; }
    }

    // Bei fehlgeschlagenem Upload den entnommenen Inhalt wieder VOR die inzwischen angehängten Zeilen setzen.
    private static void WiederVornEinfuegen(string inhalt)
    {
        try
        {
            lock (_sperre)
            {
                string neu = File.Exists(Pfad) ? SafeRead() : "";
                File.WriteAllText(Pfad, inhalt + neu, Encoding.UTF8);
                RotiereWennNoetig();
            }
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }
}
