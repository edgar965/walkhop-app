using System.Net.Http;
using System.Text.Json;

namespace WalkHop;

public record TourInfo(int Id, string Name, double Km, int DauerMin, string Kategorie,
                       List<(double lat, double lon)> Route, List<string> Facetten, string Farbe,
                       (double lat, double lon)? Start, string Bild, string Beschreibung,
                       string Grad, bool Bahn, string DetailUrl);

/// <summary>Lädt die Touren-Liste (mit Routen-Geometrie + Facetten/Farbe/Start/Bild)
/// vom Server (/ausfluege/routen.json) – für die Übersichtskarte und die Navigation.</summary>
public static class TourService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };

    // Einmal geladen+geparst (1,8 MB) → von StartPage UND Übersichtskarte geteilt, statt doppelt.
    private static List<TourInfo>? _cache;
    private static Task<List<TourInfo>>? _laden;

    public static async Task<List<TourInfo>> LadeTourenAsync()
    {
        if (_cache != null) return _cache;
        // Parallele Aufrufer (Start + Übersicht kurz nacheinander) teilen sich denselben Ladevorgang.
        _laden ??= LadenInternAsync();
        try { return await _laden; }
        catch { _laden = null; throw; }   // fehlgeschlagenen Ladevorgang NICHT behalten → nächster Aufruf lädt neu
    }

    private static async Task<List<TourInfo>> LadenInternAsync()
    {
        var roh = await _http.GetStringAsync(AppConfig.ApiBase + "/ausfluege/routen.json");
        _cache = await Task.Run(() => ParseTouren(roh));   // 1,8 MB nicht auf dem UI-Thread parsen
        return _cache;
    }

    public static List<TourInfo> ParseTouren(string roh)
    {
        var liste = new List<TourInfo>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }
        catch (JsonException) { return liste; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return liste;

            foreach (var t in doc.RootElement.EnumerateArray())
            {
                var route = new List<(double, double)>();
                if (t.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.Array)
                    foreach (var p in r.EnumerateArray())
                        if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2
                            && p[0].ValueKind == JsonValueKind.Number
                            && p[1].ValueKind == JsonValueKind.Number)
                            route.Add((p[0].GetDouble(), p[1].GetDouble()));
                (double, double)? start = null;
                if (t.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.Array
                    && s.GetArrayLength() >= 2 && s[0].ValueKind == JsonValueKind.Number
                    && s[1].ValueKind == JsonValueKind.Number)
                    start = (s[0].GetDouble(), s[1].GetDouble());

                if (route.Count < 2 && start == null) continue;   // Tour braucht Route ODER Startpunkt

                var facetten = new List<string>();
                if (t.TryGetProperty("facetten", out var f) && f.ValueKind == JsonValueKind.Array)
                    foreach (var x in f.EnumerateArray())
                        if (x.ValueKind == JsonValueKind.String) facetten.Add(x.GetString() ?? "");

                liste.Add(new TourInfo(
                    Z(t, "id"), S(t, "name"), D(t, "km"), (int)D(t, "dauer"),
                    S(t, "kat_label"), route, facetten, Farbe(t),
                    start, S(t, "bild"), S(t, "beschreibung"), S(t, "grad"),
                    B(t, "bahn"), S(t, "detail_url")));
            }
        }
        return liste;
    }

    private static string Farbe(JsonElement e)
    {
        var f = S(e, "farbe");
        return string.IsNullOrEmpty(f) ? "#0d9488" : f;
    }

    private static int Z(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static double D(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool B(JsonElement e, string n) => e.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True);
}
