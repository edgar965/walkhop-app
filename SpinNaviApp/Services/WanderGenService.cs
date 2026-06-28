using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SpinNaviApp;

/// <summary>Ein generierter Rundwanderungs-Vorschlag (vom Server /navi/rundtour.json).</summary>
public record GenWanderung(string Name, List<(double lat, double lon)> Route,
                           double Km, int DauerMin, int Fotos, string Farbe);

/// <summary>Ruft den Wanderungs-Generator des Servers auf (Rundtouren ab einem Punkt,
/// Ziel-Distanz, Modus Fuß/Rad, bevorzugt Foto-Sehenswürdigkeiten).</summary>
public static class WanderGenService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public static async Task<List<GenWanderung>> ErzeugeAsync(double lat, double lon, double km, string costing)
    {
        var liste = new List<GenWanderung>();
        try
        {
            var body = JsonSerializer.Serialize(new { lat, lng = lon, distanz_km = km, costing, anzahl = 5 });
            using var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiBase + "/navi/rundtour.json")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (Auth.Token is { } tok) req.Headers.Add("Authorization", "Token " + tok);
            using var resp = await _http.SendAsync(req);
            var roh = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return liste;
            using var doc = JsonDocument.Parse(roh);
            if (!doc.RootElement.TryGetProperty("vorschlaege", out var arr)) return liste;
            foreach (var v in arr.EnumerateArray())
            {
                var route = new List<(double lat, double lon)>();
                if (v.TryGetProperty("route", out var r))
                    foreach (var p in r.EnumerateArray())
                        if (p.GetArrayLength() >= 2)
                            route.Add((p[0].GetDouble(), p[1].GetDouble()));   // [lat, lng]
                if (route.Count < 2) continue;
                liste.Add(new GenWanderung(
                    Str(v, "name"), route,
                    Num(v, "km"), (int)Num(v, "dauer_minuten"),
                    (int)Num(v, "fotos"), Str(v, "farbe")));
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        return liste;
    }

    private static string Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double Num(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
