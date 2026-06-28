namespace SpinNaviApp;

/// <summary>Helfer für die Entfernungs-Zeile im Karten-Kontextmenü.</summary>
public static class Standort
{
    public static double MeterDist(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = (lat2 - lat1) * System.Math.PI / 180, dLon = (lon2 - lon1) * System.Math.PI / 180;
        double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2)
                 + System.Math.Cos(lat1 * System.Math.PI / 180) * System.Math.Cos(lat2 * System.Math.PI / 180)
                   * System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
        return 2 * R * System.Math.Asin(System.Math.Min(1, System.Math.Sqrt(a)));
    }

    /// <summary>Kontextmenü-Zeile „📏 450 m (von GPS Pos.)" bzw. „… (von Berlin Mitte)".
    /// Misst vom aktuellen GPS-Standort, sonst vom Default-Punkt (Einstellungen → Karte).</summary>
    public static string EntfernungZeile(double zielLat, double zielLon, (double lat, double lon)? gps)
    {
        double fLat, fLon; string quelle;
        if (gps is { } g) { fLat = g.lat; fLon = g.lon; quelle = "GPS Pos."; }
        else { fLat = Einst.StandardLat; fLon = Einst.StandardLng; quelle = Einst.StandardName; }
        double m = MeterDist(zielLat, zielLon, fLat, fLon);
        string e = m < 1000 ? $"{System.Math.Round(m)} m" : $"{m / 1000:0.0} km";
        return $"📏 {e} (von {quelle})";
    }
}
