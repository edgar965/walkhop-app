using System.Net.Http;
using System.Text.Json;

namespace SpinNaviApp;

public record PoiTreffer(string Name, double Lat, double Lon, string Kategorie);

/// <summary>Sucht sichtbare Orte/POIs im Kartenausschnitt über /navi/poi.json.</summary>
public static class PoiService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };

    /// <summary>Liefert das App-Token (von der App gesetzt; im Test null) – nötig, damit der
    /// Server die Textsuche dem Konto zuordnen und metern kann (entkoppelt von der Auth-Schicht).</summary>
    public static Func<string?>? TokenProvider;

    public static async Task<List<PoiTreffer>> SucheAsync(double west, double sued, double ost, double nord, string q)
    {
        var url = $"{AppConfig.ApiBase}/navi/poi.json?bbox={west:0.####},{sued:0.####},{ost:0.####},{nord:0.####}";
        if (!string.IsNullOrWhiteSpace(q)) url += "&q=" + Uri.EscapeDataString(q.Trim());
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var token = TokenProvider?.Invoke();
        if (!string.IsNullOrEmpty(token)) req.Headers.Add("Authorization", "Token " + token);
        using var resp = await _http.SendAsync(req);
        var roh = await resp.Content.ReadAsStringAsync();
        if ((int)resp.StatusCode == 402) throw new PaywallException(roh);   // Such-Tageslimit → Paywall
        return ParsePoi(roh);
    }

    public static List<PoiTreffer> ParsePoi(string roh)
    {
        var liste = new List<PoiTreffer>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }   // Nicht-JSON (HTML-Fehlerseite) → leere Liste statt Crash
        catch (JsonException) { return liste; }
        using (doc)
        {
        if (!doc.RootElement.TryGetProperty("poi", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return liste;
        foreach (var p in arr.EnumerateArray())
        {
            if (p.TryGetProperty("lat", out var la) && la.ValueKind == JsonValueKind.Number
                && p.TryGetProperty("lng", out var lo) && lo.ValueKind == JsonValueKind.Number)
                liste.Add(new PoiTreffer(
                    p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    la.GetDouble(), lo.GetDouble(),
                    p.TryGetProperty("kat", out var k) ? k.GetString() ?? "" : ""));
        }
        return liste;
        }
    }
}
