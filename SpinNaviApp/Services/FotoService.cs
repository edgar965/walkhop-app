using System.Net.Http;
using System.Text.Json;

namespace SpinNaviApp;

public record FotoPunkt(double Lat, double Lon, string Url, string Text, string Tour, int TourId);

/// <summary>Lädt verortete Fotos entlang der Touren (/ausfluege/fotos.json) für die Foto-Ebene.</summary>
public static class FotoService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };

    public static async Task<List<FotoPunkt>> LadeAsync()
    {
        var roh = await _http.GetStringAsync(AppConfig.ApiBase + "/ausfluege/fotos.json");
        return await Task.Run(() => ParseFotos(roh));   // viele Punkte → nicht auf dem UI-Thread
    }

    public static List<FotoPunkt> ParseFotos(string roh)
    {
        var liste = new List<FotoPunkt>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }
        catch (JsonException) { return liste; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return liste;
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (f.TryGetProperty("lat", out var la) && la.ValueKind == JsonValueKind.Number
                    && f.TryGetProperty("lng", out var lo) && lo.ValueKind == JsonValueKind.Number)
                    liste.Add(new FotoPunkt(la.GetDouble(), lo.GetDouble(),
                        Str(f, "url"), Str(f, "text"), Str(f, "tour"),
                        f.TryGetProperty("tour_id", out var ti) && ti.ValueKind == JsonValueKind.Number ? ti.GetInt32() : 0));
            }
        }
        return liste;
    }

    private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
