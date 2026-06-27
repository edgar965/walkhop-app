using Xunit;

namespace SpinNaviApp.Tests;

public class NavGeoTests
{
    [Fact]
    public void Haversine_Berlin_Potsdam_etwa_27km()
    {
        // Berlin Mitte → Potsdam: rund 27 km Luftlinie.
        double m = NavGeo.Haversine(52.52, 13.405, 52.40, 13.06);
        Assert.InRange(m, 25000, 30000);
    }

    [Fact]
    public void Kumulativ_summiert_die_Segmente()
    {
        var pts = new List<(double lat, double lon)> { (52.52, 13.40), (52.52, 13.41), (52.52, 13.42) };
        var k = NavGeo.Kumulativ(pts);
        Assert.Equal(0, k[0]);
        Assert.True(k[1] > 0 && k[2] > k[1]);
        // beide Segmente etwa gleich lang
        Assert.InRange(k[2] - k[1], (k[1] - k[0]) * 0.9, (k[1] - k[0]) * 1.1);
    }

    [Fact]
    public void Projektion_findet_naechstes_Segment_und_Strecke()
    {
        var pts = new List<(double lat, double lon)> { (52.50, 13.40), (52.50, 13.42), (52.50, 13.44) };
        var k = NavGeo.Kumulativ(pts);
        // Punkt leicht neben der Mitte des ersten Segments
        var (idx, entlang, abstand) = NavGeo.Projektion(52.5005, 13.41, pts, k);
        Assert.Equal(0, idx);
        Assert.True(entlang > 0 && entlang < k[1]);
        Assert.True(abstand is > 0 and < 200);    // ~55 m Abstand
    }

    [Fact]
    public void Projektion_mit_einem_Punkt_liefert_Nullstrecke_ohne_Crash()
    {
        var pts = new List<(double lat, double lon)> { (52.5, 13.4) };
        var (idx, entlang, _) = NavGeo.Projektion(52.5005, 13.41, pts, NavGeo.Kumulativ(pts));
        Assert.Equal(0, idx);
        Assert.Equal(0, entlang);
    }

    [Fact]
    public void Projektion_mit_leerer_Liste_wirft_nicht()
    {
        var pts = new List<(double lat, double lon)>();
        var (idx, entlang, _) = NavGeo.Projektion(52.5, 13.4, pts, NavGeo.Kumulativ(pts));
        Assert.Equal(0, idx);
        Assert.Equal(0, entlang);
    }
}
