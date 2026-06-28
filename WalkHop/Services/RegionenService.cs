using System.Net.Http;
using System.Text.Json;

namespace WalkHop;

/// <summary>Eine vordefinierte Offline-Region des Servers (/navi/karten.json). Die bbox
/// (min_lon, min_lat, max_lon, max_lat) nutzt die App, um die Raster-Kacheln dieser Region
/// vorzuladen. (Die Server-PMTiles selbst sind für die Web-Offline-Karte; die App lädt Raster.)</summary>
public record KartenRegion(string Id, string Name, string Gruppe,
                           double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    public double MitteLat => (MinLat + MaxLat) / 2;
    public double MitteLon => (MinLon + MaxLon) / 2;
}

/// <summary>Lädt die Liste der vordefinierten Offline-Regionen vom Server.</summary>
public static class RegionenService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<List<KartenRegion>> LadeAsync()
    {
        try
        {
            var roh = await _http.GetStringAsync(AppConfig.ApiBase + "/navi/karten.json");
            return ParseRegionen(roh);   // Parsen separat → unit-testbar
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return new List<KartenRegion>(); }
    }

    /// <summary>Parst die Regionen aus dem rohen JSON-String (separat von der HTTP-Methode,
    /// damit unit-testbar – analog zu TourService.ParseTouren / FotoService.ParseFotos). Defensiv:
    /// fehlende/falsch typisierte bbox-Werte werden übersprungen statt zu crashen.</summary>
    public static List<KartenRegion> ParseRegionen(string roh)
    {
        var liste = new List<KartenRegion>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }
        catch (JsonException) { return liste; }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("regionen", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return liste;
            foreach (var r in arr.EnumerateArray())
            {
                if (!r.TryGetProperty("bbox", out var bb) || bb.ValueKind != JsonValueKind.Array || bb.GetArrayLength() < 4)
                    continue;
                // Alle vier bbox-Werte müssen Zahlen sein, sonst Eintrag überspringen (kein Crash bei GetDouble).
                if (bb[0].ValueKind != JsonValueKind.Number || bb[1].ValueKind != JsonValueKind.Number
                    || bb[2].ValueKind != JsonValueKind.Number || bb[3].ValueKind != JsonValueKind.Number)
                    continue;
                liste.Add(new KartenRegion(
                    Str(r, "id"), Str(r, "name"), Str(r, "gruppe"),
                    bb[0].GetDouble(), bb[1].GetDouble(), bb[2].GetDouble(), bb[3].GetDouble()));
            }
        }
        return liste;
    }

    private static string Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
