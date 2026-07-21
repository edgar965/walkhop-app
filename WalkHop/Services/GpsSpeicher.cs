using System.Globalization;
using System.Text;
using Microsoft.Maui.Storage;

namespace WalkHop;

/// <summary>Fortlaufende, PERSISTENTE GPS-Aufzeichnung: hängt jeden empfangenen Fix an eine Datei im
/// App-Datenverzeichnis an (NICHT System-Temp). Übersteht Abstürze/Hintergrund-Kills – anders als die
/// nur im Arbeitsspeicher gehaltene Breadcrumb-Spur, die bei jedem App-Ende verloren geht. Thread-sicher,
/// nie werfend, mit Größendeckel (die ältere Hälfte wird verworfen). Zahlen mit Invariant-Punkt
/// (maschinenlesbares JSONL), NICHT mit deutschem Komma.</summary>
internal static class GpsSpeicher
{
    private static readonly object _sperre = new();
    private const long MaxBytes = 5 * 1024 * 1024;   // ~5 MB (≈ Tage an 1-Hz-Fixes); darüber die ältere Hälfte verwerfen

    internal static string Pfad => Path.Combine(FileSystem.AppDataDirectory, "gps_track.jsonl");

    /// <summary>Hängt einen Fix als JSONL-Zeile an (Zeit UTC, lat, lon, Genauigkeit m). Nie werfend.</summary>
    internal static void Speichere(double lat, double lon, double genauigkeit, DateTimeOffset zeit)
    {
        try
        {
            var ci = CultureInfo.InvariantCulture;
            string zeile = $"{{\"t\":\"{zeit.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\",\"lat\":{lat.ToString(ci)},\"lon\":{lon.ToString(ci)},\"acc\":{Math.Round(genauigkeit).ToString(ci)}}}\n";
            bool ueberlauf;
            lock (_sperre)
            {
                File.AppendAllText(Pfad, zeile, Encoding.UTF8);
                ueberlauf = new FileInfo(Pfad).Length > MaxBytes;
            }
            if (ueberlauf) Task.Run(Rotiere);   // Kürzen nicht auf dem (evtl. UI-)Thread
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    private static void Rotiere()
    {
        try
        {
            lock (_sperre)
            {
                var fi = new FileInfo(Pfad);
                if (!fi.Exists || fi.Length <= MaxBytes) return;
                var alle = File.ReadAllLines(Pfad);
                File.WriteAllLines(Pfad, alle.Skip(alle.Length / 2));   // auf die jüngere Hälfte kürzen
            }
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    internal static long Groesse()
    {
        try { var fi = new FileInfo(Pfad); return fi.Exists ? fi.Length : 0; }
        catch { return 0; }
    }
}
