using System.Net.Http;
using System.Text.Json;

namespace WalkHop;

/// <summary>Holt das Höhenprofil einer Route (/navi/hoehe.json → range_height = [[Distanz_m, Höhe_m], …]).</summary>
public static class HoeheService
{
    private static readonly HttpClient _http = Erzeuge();

    private static HttpClient Erzeuge()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.ExpectContinue = false;
        return c;
    }

    public static async Task<List<(double dist, double hoehe)>> ProfilAsync(List<(double lat, double lon)> route)
    {
        var shape = route.Select(p => new[] { p.lat, p.lon }).ToArray();
        var json = JsonSerializer.Serialize(new { shape });
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        // Response disposen (kein Leak) und Statuscode prüfen, bevor der Body gelesen wird.
        using var resp = await _http.PostAsync(AppConfig.ApiBase + "/navi/hoehe.json", content);
        if (!resp.IsSuccessStatusCode) return new List<(double dist, double hoehe)>();
        return ParseHoehe(await resp.Content.ReadAsStringAsync());
    }

    public static List<(double dist, double hoehe)> ParseHoehe(string roh)
    {
        var liste = new List<(double, double)>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }
        catch (JsonException) { return liste; }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("range_height", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return liste;
            foreach (var p in arr.EnumerateArray())
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2
                    && p[0].ValueKind == JsonValueKind.Number && p[1].ValueKind == JsonValueKind.Number)
                    liste.Add((p[0].GetDouble(), p[1].GetDouble()));
        }
        return liste;
    }
}
