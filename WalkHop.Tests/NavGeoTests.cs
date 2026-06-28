using Xunit;

namespace WalkHop.Tests;

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

    // ---- Bearing (Kompasskurs 0–360°, 0 = Norden) ----
    [Theory]
    [InlineData(52.5, 13.5, 88, 92)]    // nach Osten  ≈ 90°
    [InlineData(52.5, 13.3, 268, 272)]  // nach Westen ≈ 270°
    [InlineData(52.6, 13.4, 358, 362)]  // nach Norden ≈ 0/360°
    [InlineData(52.4, 13.4, 178, 182)]  // nach Süden  ≈ 180°
    public void Bearing_zeigt_in_die_richtige_Himmelsrichtung(double zielLat, double zielLon, double min, double max)
    {
        double b = NavGeo.Bearing(52.5, 13.4, zielLat, zielLon);
        double normiert = b < 5 ? b + 360 : b;   // Norden (0°) im Fenster 358–362 prüfbar machen
        double wert = (min >= 358) ? normiert : b;
        Assert.InRange(wert, min, max);
    }

    // ---- DistanzZuSegment (Abstand Punkt → Segment in Metern) ----
    [Fact]
    public void DistanzZuSegment_Punkt_auf_dem_Segment_ist_etwa_Null()
    {
        double d = NavGeo.DistanzZuSegment(52.5, 13.42, (52.5, 13.40), (52.5, 13.44));
        Assert.True(d < 1, $"erwartet ~0, war {d}");
    }

    [Fact]
    public void DistanzZuSegment_senkrecht_daneben_ist_etwa_111m()
    {
        // 0,001° Breite nördlich der Linie ≈ 111 m
        double d = NavGeo.DistanzZuSegment(52.501, 13.42, (52.50, 13.40), (52.50, 13.44));
        Assert.InRange(d, 95, 130);
    }

    [Fact]
    public void DistanzZuSegment_entartetes_Segment_liefert_Punktdistanz_ohne_NaN()
    {
        // a == b (len2 == 0): darf kein NaN liefern, sondern den Punktabstand (~111 m)
        double d = NavGeo.DistanzZuSegment(52.501, 13.40, (52.5, 13.40), (52.5, 13.40));
        Assert.False(double.IsNaN(d));
        Assert.InRange(d, 95, 130);
    }
}
