namespace SpinNaviApp;

/// <summary>Einheitengerechte Formatierung von Strecke/Zeit – als reine, MAUI-freie
/// Logik (vom Navi-UI genutzt UND unit-getestet).</summary>
public static class Format
{
    public const double MeterProMeile = 1609.344;
    public const double MeterProFuss = 0.3048;

    /// <summary>Distanz menschenlesbar. <paramref name="imperial"/>=false → Meter/Kilometer
    /// (&lt;1 km in 10-m-Schritten), true → Fuß/Meilen (&lt;0,1 mi in 10-ft-Schritten).</summary>
    public static string Strecke(double meter, bool imperial)
    {
        if (imperial)
        {
            double meilen = meter / MeterProMeile;
            if (meilen >= 0.1) return $"{meilen:0.0} mi";
            return $"{Math.Round(meter / MeterProFuss / 10) * 10:0} ft";
        }
        return meter >= 1000 ? $"{meter / 1000.0:0.0} km" : $"{Math.Round(meter / 10) * 10:0} m";
    }

    /// <summary>Dauer: &lt;1 min, Minuten, oder Stunden:Minuten.</summary>
    public static string Zeit(double min)
    {
        if (min < 1) return "<1 min";
        if (min < 60) return $"{min:0} min";
        return $"{(int)(min / 60)}:{(int)(min % 60):00} h";
    }
}
