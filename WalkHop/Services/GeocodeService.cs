using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WalkHop;

public record GeoOrt(string Name, double Lat, double Lon);

/// <summary>Adress-/Ortssuche als Karten-/Umkreis-Mittelpunkt (/ausfluege/geocode.json → Nominatim).</summary>
public static class GeocodeService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<List<GeoOrt>> SucheAsync(string q)
        => Parse(await _http.GetStringAsync(AppConfig.ApiBase + "/ausfluege/geocode.json?q=" + Uri.EscapeDataString(q)));

    /// <summary>Reverse-Geocoding: Koordinaten → kurzer Ortsname (null bei Fehlschlag).</summary>
    public static async Task<string?> ReverseAsync(double lat, double lon)
    {
        try
        {
            var roh = await _http.GetStringAsync(AppConfig.ApiBase + "/ausfluege/geocode.json"
                + "?lat=" + lat.ToString(CultureInfo.InvariantCulture)
                + "&lon=" + lon.ToString(CultureInfo.InvariantCulture));
            var liste = Parse(roh);
            return liste.Count > 0 && !string.IsNullOrEmpty(liste[0].Name) ? liste[0].Name : null;
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Geocoding", ex); return null; }
    }

    public static List<GeoOrt> Parse(string roh)
    {
        var liste = new List<GeoOrt>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }
        catch (JsonException) { return liste; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return liste;
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("lat", out var la) && la.ValueKind == JsonValueKind.Number
                    && e.TryGetProperty("lng", out var lo) && lo.ValueKind == JsonValueKind.Number)
                    liste.Add(new GeoOrt(
                        e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        la.GetDouble(), lo.GetDouble()));
        }
        return liste;
    }
}
