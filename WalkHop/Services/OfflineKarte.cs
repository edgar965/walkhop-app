using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Microsoft.Maui.Storage;

namespace WalkHop;

/// <summary>
/// Offline-Karten: Die OSM-Kachelquelle bekommt einen persistenten Datei-Cache.
/// Angeschaute Kacheln landen automatisch auf der Platte und sind offline verfügbar.
/// „Herunterladen" lädt zusätzlich die ganze sichtbare Region (Zoomstufen) vor.
/// </summary>
public static class OfflineKarte
{
    public static string CacheDir { get; } =
        Path.Combine(FileSystem.AppDataDirectory, "kartencache");

    private static FileCache? _cache;

    /// <summary>OSM-Kachelquelle MIT persistentem Datei-Cache (online lädt + speichert,
    /// offline bedient sie aus dem Cache).</summary>
    public static HttpTileSource Quelle()
    {
        Directory.CreateDirectory(CacheDir);
        _cache ??= new FileCache(CacheDir, "png");
        return KnownTileSources.Create(KnownTileSource.OpenStreetMap,
            persistentCache: _cache,
            userAgent: "Spin1More/1.0 (+https://spin1more.com)");
    }

    /// <summary>Lädt alle Kacheln der Region (Mercator-Rechteck) in den Stufen
    /// minZoom..maxZoom in den Cache (für Offline-Nutzung). Begrenzt auf maxKacheln.</summary>
    public static async Task<int> DownloadAsync(HttpTileSource quelle, MRect bereich,
        int minZoom, int maxZoom, int maxKacheln, IProgress<(int done, int total)>? prog = null)
    {
        var extent = new Extent(bereich.MinX, bereich.MinY, bereich.MaxX, bereich.MaxY);

        // Schon beim Sammeln bei maxKacheln abbrechen, damit nicht erst riesige
        // Listen aufgebaut und dann gekappt werden (Speicherspitze).
        var infos = new List<TileInfo>();
        for (int z = minZoom; z <= maxZoom && infos.Count < maxKacheln; z++)
            foreach (var ti in quelle.Schema.GetTileInfos(extent, z))
            {
                infos.Add(ti);
                if (infos.Count >= maxKacheln) break;
            }

        int total = infos.Count, done = 0, ok = 0, fehler = 0;
        foreach (var info in infos)
        {
            try { await quelle.GetTileAsync(info); ok++; }
            catch (Exception ex)
            {
                // Einzelne Kachel darf fehlen – aber nicht stumm verschlucken: protokollieren.
                fehler++;
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineKarte] Kachel fehlgeschlagen (z{info.Index.Level} x{info.Index.Col} y{info.Index.Row}): {ex.Message}");
            }
            prog?.Report((++done, total));
        }
        // Totalausfall erkennbar machen: Wurden Kacheln angefordert, aber KEINE geladen
        // (z. B. komplett offline), ist das KEIN Erfolg – auch wenn die Rückgabe (0) das
        // nahelegen könnte. Mindestens deutlich loggen (Signatur bleibt unverändert).
        if (total > 0 && ok == 0)
            System.Diagnostics.Debug.WriteLine(
                $"[OfflineKarte] TOTALAUSFALL: 0 von {total} Kacheln geladen (alle {fehler} fehlgeschlagen – offline?).");
        else if (fehler > 0)
            System.Diagnostics.Debug.WriteLine(
                $"[OfflineKarte] {ok} von {total} Kacheln geladen, {fehler} fehlgeschlagen.");
        return ok;   // tatsächlich gespeicherte Kacheln (nicht nur versuchte)
    }

    // ---- Schätzung (für die Anzeige VOR dem Download) ----------------------
    // Mittlere Kachelgröße (Bytes) zur groben Größen-Schätzung. OSM-Raster ~15–25 KB,
    // OpenTopoMap schwerer – konservativ ~22 KB ansetzen.
    public const long KachelBytesSchaetzung = 22 * 1024;

    private static int XKachel(double lon, int z) =>
        (int)Math.Floor((lon + 180.0) / 360.0 * (1L << z));

    private static int YKachel(double lat, int z)
    {
        double r = lat * Math.PI / 180.0;
        double y = (1 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1L << z);
        return (int)Math.Floor(y);
    }

    /// <summary>Anzahl OSM-Kacheln für eine Geo-bbox über die Zoomstufen minZoom..maxZoom
    /// (für die Größen-Schätzung vor dem Laden). Bricht bei <paramref name="deckel"/> ab.</summary>
    public static int KachelAnzahl(double minLon, double minLat, double maxLon, double maxLat,
        int minZoom, int maxZoom, int deckel = int.MaxValue)
    {
        long summe = 0;
        for (int z = minZoom; z <= maxZoom; z++)
        {
            int x0 = XKachel(minLon, z), x1 = XKachel(maxLon, z);
            int y0 = YKachel(maxLat, z), y1 = YKachel(minLat, z);   // maxLat = oben (kleineres y)
            long nx = Math.Max(0, x1 - x0 + 1), ny = Math.Max(0, y1 - y0 + 1);
            summe += nx * ny;
            if (summe >= deckel) return deckel;
        }
        return (int)Math.Min(summe, int.MaxValue);
    }
}
