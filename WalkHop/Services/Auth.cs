using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace WalkHop;

/// <summary>Konto-/Auth-Schicht der App: anonymes Geräte-Konto (ohne Eingabe),
/// Login/Registrierung, Token-Auth, gecachter Freischaltungs-Status.</summary>
public static class Auth
{
    private static readonly HttpClient _http = Erzeuge();
    private static string? _token;

    private static HttpClient Erzeuge()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        c.DefaultRequestHeaders.ExpectContinue = false;
        return c;
    }

    public static string? Token => _token;
    public static bool Angemeldet => !string.IsNullOrEmpty(_token);

    // Gecachter Status (aus /api/me bzw. Login-Antwort)
    public static bool Anonym { get => Preferences.Get("k_anonym", true); private set => Preferences.Set("k_anonym", value); }
    public static string Email { get => Preferences.Get("k_email", ""); private set => Preferences.Set("k_email", value); }
    // Vor-/Nachname: lokal gepflegtes Profil (die API speichert sie aktuell nicht; werden
    // bei der Registrierung mitgesendet, damit der Server sie später übernehmen kann).
    public static string Vorname { get => Preferences.Get("k_vorname", ""); set => Preferences.Set("k_vorname", value); }
    public static string Name { get => Preferences.Get("k_name", ""); set => Preferences.Set("k_name", value); }
    public static string Plan { get => Preferences.Get("k_plan", "frei"); private set => Preferences.Set("k_plan", value); }
    public static bool Premium { get => Preferences.Get("k_premium", false); private set => Preferences.Set("k_premium", value); }
    public static bool AlleFunktionen { get => Preferences.Get("k_alle", false); private set => Preferences.Set("k_alle", value); }
    /// <summary>Admin/Vollzugriff: vom Server freigeschaltet (alle_funktionen) ODER das fest
    /// hinterlegte Admin-Konto e@edgarm.de. Steuert u. a. den Selbsttest-Menüpunkt.</summary>
    public static bool IstAdmin => AlleFunktionen
        || string.Equals(Email, "e@edgarm.de", StringComparison.OrdinalIgnoreCase);
    /// <summary>Voller Funktionsumfang ohne Kauf nötig: zahlendes Premium-Konto ODER Admin/
    /// Alle-Funktionen. Steuert konsistent, dass „Premium freischalten" NUR ohne Vollzugriff
    /// erscheint und das Kontingent als „unbegrenzt" gilt. (Admin e@edgarm.de hat Vollzugriff,
    /// auch wenn der Server kein zahlendes „premium" meldet.)</summary>
    public static bool Vollzugriff => Premium || IstAdmin;
    /// <summary>Wird ausgelöst, wenn sich der Konto-Status geändert hat (Login/Logout/Refresh).</summary>
    public static event Action? StatusGeaendert;
    /// <summary>Wird ausgelöst, wenn ein Tages-Limit der aktuellen Stufe erreicht ist
    /// (Parameter = Art des Limits: „zeit"/„routen"/…). Die UI zeigt daraufhin den
    /// Registrieren-/Upgrade-Hinweis. Feuert pro Tag/Session nur einmal.</summary>
    public static event Action<string>? LimitErreicht;
    public static int RoutenHeute { get => Preferences.Get("k_routen", 0); private set => Preferences.Set("k_routen", value); }
    public static int GratisProTag { get => Preferences.Get("k_gratis", 2); private set => Preferences.Set("k_gratis", value); }
    public static int CreditsRouten { get => Preferences.Get("k_cr", 0); private set => Preferences.Set("k_cr", value); }
    public static int OfflineGekauft { get => Preferences.Get("k_off", 0); private set => Preferences.Set("k_off", value); }
    // Nutzungsstufe + Zeit-Budget (Demo = X Min/Tag). Für Anzeige und Bestätigungs-Hinweis.
    public static string Stufe { get => Preferences.Get("k_stufe", "demo"); private set => Preferences.Set("k_stufe", value); }
    public static bool EmailBestaetigt { get => Preferences.Get("k_ebest", false); private set => Preferences.Set("k_ebest", value); }
    public static int MinutenProTag { get => Preferences.Get("k_minlim", 0); private set => Preferences.Set("k_minlim", value); }
    public static int MinutenHeuteS { get => Preferences.Get("k_minhs", 0); private set => Preferences.Set("k_minhs", value); }
    // Merker, damit der Limit-Dialog nicht bei jedem Ping erneut aufpoppt (Reset bei neuem Tag).
    private static bool _limitGemeldet;

    /// <summary>Beim App-Start: Token laden bzw. anonymes Geräte-Konto anlegen.</summary>
    public static async Task InitAsync()
    {
        RouteService.TokenProvider = () => _token;   // Token an Routing-Anfragen anhängen (Metering)
        PoiService.TokenProvider = () => _token;      // Token an POI-Textsuche anhängen (Such-Metering)
        try { _token = await SecureStorage.Default.GetAsync("apptoken"); }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Konto-Token laden", ex); }
        if (string.IsNullOrEmpty(_token)) await GeraetKontoAsync();
        else _ = AktualisiereAsync();
    }

    private static async Task GeraetKontoAsync()
    {
        try
        {
            using var resp = await _http.PostAsync(AppConfig.ApiBase + "/api/geraet/",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode) await StatusUebernehmen(await resp.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Geräte-Konto anlegen", ex); }
    }

    public static async Task AktualisiereAsync()
    {
        if (!Angemeldet) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppConfig.ApiBase + "/api/me/");
            req.Headers.Add("Authorization", "Token " + _token);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) await StatusUebernehmen(await resp.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Konto-Status laden", ex); }
    }

    /// <summary>Nutzungs-Heartbeat: meldet aktive Vordergrund-Sekunden an den Server
    /// (Zeit-Budget der Stufe + Tracking). Erreicht das Konto sein Tages-Limit, wird
    /// einmalig <see cref="LimitErreicht"/> ausgelöst (die UI zeigt den Hinweis).</summary>
    public static async Task PingAsync(int sekunden)
    {
        if (!Angemeldet || sekunden <= 0) return;
        try
        {
            var json = JsonSerializer.Serialize(new { sekunden });
            using var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiBase + "/api/nutzung/ping/")
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            req.Headers.Add("Authorization", "Token " + _token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;
            var roh = await resp.Content.ReadAsStringAsync();
            await StatusUebernehmen(roh);   // aktualisiert Zähler/Minuten + feuert StatusGeaendert
            using var doc = JsonDocument.Parse(roh);
            var r = doc.RootElement;
            bool limit = r.TryGetProperty("limit_erreicht", out var le) && le.ValueKind == JsonValueKind.True;
            if (limit && !_limitGemeldet)
            {
                _limitGemeldet = true;
                string art = TextFeld(r, "limit") ?? "zeit";
                MainThread.BeginInvokeOnMainThread(() => LimitErreicht?.Invoke(art));
            }
            else if (!limit)
            {
                _limitGemeldet = false;   // neuer Tag / Upgrade → Hinweis wieder erlauben
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    /// <summary>Login bestehender Nutzer. Gibt null bei Erfolg, sonst die Fehlermeldung.</summary>
    public static Task<string?> LoginAsync(string email, string passwort) => AuthRufAsync("/api/login/", email, passwort);

    /// <summary>Registrierung. Vor-/Nachname werden mitgesendet (Server kann sie übernehmen)
    /// und lokal gemerkt.</summary>
    public static async Task<string?> RegistrierenAsync(string email, string passwort, string vorname = "", string name = "")
    {
        var fehler = await AuthRufAsync("/api/register/", email, passwort, vorname, name);
        if (fehler == null) { Vorname = vorname; Name = name; }   // lokal merken
        return fehler;
    }

    private static async Task<string?> AuthRufAsync(string pfad, string email, string passwort, string vorname = "", string name = "")
    {
        try
        {
            var json = JsonSerializer.Serialize(new { email, passwort, vorname, name });
            using var resp = await _http.PostAsync(AppConfig.ApiBase + pfad,
                new StringContent(json, Encoding.UTF8, "application/json"));
            var roh = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) { await StatusUebernehmen(roh); return null; }
            try { using var d = JsonDocument.Parse(roh); return d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "Fehler"; }
            catch { return "Fehler"; }
        }
        catch (Exception ex) { Debug.WriteLine(ex); return "Server nicht erreichbar"; }
    }

    /// <summary>Lädt eine Aufnahme (JSON-Body mit punkte/name/dauer_s) hoch.</summary>
    public static async Task<bool> SendeAufnahmeAsync(string json)
    {
        if (!Angemeldet) return false;
        using var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiBase + "/navi/aufnahme.json")
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Add("Authorization", "Token " + _token);
        using var resp = await _http.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    public static async Task AbmeldenAsync()
    {
        if (!string.IsNullOrEmpty(_token))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiBase + "/api/logout/")
                { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                req.Headers.Add("Authorization", "Token " + _token);
                await _http.SendAsync(req);
            }
            catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Abmelden", ex); }
        }
        _token = null;
        // Ausnahme hier (z. B. Keystore/Plattform-Fehler) darf den Logout NICHT abbrechen.
        try { SecureStorage.Default.Remove("apptoken"); }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Token löschen", ex); }
        // ALLE konto-/profilgebundenen Werte löschen – inkl. lokal gemerktem Namen
        // (sonst sähe der nächste Nutzer auf einem geteilten Gerät den Namen des vorigen)
        // und Gratis-Kontingent (sonst bliebe ein erhöhtes Premium-Limit stehen).
        foreach (var k in new[] { "k_anonym", "k_email", "k_vorname", "k_name", "k_plan",
                                  "k_premium", "k_alle", "k_routen", "k_gratis", "k_cr", "k_off",
                                  "k_stufe", "k_ebest", "k_minlim", "k_minhs" })
            Preferences.Remove(k);
        _limitGemeldet = false;
        await GeraetKontoAsync();   // sofort neues anonymes Konto
    }

    // String-Property aus dem JSON lesen (null, falls fehlend/kein String).
    private static string? TextFeld(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static async Task StatusUebernehmen(string roh)
    {
        try
        {
            using var doc = JsonDocument.Parse(roh);
            var r = doc.RootElement;
            if (r.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
            {
                _token = t.GetString();
                try { await SecureStorage.Default.SetAsync("apptoken", _token!); } catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Token speichern", ex); }
            }
            string altEmail = Email;   // für Konto-/Email-Wechsel-Erkennung (Namen-Reset)
            if (r.TryGetProperty("anonym", out var a)) Anonym = a.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("email", out var e)) Email = e.GetString() ?? "";
            // Vor-/Nachname: vom Server übernehmen, falls geliefert (vorname/first_name,
            // name/last_name). Liefert der Server keine Namen UND wechselt das Konto (andere
            // Email), den lokal gemerkten Namen leeren – sonst zeigte ein neues Konto den
            // Namen des vorigen an.
            bool emailGewechselt = !string.Equals(altEmail, Email, StringComparison.OrdinalIgnoreCase);
            if (TextFeld(r, "vorname") is { } vn) Vorname = vn;
            else if (TextFeld(r, "first_name") is { Length: > 0 } fn) Vorname = fn;
            else if (emailGewechselt) Vorname = "";
            if (TextFeld(r, "name") is { } nm) Name = nm;
            else if (TextFeld(r, "last_name") is { Length: > 0 } ln) Name = ln;
            else if (emailGewechselt) Name = "";
            if (r.TryGetProperty("plan", out var p)) Plan = p.GetString() ?? "frei";
            if (r.TryGetProperty("premium", out var pr)) Premium = pr.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("alle_funktionen", out var al)) AlleFunktionen = al.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("routen_heute", out var rh) && rh.ValueKind == JsonValueKind.Number) RoutenHeute = rh.GetInt32();
            if (r.TryGetProperty("gratis_pro_tag", out var g) && g.ValueKind == JsonValueKind.Number) GratisProTag = g.GetInt32();
            if (r.TryGetProperty("credits_routen", out var cr) && cr.ValueKind == JsonValueKind.Number) CreditsRouten = cr.GetInt32();
            if (r.TryGetProperty("offline_gekauft", out var of) && of.ValueKind == JsonValueKind.Number) OfflineGekauft = of.GetInt32();
            if (TextFeld(r, "stufe") is { Length: > 0 } st) Stufe = st;
            if (r.TryGetProperty("email_bestaetigt", out var eb)) EmailBestaetigt = eb.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("minuten_pro_tag", out var mp) && mp.ValueKind == JsonValueKind.Number) MinutenProTag = mp.GetInt32();
            if (r.TryGetProperty("minuten_heute_s", out var mh) && mh.ValueKind == JsonValueKind.Number) MinutenHeuteS = mh.GetInt32();
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Konto-Status verarbeiten", ex); }
        try { MainThread.BeginInvokeOnMainThread(() => StatusGeaendert?.Invoke()); }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Konto-Status melden", ex); }
    }
}
