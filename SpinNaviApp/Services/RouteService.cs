using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpinNaviApp;

/// <summary>Wird geworfen, wenn der Server eine Route wegen erreichtem Tageslimit
/// ablehnt (HTTP 402) – die App zeigt dann die Paywall.</summary>
public class PaywallException : Exception
{
    public PaywallException(string roh) : base(roh) { }
}

public record Manoever(string Anweisung, double Km, double Sekunden, int BeginIndex, int Typ);

public record RouteErgebnis(
    List<(double lat, double lon)> Punkte,
    double Km, double Minuten,
    List<Manoever> Manoever);

/// <summary>Ruft die Routing-API (Valhalla-Proxy /navi/route.json) des Servers auf
/// und dekodiert die Antwort (Polyline6 + Manöver). Keine native Routing-Engine nötig –
/// der Server (bzw. die FOSSGIS-Valhalla) rechnet, die App zeichnet.</summary>
public static class RouteService
{
    private static readonly HttpClient _http = Erzeuge();

    /// <summary>Liefert das App-Token (von der App gesetzt; im Test null) – entkoppelt
    /// RouteService von der MAUI-Auth-Schicht.</summary>
    public static Func<string?>? TokenProvider;

    private static HttpClient Erzeuge()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        c.DefaultRequestHeaders.ExpectContinue = false;   // kein „Expect: 100-continue"
        return c;
    }

    // POST-Anfrage mit App-Token (für Konto/Metering).
    private static HttpRequestMessage Anfrage(string url, string json)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        var token = TokenProvider?.Invoke();
        if (!string.IsNullOrEmpty(token)) req.Headers.Add("Authorization", "Token " + token);
        return req;
    }

    /// <summary>costing_options wie in der Web-Navi (costingOptionen()) – flacher Dict,
    /// den der Server unter dem costing-Schlüssel verschachtelt.</summary>
    public static Dictionary<string, object> CostingOptionen(
        string costing, string wegtyp, bool autobahn, bool unbefestigt, bool schlechteOberflaeche)
    {
        if (costing == "auto")
            return new() { ["use_highways"] = autobahn ? 0.25 : 1.0, ["use_tracks"] = unbefestigt ? 0.0 : 0.5 };
        if (costing == "pedestrian")
        {
            if (wegtyp == "natur") return new() { ["use_tracks"] = 1.0, ["walkway_factor"] = 0.5 };
            if (wegtyp == "fest") return new() { ["use_tracks"] = 0.0, ["walkway_factor"] = 1.0 };
            return new();                       // neutral
        }
        return new() { ["use_roads"] = 0.4, ["bicycle_type"] = "Hybrid",
                       ["avoid_bad_surfaces"] = schlechteOberflaeche ? 0.8 : 0.0 };   // bicycle
    }

    /// <summary>Baut den Request-Body (separat → unit-testbar ohne Netz).</summary>
    public static string BaueAnfrageJson(double vonLat, double vonLon, double nachLat, double nachLon,
        string costing, Dictionary<string, object> optionen, string sprache, int alternates = 0, bool folge = false)
    {
        var body = new
        {
            locations = new[]
            {
                new { lat = vonLat, lon = vonLon },
                new { lat = nachLat, lon = nachLon },
            },
            costing,
            costing_options = optionen,    // flach; der Server nistet unter costing
            language = sprache,
            units = "kilometers",
            alternates,
            folge,                         // Folge-Anfrage (Reroute/Anfahrt) → kein Routen-Kontingent
        };
        return JsonSerializer.Serialize(body);
    }

    /// <summary>Wie <see cref="RouteAsync(double,double,double,double,string,Dictionary{string,object},string)"/>,
    /// liefert aber zusätzlich Alternativrouten (Valhalla „alternates").</summary>
    public static async Task<(RouteErgebnis? haupt, List<RouteErgebnis> alternativen)> RouteVollAsync(
        double vonLat, double vonLon, double nachLat, double nachLon,
        string costing, Dictionary<string, object> optionen, string sprache, int alternates, bool folge = false)
    {
        var json = BaueAnfrageJson(vonLat, vonLon, nachLat, nachLon, costing, optionen, sprache, alternates, folge);
        using var resp = await _http.SendAsync(Anfrage(AppConfig.ApiBase + "/navi/route.json", json));
        if ((int)resp.StatusCode == 402) throw new PaywallException(await resp.Content.ReadAsStringAsync());
        if (!resp.IsSuccessStatusCode) return (null, new List<RouteErgebnis>());
        var roh = await resp.Content.ReadAsStringAsync();
        return (ParseTrip(roh), ParseAlternativen(roh));
    }

    /// <summary>Parst die Alternativrouten (alternates[].trip) einer Valhalla-Antwort.</summary>
    public static List<RouteErgebnis> ParseAlternativen(string roh)
    {
        var liste = new List<RouteErgebnis>();
        try
        {
            using var doc = JsonDocument.Parse(roh);
            if (doc.RootElement.TryGetProperty("alternates", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                {
                    var erg = ParseTripRoot(a);
                    if (erg != null) liste.Add(erg);
                }
        }
        catch (JsonException) { }
        return liste;
    }

    public static Task<RouteErgebnis?> RouteAsync(
        double vonLat, double vonLon, double nachLat, double nachLon, string costing = "pedestrian")
        => RouteAsync(vonLat, vonLon, nachLat, nachLon, costing, new Dictionary<string, object>(), "de-DE");

    public static async Task<RouteErgebnis?> RouteAsync(
        double vonLat, double vonLon, double nachLat, double nachLon,
        string costing, Dictionary<string, object> optionen, string sprache, bool folge = false)
    {
        var json = BaueAnfrageJson(vonLat, vonLon, nachLat, nachLon, costing, optionen, sprache, 0, folge);
        using var resp = await _http.SendAsync(Anfrage(AppConfig.ApiBase + "/navi/route.json", json));
        var roh = await resp.Content.ReadAsStringAsync();
        if ((int)resp.StatusCode == 402) throw new PaywallException(roh);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode} @ {AppConfig.ApiBase}: {roh.Substring(0, Math.Min(80, roh.Length))}");
        return ParseTrip(roh);
    }

    /// <summary>Map-Matching einer Tour-Spur (/navi/trace.json) → echte Turn-by-Turn-
    /// Manöver entlang der GPX-Route (gesnappte Geometrie + Manöver).</summary>
    public static async Task<RouteErgebnis?> TraceAsync(
        List<(double lat, double lon)> shape, string costing, string sprache, bool folge = false)
    {
        if (shape.Count < 2) return null;
        var body = new
        {
            shape = shape.Select(p => new[] { p.lat, p.lon }).ToArray(),
            costing,
            language = sprache,
            folge,
        };
        var json = JsonSerializer.Serialize(body);
        using var resp = await _http.SendAsync(Anfrage(AppConfig.ApiBase + "/navi/trace.json", json));
        if (!resp.IsSuccessStatusCode) return null;
        return ParseTrip(await resp.Content.ReadAsStringAsync());
    }

    /// <summary>Parst die Valhalla-Trip-Antwort (Polyline6 + Manöver + Summary).
    /// Separat von der HTTP-Schicht → unit-testbar ohne Server.</summary>
    public static RouteErgebnis? ParseTrip(string roh)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(roh); }   // Nicht-JSON (z. B. HTML-Fehlerseite) → keine Route statt Crash
        catch (JsonException) { return null; }
        using (doc)
            return ParseTripRoot(doc.RootElement);
    }

    private static RouteErgebnis? ParseTripRoot(JsonElement root)
    {
        if (!root.TryGetProperty("trip", out var trip)) return null;
        if (!trip.TryGetProperty("legs", out var legs) || legs.GetArrayLength() == 0) return null;

        var leg = legs[0];
        var punkte = Polyline.Decode(leg.TryGetProperty("shape", out var sh) ? sh.GetString() : null);

        double km = 0, sek = 0;
        if (trip.TryGetProperty("summary", out var s))
        {
            if (s.TryGetProperty("length", out var l)) km = l.GetDouble();
            if (s.TryGetProperty("time", out var t)) sek = t.GetDouble();
        }

        var mans = new List<Manoever>();
        if (leg.TryGetProperty("maneuvers", out var ms))
            foreach (var m in ms.EnumerateArray())
                mans.Add(new Manoever(
                    m.TryGetProperty("instruction", out var i) ? i.GetString() ?? "" : "",
                    m.TryGetProperty("length", out var ml) ? ml.GetDouble() : 0,
                    m.TryGetProperty("time", out var mt) ? mt.GetDouble() : 0,
                    m.TryGetProperty("begin_shape_index", out var bi) ? bi.GetInt32() : 0,
                    m.TryGetProperty("type", out var ty) ? ty.GetInt32() : 0));

        return new RouteErgebnis(punkte, km, sek / 60.0, mans);
    }
}
