using BruTile.Web;
using Mapsui;
using Mapsui.Projections;

namespace SpinNaviApp;

/// <summary>Geschätzter Umfang eines Offline-Downloads (für die Anzeige vor dem Laden).</summary>
public record OfflineSchaetzung(int Kacheln, int Fotos, long Bytes);

/// <summary>Ergebnis eines Offline-Downloads.</summary>
public record OfflineErgebnis(bool Ok, int Kacheln, int Fotos, long Bytes, string? Fehler = null);

/// <summary>Orchestriert das Offline-Speichern: lädt die Raster-Kacheln einer Region bzw. einer
/// Tour-Route + die zugehörigen verkleinerten Fotos und trägt das Paket in den Index ein.
/// Schätzungen (Kachel-/Fotozahl, Größe) für die Anzeige VOR dem Download.</summary>
public static class OfflineManager
{
    // Region: großflächig → Übersicht bis Straßenniveau (gedeckelt). Tour: enge bbox → höher zoomen.
    public const int RegionMinZoom = 10, RegionMaxZoom = 14;
    public const int TourMinZoom = 11, TourMaxZoom = 16;
    public const int MaxKachelnRegion = 6000;
    public const int MaxKachelnTour = 4000;
    private const long FotoBytesSchaetzung = 60 * 1024;   // ~60 KB je mittlerem Foto
    private const double TourPuffer = 0.006;              // ~600 m Rand um die Tour-bbox
    private const double FotoNahMeter = 150;              // Fotos ≤ 150 m an der Route gelten als „entlang"

    // ---- Schätzungen ------------------------------------------------------
    public static OfflineSchaetzung SchaetzeRegion(KartenRegion r, List<FotoPunkt> fotos)
    {
        int kacheln = OfflineKarte.KachelAnzahl(r.MinLon, r.MinLat, r.MaxLon, r.MaxLat,
            RegionMinZoom, RegionMaxZoom, MaxKachelnRegion);
        int nFotos = fotos.Count(f => f.Id > 0 && InBbox(f, r.MinLon, r.MinLat, r.MaxLon, r.MaxLat));
        return new OfflineSchaetzung(kacheln, nFotos, Bytes(kacheln, nFotos));
    }

    public static OfflineSchaetzung SchaetzeTour(List<(double lat, double lon)> route, List<FotoPunkt> fotos)
    {
        var (mnLon, mnLat, mxLon, mxLat) = TourBbox(route);
        int kacheln = OfflineKarte.KachelAnzahl(mnLon, mnLat, mxLon, mxLat, TourMinZoom, TourMaxZoom, MaxKachelnTour);
        int nFotos = FotosNahRoute(route, fotos).Count;
        return new OfflineSchaetzung(kacheln, nFotos, Bytes(kacheln, nFotos));
    }

    // ---- Downloads --------------------------------------------------------
    public static async Task<OfflineErgebnis> LadeRegionAsync(KartenRegion r, HttpTileSource quelle,
        List<FotoPunkt> fotos, IProgress<(int done, int total, string phase)>? prog = null)
    {
        try
        {
            var bereich = MercatorBox(r.MinLon, r.MinLat, r.MaxLon, r.MaxLat);
            int kacheln = await OfflineKarte.DownloadAsync(quelle, bereich, RegionMinZoom, RegionMaxZoom,
                MaxKachelnRegion, Phase(prog, "kacheln"));
            var ids = fotos.Where(f => f.Id > 0 && InBbox(f, r.MinLon, r.MinLat, r.MaxLon, r.MaxLat)).Select(f => f.Id);
            var (nFotos, fbytes) = await OfflineFotos.LadeVieleAsync(ids, Phase(prog, "fotos"));
            long bytes = (long)kacheln * OfflineKarte.KachelBytesSchaetzung + fbytes;
            OfflinePakete.Hinzufuegen(new OfflinePaket("region:" + r.Id, r.Name, "region", kacheln, nFotos, bytes, Zeit()));
            return new OfflineErgebnis(true, kacheln, nFotos, bytes);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return new OfflineErgebnis(false, 0, 0, 0, ex.Message); }
    }

    public static async Task<OfflineErgebnis> LadeTourAsync(string tourId, string name,
        List<(double lat, double lon)> route, HttpTileSource quelle, List<FotoPunkt> fotos,
        IProgress<(int done, int total, string phase)>? prog = null)
    {
        try
        {
            var (mnLon, mnLat, mxLon, mxLat) = TourBbox(route);
            var bereich = MercatorBox(mnLon, mnLat, mxLon, mxLat);
            int kacheln = await OfflineKarte.DownloadAsync(quelle, bereich, TourMinZoom, TourMaxZoom,
                MaxKachelnTour, Phase(prog, "kacheln"));
            var ids = FotosNahRoute(route, fotos);
            var (nFotos, fbytes) = await OfflineFotos.LadeVieleAsync(ids, Phase(prog, "fotos"));
            long bytes = (long)kacheln * OfflineKarte.KachelBytesSchaetzung + fbytes;
            OfflinePakete.Hinzufuegen(new OfflinePaket("tour:" + tourId, name, "tour", kacheln, nFotos, bytes, Zeit()));
            return new OfflineErgebnis(true, kacheln, nFotos, bytes);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return new OfflineErgebnis(false, 0, 0, 0, ex.Message); }
    }

    // ---- Helfer -----------------------------------------------------------
    private static long Bytes(int kacheln, int fotos) =>
        (long)kacheln * OfflineKarte.KachelBytesSchaetzung + (long)fotos * FotoBytesSchaetzung;

    private static long Zeit() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static IProgress<(int, int)> Phase(IProgress<(int, int, string)>? prog, string phase) =>
        new Progress<(int done, int total)>(p => prog?.Report((p.done, p.total, phase)));

    private static bool InBbox(FotoPunkt f, double minLon, double minLat, double maxLon, double maxLat) =>
        f.Lat >= minLat && f.Lat <= maxLat && f.Lon >= minLon && f.Lon <= maxLon;

    private static MRect MercatorBox(double minLon, double minLat, double maxLon, double maxLat)
    {
        var (x0, y0) = SphericalMercator.FromLonLat(minLon, minLat);
        var (x1, y1) = SphericalMercator.FromLonLat(maxLon, maxLat);
        return new MRect(x0, y0, x1, y1);
    }

    private static (double minLon, double minLat, double maxLon, double maxLat) TourBbox(
        List<(double lat, double lon)> route)
    {
        double mnLon = 180, mnLat = 90, mxLon = -180, mxLat = -90;
        foreach (var p in route)
        {
            if (p.lon < mnLon) mnLon = p.lon; if (p.lon > mxLon) mxLon = p.lon;
            if (p.lat < mnLat) mnLat = p.lat; if (p.lat > mxLat) mxLat = p.lat;
        }
        return (mnLon - TourPuffer, mnLat - TourPuffer, mxLon + TourPuffer, mxLat + TourPuffer);
    }

    // Foto-Ids, die ≤150 m an der Route liegen (mit grober bbox-Vorfilterung).
    private static List<int> FotosNahRoute(List<(double lat, double lon)> route, List<FotoPunkt> fotos)
    {
        var ids = new List<int>();
        if (route.Count < 2) return ids;
        var (mnLon, mnLat, mxLon, mxLat) = TourBbox(route);
        foreach (var f in fotos)
        {
            if (f.Id <= 0) continue;
            if (f.Lat < mnLat || f.Lat > mxLat || f.Lon < mnLon || f.Lon > mxLon) continue;
            double best = double.MaxValue;
            for (int i = 1; i < route.Count; i++)
            {
                double d = NavGeo.DistanzZuSegment(f.Lat, f.Lon, route[i - 1], route[i]);
                if (d < best) best = d;
                if (best <= 40) break;
            }
            if (best <= FotoNahMeter) ids.Add(f.Id);
        }
        return ids.Distinct().ToList();
    }
}
