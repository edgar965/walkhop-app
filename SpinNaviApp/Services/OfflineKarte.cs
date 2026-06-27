using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Microsoft.Maui.Storage;

namespace SpinNaviApp;

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

        int total = infos.Count, done = 0, ok = 0;
        foreach (var info in infos)
        {
            try { await quelle.GetTileAsync(info); ok++; }
            catch { /* einzelne Kachel darf fehlen */ }
            prog?.Report((++done, total));
        }
        return ok;   // tatsächlich gespeicherte Kacheln (nicht nur versuchte)
    }
}
