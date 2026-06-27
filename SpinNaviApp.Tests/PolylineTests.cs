using Xunit;

namespace SpinNaviApp.Tests;

public class PolylineTests
{
    [Fact]
    public void Dekodiert_klassisches_Google_Beispiel_bei_1e5()
    {
        // Standard-Beispiel aus der Google-Polyline-Doku (Präzision 1e5).
        var pts = Polyline.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@", 1e5);

        Assert.Equal(3, pts.Count);
        Assert.Equal(38.5, pts[0].lat, 3);
        Assert.Equal(-120.2, pts[0].lon, 3);
        Assert.Equal(40.7, pts[1].lat, 3);
        Assert.Equal(-120.95, pts[1].lon, 3);
        Assert.Equal(43.252, pts[2].lat, 3);
        Assert.Equal(-126.453, pts[2].lon, 3);
    }

    [Fact]
    public void Leerer_oder_null_String_gibt_leere_Liste()
    {
        Assert.Empty(Polyline.Decode(""));
        Assert.Empty(Polyline.Decode(null));
    }

    [Fact]
    public void Praezision_1e6_liefert_kleinere_Koordinaten_als_1e5()
    {
        // Derselbe String mit Praezision 1e6 (Valhalla) → Werte 10x kleiner.
        var bei6 = Polyline.Decode("_p~iF~ps|U", 1e6);
        Assert.Single(bei6);
        Assert.Equal(3.85, bei6[0].lat, 2);
        Assert.Equal(-12.02, bei6[0].lon, 2);
    }

    [Fact]
    public void Abgeschnittener_String_verwirft_unvollstaendiges_Paar_ohne_Crash()
    {
        // Erstes Paar vollständig, danach nur halbes (lat ohne lon) -> nur 1 Punkt,
        // KEIN Phantom-Punkt und kein Absturz.
        var pts = Polyline.Decode("_p~iF~ps|U_ulL", 1e5);
        Assert.Single(pts);
        Assert.Equal(38.5, pts[0].lat, 3);
        Assert.Equal(-120.2, pts[0].lon, 3);
    }
}
