using System.Net.Http;
using System.Text.Json;

namespace WalkHop;

// Id = Foto-Primärschlüssel (für die Offline-Variante /ausfluege/foto/<id>/medium); optional/additiv.
public record TourPoi(string Name, string Kategorie, string Bild, double Lat, double Lon, int DistM, int Id = 0);

/// <summary>Tour-Details (/ausfluege/&lt;pk&gt;/details.json): Sehenswürdigkeiten am Weg.</summary>
public static class TourDetailService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<List<TourPoi>> PoisAsync(int id)
        => ParsePois(await _http.GetStringAsync(AppConfig.ApiBase + $"/ausfluege/{id}/details.json"));

    public static List<TourPoi> ParsePois(string roh)
    {
        var liste = new List<TourPoi>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }
        catch (JsonException) { return liste; }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("pois", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return liste;
            foreach (var p in arr.EnumerateArray())
                liste.Add(new TourPoi(
                    S(p, "name"), S(p, "kategorie"), S(p, "bild"),
                    D(p, "lat"), D(p, "lng"), (int)D(p, "dist_m")));
        }
        return liste;
    }

    private static double D(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
