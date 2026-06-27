using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace SpinNaviApp;

/// <summary>GPX-Aufnahmen lokal speichern und beim App-Ende auf den Server hochladen.
/// Die .gpx bleibt lokal erhalten; eine .json (für den Upload) wird nach Erfolg gelöscht.</summary>
public static class AufnahmeService
{
    private static string Verz(string sub)
    {
        var d = Path.Combine(FileSystem.AppDataDirectory, "aufnahmen", sub);
        Directory.CreateDirectory(d);
        return d;
    }

    private static string PendingDir => Verz("pending");
    private static string GpxDir => Verz("tracks");

    /// <summary>Speichert die Aufnahme lokal (GPX + Upload-Payload). Gibt den GPX-Pfad zurück.</summary>
    public static string SpeichereLokal(List<(double lat, double lon)> punkte, string name, int dauerSek)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var gpxPfad = Path.Combine(GpxDir, $"track-{stamp}.gpx");
        File.WriteAllText(gpxPfad, Gpx(punkte, name));
        var payload = JsonSerializer.Serialize(new
        {
            name,
            punkte = punkte.Select(p => new[] { p.lat, p.lon }).ToArray(),
            dauer_s = dauerSek,
        });
        File.WriteAllText(Path.Combine(PendingDir, $"track-{stamp}.json"), payload);
        return gpxPfad;
    }

    public static int AnzahlAusstehend()
    {
        try { return Directory.Exists(PendingDir) ? Directory.GetFiles(PendingDir, "*.json").Length : 0; }
        catch (Exception ex) { Debug.WriteLine(ex); return 0; }
    }

    /// <summary>Lädt alle noch nicht synchronisierten Aufnahmen hoch (beim App-Ende / Start).</summary>
    public static async Task<int> UploadeAusstehendAsync()
    {
        if (!Auth.Angemeldet) return 0;
        int n = 0;
        try
        {
            foreach (var f in Directory.GetFiles(PendingDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(f);
                    if (await Auth.SendeAufnahmeAsync(json)) { File.Delete(f); n++; }
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        return n;
    }

    private static string Gpx(List<(double lat, double lon)> punkte, string name)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<gpx version=\"1.1\" creator=\"Spin1More\" xmlns=\"http://www.topografix.com/GPX/1/1\">");
        sb.AppendLine($"<trk><name>{System.Security.SecurityElement.Escape(name)}</name><trkseg>");
        foreach (var (lat, lon) in punkte)
            sb.AppendLine($"<trkpt lat=\"{lat.ToString(CultureInfo.InvariantCulture)}\" lon=\"{lon.ToString(CultureInfo.InvariantCulture)}\" />");
        sb.AppendLine("</trkseg></trk></gpx>");
        return sb.ToString();
    }
}
