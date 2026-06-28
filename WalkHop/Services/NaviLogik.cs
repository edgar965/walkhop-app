namespace WalkHop;

/// <summary>Reine Navigations-Berechnungen OHNE MAUI-/Mapsui-/Seiten-Abhängigkeit – aus
/// <c>MainPage</c> ausgelagert, damit die Turn-by-Turn-Mathematik (nächster Routenpunkt,
/// Reststrecke, Routenabweichung, Manöver-Auswahl) eigenständig getestet werden kann. Nutzt
/// durchgängig die vorhandenen <see cref="NavGeo"/>-Funktionen, statt Geometrie zu duplizieren.</summary>
public static class NaviLogik
{
    /// <summary>Index des nächsten (noch ausstehenden) Routenpunkts nach dem aktuellen
    /// Segmentindex <paramref name="idx"/> aus der Projektion – auf den gültigen Bereich geklammert.</summary>
    public static int NaechsterIndex(int idx, int anzahlPunkte)
        => Math.Min(idx + 1, Math.Max(0, anzahlPunkte - 1));

    /// <summary>Reststrecke (Meter) bis zum Ziel = Gesamtlänge minus bereits entlang der Route
    /// zurückgelegte Strecke <paramref name="entlang"/>. Nie negativ.</summary>
    public static double Reststrecke(double navGesamt, double entlang)
        => Math.Max(0, navGesamt - entlang);

    /// <summary>Abweichung (Meter) der Position von der Route = Abstand ihrer Projektion auf die
    /// Route. Bequemer Wrapper um <see cref="NavGeo.Projektion"/> (verwirft idx/entlang).</summary>
    public static double AbweichungVonRoute(double lat, double lon,
        List<(double lat, double lon)> pts, double[] kum, int letzterIdx = -1)
        => NavGeo.Projektion(lat, lon, pts, kum, letzterIdx).abstand;

    /// <summary>Liegt die Position weiter als <paramref name="schwelleMeter"/> neben der Route?
    /// (Auslöser fürs Auto-Reroute.)</summary>
    public static bool IstAbseitsRoute(double abstand, double schwelleMeter)
        => abstand > schwelleMeter;

    /// <summary>Kompasskurs (0–360°, 0 = Norden) vom Standort zum nächsten Routenpunkt
    /// <paramref name="naechsterIdx"/> (Index geklammert).</summary>
    public static double BearingZumNaechsten(double lat, double lon,
        List<(double lat, double lon)> pts, int naechsterIdx)
    {
        var z = pts[Math.Clamp(naechsterIdx, 0, pts.Count - 1)];
        return NavGeo.Bearing(lat, lon, z.lat, z.lon);
    }

    /// <summary>Index des nächsten Manövers, das DISTANZMÄSSIG noch vor uns liegt: das erste,
    /// dessen Stützpunkt (<paramref name="beginIndizes"/>) &gt;= aktuellem Segmentindex
    /// <paramref name="idx"/> ist UND dessen kumulative Distanz über der bereits zurückgelegten
    /// Strecke <paramref name="entlang"/> liegt. -1, wenn keines mehr voraus liegt. Die Distanz-
    /// statt reiner Index-Prüfung ist robust gegen GPS-Sprünge und überspringt das Start-Manöver
    /// (Distanz 0) ganz natürlich.</summary>
    public static int NaechstesManoever(IReadOnlyList<int> beginIndizes, double[] kum, int idx, double entlang)
    {
        for (int m = 0; m < beginIndizes.Count; m++)
        {
            int bi = Math.Min(beginIndizes[m], kum.Length - 1);
            if (beginIndizes[m] >= idx && kum[bi] > entlang) return m;
        }
        return -1;
    }

    /// <summary>Distanz (Meter) bis zum Manöver am <paramref name="beginIndex"/>, gemessen ab der
    /// bereits zurückgelegten Strecke <paramref name="entlang"/> (BeginIndex auf <paramref name="kum"/>
    /// geklammert). Nie negativ.</summary>
    public static double DistanzBisManoever(double[] kum, int beginIndex, double entlang)
    {
        int bi = Math.Min(beginIndex, kum.Length - 1);
        return Math.Max(0, kum[bi] - entlang);
    }
}
