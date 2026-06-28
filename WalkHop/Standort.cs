namespace WalkHop;

/// <summary>Helfer für die Entfernungs-Zeile im Karten-Kontextmenü.</summary>
public static class Standort
{
    // Keine eigene Haversine-Implementierung mehr (Duplikat) – intern auf NavGeo.Haversine
    // umgestellt; Signatur bleibt für die Aufrufer unverändert.
    public static double MeterDist(double lat1, double lon1, double lat2, double lon2)
        => NavGeo.Haversine(lat1, lon1, lat2, lon2);

    /// <summary>Kontextmenü-Zeile „📏 450 m (von GPS Pos.)" bzw. „… (von Berlin Mitte)".
    /// Misst vom aktuellen GPS-Standort, sonst vom Default-Punkt (Einstellungen → Karte).</summary>
    public static string EntfernungZeile(double zielLat, double zielLon, (double lat, double lon)? gps)
    {
        double fLat, fLon; string quelle;
        if (gps is { } g) { fLat = g.lat; fLon = g.lon; quelle = L.T("quelle_gps"); }
        else { fLat = Einst.StandardLat; fLon = Einst.StandardLng; quelle = StandardnameAnzeige(Einst.StandardName); }
        double m = MeterDist(zielLat, zielLon, fLat, fLon);
        string e = m < 1000 ? $"{System.Math.Round(m)} m" : $"{m / 1000:0.0} km";
        return L.T("entfernung_zeile", e, quelle);
    }

    /// <summary>Übersetzt die bekannten, intern (deutsch) gespeicherten Standardpunkt-Namen für die
    /// Anzeige; ein eigener (frei benannter) Punkt bleibt unverändert.</summary>
    public static string StandardnameAnzeige(string gespeichert) => gespeichert switch
    {
        "Berlin Mitte" => L.T("standardname_berlin"),
        "eigener Punkt" => L.T("standardname_eigener"),
        _ => gespeichert,
    };
}
