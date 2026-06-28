using System.Globalization;
using Xunit;

namespace WalkHop.Tests;

/// <summary>Tests der neuen einheitengerechten Strecken-/Zeit-Formatierung (Format.cs),
/// die das Navi-UI für die Einheiten-Einstellung (metrisch/imperial) nutzt.</summary>
public class FormatTests
{
    // Zahl aus einem formatierten String ("1,5 km"/"1.0 mi") kulturunabhängig herausziehen.
    private static double Zahl(string s)
    {
        var teil = s.Split(' ')[0];
        return double.Parse(teil, NumberStyles.Any, CultureInfo.CurrentCulture);
    }

    [Theory]
    [InlineData(0, "0 m")]
    [InlineData(124, "120 m")]    // auf 10 m gerundet
    [InlineData(847, "850 m")]
    [InlineData(999, "1000 m")]   // knapp unter 1 km → noch in Metern
    public void Strecke_metrisch_unter_km_in_zehnermetern(double meter, string erwartet)
        => Assert.Equal(erwartet, Format.Strecke(meter, imperial: false));

    [Theory]
    [InlineData(1000, 1.0)]
    [InlineData(1500, 1.5)]
    [InlineData(12345, 12.3)]
    public void Strecke_metrisch_ab_km_in_kilometern(double meter, double km)
    {
        var s = Format.Strecke(meter, imperial: false);
        Assert.EndsWith(" km", s);
        Assert.Equal(km, Zahl(s), 1);
    }

    [Theory]
    [InlineData(30.48, "100 ft")]    // 100 ft
    [InlineData(100, "330 ft")]      // 328 ft → auf 10 gerundet
    public void Strecke_imperial_unter_meile_in_fuss(double meter, string erwartet)
        => Assert.Equal(erwartet, Format.Strecke(meter, imperial: true));

    [Theory]
    [InlineData(1609.344, 1.0)]
    [InlineData(8046.72, 5.0)]
    public void Strecke_imperial_ab_meile_in_meilen(double meter, double mi)
    {
        var s = Format.Strecke(meter, imperial: true);
        Assert.EndsWith(" mi", s);
        Assert.Equal(mi, Zahl(s), 1);
    }

    [Theory]
    [InlineData(0.5, "<1 min")]
    [InlineData(1, "1 min")]
    [InlineData(30, "30 min")]
    [InlineData(59, "59 min")]
    [InlineData(60, "1:00 h")]
    [InlineData(90, "1:30 h")]
    [InlineData(125, "2:05 h")]
    public void Zeit_formatiert_minuten_und_stunden(double min, string erwartet)
        => Assert.Equal(erwartet, Format.Zeit(min));
}
