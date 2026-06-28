namespace WalkHop;

/// <summary>Geometrie für die Live-Navigation: Haversine-Distanzen, kumulierte
/// Streckenlänge und Projektion der GPS-Position auf die Route.</summary>
public static class NavGeo
{
    public static double Haversine(double la1, double lo1, double la2, double lo2)
    {
        const double R = 6371000.0;
        double dLa = (la2 - la1) * Math.PI / 180, dLo = (lo2 - lo1) * Math.PI / 180;
        double a = Math.Sin(dLa / 2) * Math.Sin(dLa / 2)
                 + Math.Cos(la1 * Math.PI / 180) * Math.Cos(la2 * Math.PI / 180)
                 * Math.Sin(dLo / 2) * Math.Sin(dLo / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Kompasskurs (0–360°, 0 = Norden) von Punkt 1 nach Punkt 2.</summary>
    public static double Bearing(double la1, double lo1, double la2, double lo2)
    {
        double φ1 = la1 * Math.PI / 180, φ2 = la2 * Math.PI / 180;
        double dλ = (lo2 - lo1) * Math.PI / 180;
        double y = Math.Sin(dλ) * Math.Cos(φ2);
        double x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(dλ);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>Abstand (Meter) eines Punktes zum Segment a→b (lokale planare Näherung).</summary>
    public static double DistanzZuSegment(double lat, double lon,
                                          (double lat, double lon) a, (double lat, double lon) b)
    {
        double mLat = 111320.0, mLon = 111320.0 * Math.Cos(lat * Math.PI / 180);
        double px = (lon - a.lon) * mLon, py = (lat - a.lat) * mLat;
        double bx = (b.lon - a.lon) * mLon, by = (b.lat - a.lat) * mLat;
        double len2 = bx * bx + by * by;
        double t = len2 > 0 ? Math.Clamp((px * bx + py * by) / len2, 0, 1) : 0;
        double dx = px - t * bx, dy = py - t * by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double[] Kumulativ(List<(double lat, double lon)> pts)
    {
        var k = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            k[i] = k[i - 1] + Haversine(pts[i - 1].lat, pts[i - 1].lon, pts[i].lat, pts[i].lon);
        return k;
    }

    /// <summary>Nächster Segmentindex + zurückgelegte Strecke (Meter) + Abstand zur Route (Meter).
    /// Mit <paramref name="letzterIdx"/> &gt;= 0 wird nur ein Fenster um den letzten Index
    /// durchsucht (O(1) im GPS-Takt statt O(n)); bei großer Abweichung folgt ein Vollscan.</summary>
    public static (int idx, double entlang, double abstand) Projektion(
        double lat, double lon, List<(double lat, double lon)> pts, double[] kum, int letzterIdx = -1)
    {
        if (letzterIdx < 0)
            return ProjektionBereich(lat, lon, pts, kum, 0, pts.Count - 1);

        int von = Math.Max(0, letzterIdx - 10);
        int bis = Math.Min(pts.Count - 1, letzterIdx + 60);
        var fenster = ProjektionBereich(lat, lon, pts, kum, von, bis);
        if (fenster.abstand <= 60) return fenster;                 // sicher auf der Route → Fenster reicht
        var voll = ProjektionBereich(lat, lon, pts, kum, 0, pts.Count - 1);
        return voll.abstand < fenster.abstand ? voll : fenster;
    }

    private static (int idx, double entlang, double abstand) ProjektionBereich(
        double lat, double lon, List<(double lat, double lon)> pts, double[] kum, int von, int bis)
    {
        int best = von; double bestD = double.MaxValue, bestEntlang = 0;
        for (int i = von; i < bis && i < pts.Count - 1; i++)
        {
            var (plat, plon) = NaehesterPunkt(lat, lon, pts[i].lat, pts[i].lon, pts[i + 1].lat, pts[i + 1].lon);
            double d = Haversine(lat, lon, plat, plon);
            if (d < bestD)
            {
                bestD = d; best = i;
                bestEntlang = kum[i] + Haversine(pts[i].lat, pts[i].lon, plat, plon);
            }
        }
        return (best, bestEntlang, bestD);
    }

    // Nächster Punkt auf dem Segment a→b. Längengrade werden mit cos(Breite)
    // skaliert, damit die Projektion in mittleren Breiten (Berlin ~52°) nicht
    // verzerrt; der Parameter t läuft im skalierten Raum, das Ergebnis wird über
    // die echten Delta-Werte zurückgerechnet.
    private static (double lat, double lon) NaehesterPunkt(
        double plat, double plon, double alat, double alon, double blat, double blon)
    {
        double k = Math.Cos(alat * Math.PI / 180);
        double dLon = blon - alon, dLat = blat - alat;
        double dx = dLon * k, dy = dLat;
        double len2 = dx * dx + dy * dy;
        double t = len2 > 0 ? ((plon - alon) * k * dx + (plat - alat) * dy) / len2 : 0;
        t = Math.Max(0, Math.Min(1, t));
        return (alat + dLat * t, alon + dLon * t);
    }
}
