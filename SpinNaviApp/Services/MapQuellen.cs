using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;

namespace SpinNaviApp;

/// <summary>Die vier Kartenmodi der Web-Navi (identische Tile-Quellen) plus das
/// Wanderwege-/Radwege-Overlay. Jede Quelle cached offline in einem eigenen
/// Unterordner von <see cref="OfflineKarte.CacheDir"/>.</summary>
public enum Kartenmodus { Wandern, Standard, Satellit, Dunkel }

public static class MapQuellen
{
    private const string UA = "Spin1More/1.0 (+https://spin1more.com)";

    private static FileCache Cache(string sub)
    {
        var d = Path.Combine(OfflineKarte.CacheDir, sub);
        Directory.CreateDirectory(d);
        return new FileCache(d, "png");
    }

    /// <summary>Effektiver Karten-Modus aus Farbmodus + der Nutzer-Kartenwahl für die NAVIGATIONS-Karte:
    /// "nacht" erzwingt die dunkle Basiskarte; "auto" folgt dem System-Theme (dunkel → Dunkel); "tag"
    /// (und "auto" bei hellem Theme) nutzt den vom Nutzer gewählten Kartenmodus.
    /// Vorrangregel: Sobald „dunkel" gefordert ist (Nacht oder Auto+System dunkel), hat der Farbmodus
    /// Vorrang vor der Kartenwahl; sonst gewinnt die Nutzer-Wahl (auch wenn das selbst „Dunkel" ist).</summary>
    public static Kartenmodus EffektiverModus(string farbmodus, Kartenmodus gewaehlt, bool systemDunkel)
    {
        bool dunkel = farbmodus == "nacht" || (farbmodus == "auto" && systemDunkel);
        return dunkel ? Kartenmodus.Dunkel : gewaehlt;
    }

    public static HttpTileSource Quelle(Kartenmodus m) => m switch
    {
        Kartenmodus.Standard => Bauen("standard",
            "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", new[] { "a", "b", "c" }, 19),
        Kartenmodus.Satellit => Bauen("satellit",
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}", null, 19),
        Kartenmodus.Dunkel => Bauen("dunkel",
            "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png", new[] { "a", "b", "c" }, 19),
        _ => Bauen("wandern",
            "https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png", new[] { "a", "b", "c" }, 17),
    };

    /// <summary>Wanderwege- (Fuß) bzw. Radwege-Overlay (waymarkedtrails).</summary>
    public static HttpTileSource Wanderwege(bool rad) => Bauen(
        rad ? "radwege" : "wanderwege",
        $"https://tile.waymarkedtrails.org/{(rad ? "cycling" : "hiking")}/{{z}}/{{x}}/{{y}}.png", null, 18);

    private static HttpTileSource Bauen(string name, string url, string[]? nodes, int maxZoom)
    {
        var schema = new GlobalSphericalMercator(YAxis.OSM, 0, maxZoom, name);
        return new HttpTileSource(schema, url, nodes, name: name,
            persistentCache: Cache(name), userAgent: UA);
    }
}
