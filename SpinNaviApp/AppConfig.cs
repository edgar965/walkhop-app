namespace SpinNaviApp;

/// <summary>
/// Zentrale Konfiguration der NATIVEN Spin1More-Navigations-App (Mapsui-Karte,
/// natives GPS/Kompass/Routing). Der Produktivcode nutzt nur <see cref="ApiBase"/>;
/// die übrigen Felder gehören ausschließlich zum Debug-Selbsttest (TestPage) und
/// werden nur in Debug-Builds kompiliert.
/// </summary>
public static class AppConfig
{
    /// <summary>Server für Routing (/navi/route.json) und Touren (/ausfluege/routen.json).
    /// Produktion = Live; zum Testen auf den Dev-Server stellen (vom Android-Emulator
    /// aus ist der Host-Rechner "http://10.0.2.2:8090").</summary>
    public const string ApiBase = "https://spin1more.com";

    /// <summary>Markenname dieser App-Variante (Flyout-Kopf, Shell-Titel). Pro Marke/Build
    /// ändern: WalkHop-App = "WalkHop", Spin1More-App = "Spin1More".</summary>
    public const string Marke = "WalkHop";

    /// <summary>Untertitel unter dem Markennamen im Flyout-Kopf (Hamburger-Menü).</summary>
    public const string MarkeUntertitel = "Wandern und Sehenswürdigkeiten";

#if DEBUG
    // ---- Nur Debug: Selbsttest-WebView (TestPage) --------------------------
    /// <summary>Basis-URL der Web-App (nur für den Debug-Selbsttest-WebView).</summary>
    public const string BaseUrl = "https://spin1more.com";

    public const string StartPath = "/ausfluege/";

    /// <summary>Nur Dev-Server: vor den Tests automatisch per /dev-login/ anmelden.</summary>
    public static readonly bool UseDevLogin = false;

    public static string StartUrl => BaseUrl + StartPath;
    public static string NaviTestUrl => BaseUrl + "/navi/ziel/?lat=52.50&lng=13.42&name=Test&demo=1";
    public static string OsmTestUrl => BaseUrl + "/ausfluege/";
    public static string NaviTourTestUrl => BaseUrl + "/navi/tour/1/?demo=1";

    /// <summary>Gruppen-Schlüssel (aus testcases.js) → Test-Seite. Reihenfolge = Tab-Reihenfolge.</summary>
    public static (string key, string titel, string url)[] TestGruppen => new[]
    {
        ("navi", "Navigation", NaviTestUrl),
        ("osm", "OSM-Übersicht", OsmTestUrl),
        ("navitour", "Tour", NaviTourTestUrl),
    };

    public static string DevLoginUrl => BaseUrl + "/dev-login/";
#endif
}
