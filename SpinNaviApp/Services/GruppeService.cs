using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SpinNaviApp;

/// <summary>Eine Live-Position eines Gruppenmitglieds (vom Server /navi/gruppe/&lt;code&gt;.json).</summary>
public record GruppenMitglied(string Name, double Lat, double Lng, int AlterS);

/// <summary>Live-Positions-Sharing in einer Gruppe (per Code), zustandslos über den Server-Cache.
/// POST /navi/gruppe/position teilt die eigene Position; GET /navi/gruppe/&lt;code&gt;.json liefert
/// die Mitglieder. Beide Endpunkte sind öffentlich (kein Login nötig) und IP-gedrosselt.</summary>
public static class GruppeService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Gruppen-Code wie der Server säubern: nur ASCII-Alphanumerik, klein, max. 32 Zeichen.
    /// So ist der Code ein sicherer URL-/Cache-Bestandteil und stimmt mit der Server-Normalisierung überein.</summary>
    public static string CodeSaeubern(string? roh)
    {
        if (string.IsNullOrEmpty(roh)) return "";
        var sb = new StringBuilder(32);
        foreach (var c in roh)
            if (c < 128 && char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); if (sb.Length >= 32) break; }
        return sb.ToString();
    }

    /// <summary>Teilt die eigene Position in der Gruppe (POST). True bei Erfolg.</summary>
    public static async Task<bool> SendePositionAsync(string code, string name, double lat, double lng)
    {
        code = CodeSaeubern(code);
        if (code.Length == 0) return false;
        try
        {
            var body = JsonSerializer.Serialize(new { gruppe = code, name, lat, lng });
            using var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiBase + "/navi/gruppe/position")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (Auth.Token is { } tok) req.Headers.Add("Authorization", "Token " + tok);
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
    }

    /// <summary>Holt die aktuellen Mitglieder-Positionen der Gruppe (GET). Leere Liste bei Fehler.</summary>
    public static async Task<List<GruppenMitglied>> HoleAsync(string code)
    {
        var liste = new List<GruppenMitglied>();
        code = CodeSaeubern(code);
        if (code.Length == 0) return liste;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppConfig.ApiBase + $"/navi/gruppe/{code}.json");
            if (Auth.Token is { } tok) req.Headers.Add("Authorization", "Token " + tok);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return liste;
            var roh = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(roh);
            if (!doc.RootElement.TryGetProperty("mitglieder", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return liste;
            foreach (var m in arr.EnumerateArray())
            {
                if (!m.TryGetProperty("lat", out var la) || la.ValueKind != JsonValueKind.Number) continue;
                if (!m.TryGetProperty("lng", out var ln) || ln.ValueKind != JsonValueKind.Number) continue;
                string name = m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                int alter = m.TryGetProperty("alter_s", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : 0;
                liste.Add(new GruppenMitglied(name, la.GetDouble(), ln.GetDouble(), alter));
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        return liste;
    }
}
