using System.Net.Http;
using Microsoft.Maui.Storage;

namespace WalkHop;

/// <summary>Offline-Fotos: lädt die VERKLEINERTE Variante eines verorteten Fotos
/// (/ausfluege/foto/&lt;id&gt;/medium, ~800 px, ~30–90 KB statt ~480 KB) auf die Platte und
/// stellt sie offline als Bildquelle bereit. So sind Sehenswürdigkeiten-Fotos auch ohne Netz da.</summary>
public static class OfflineFotos
{
    // Eigener Unterordner im Karten-Cache, damit „alles löschen" beides mitnimmt.
    public static string Verzeichnis { get; } = Path.Combine(OfflineKarte.CacheDir, "fotos");

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static string Datei(int id) => Path.Combine(Verzeichnis, id + ".jpg");

    /// <summary>Ist das Foto (per Id) offline vorhanden?</summary>
    public static bool Vorhanden(int id) => id > 0 && File.Exists(Datei(id));

    /// <summary>Lokale Bildquelle, falls offline vorhanden – sonst null (dann Online-URL verwenden).</summary>
    public static ImageSource? LokaleQuelle(int id)
        => Vorhanden(id) ? ImageSource.FromFile(Datei(id)) : null;

    /// <summary>Lädt die mittlere Variante eines Fotos in den Offline-Speicher (überspringt Vorhandene).
    /// Liefert die geschriebene Byte-Zahl (0 = übersprungen/fehlgeschlagen).</summary>
    public static async Task<long> LadeAsync(int id)
    {
        if (id <= 0) return 0;
        var ziel = Datei(id);
        if (File.Exists(ziel)) return 0;   // schon da
        try
        {
            Directory.CreateDirectory(Verzeichnis);
            using var resp = await _http.GetAsync(AppConfig.ApiBase + $"/ausfluege/foto/{id}/medium");
            if (!resp.IsSuccessStatusCode) return 0;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return 0;
            var tmp = ziel + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, ziel, overwrite: true);   // atomar: nie halbe Datei sichtbar
            return bytes.Length;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return 0; }
    }

    /// <summary>Lädt mehrere Fotos (per Id) offline. Liefert (Anzahl neu geladen, Summe Bytes).</summary>
    public static async Task<(int anzahl, long bytes)> LadeVieleAsync(
        IEnumerable<int> ids, IProgress<(int done, int total)>? prog = null)
    {
        var liste = ids.Where(i => i > 0).Distinct().ToList();
        int total = liste.Count, done = 0, neu = 0;
        long summe = 0;
        foreach (var id in liste)
        {
            long n = await LadeAsync(id);
            if (n > 0) { neu++; summe += n; }
            prog?.Report((++done, total));
        }
        return (neu, summe);
    }

    /// <summary>Gesamtgröße aller offline gespeicherten Fotos (Bytes).</summary>
    public static long Groesse()
    {
        long b = 0;
        try
        {
            if (Directory.Exists(Verzeichnis))
                foreach (var f in Directory.EnumerateFiles(Verzeichnis, "*.jpg"))
                    b += new FileInfo(f).Length;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Foto-Speichergröße berechnen", ex); }
        return b;
    }

    /// <summary>Alle Offline-Fotos löschen.</summary>
    public static void Leeren()
    {
        try { if (Directory.Exists(Verzeichnis)) Directory.Delete(Verzeichnis, true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Offline-Fotos löschen", ex); }
    }
}
