using Xunit;

namespace WalkHop.Tests;

/// <summary>Tests der aus MainPage ausgelagerten, MAUI-freien Navigations-Kernlogik
/// (NaviLogik): nächster Routenpunkt, Reststrecke, Routenabweichung, Manöver-Auswahl.</summary>
public class NaviLogikTests
{
    // ---- NaechsterIndex (Index des nächsten noch ausstehenden Routenpunkts) ----
    [Theory]
    [InlineData(2, 10, 3)]    // normaler Schritt: idx+1
    [InlineData(9, 10, 9)]    // am Ende der Route: bleibt auf dem letzten Punkt
    [InlineData(0, 1, 0)]     // Ein-Punkt-Route: bleibt auf 0 (kein Überlauf)
    [InlineData(5, 0, 0)]     // leere Route: keine negative Indizes
    public void NaechsterIndex_klemmt_auf_gueltigen_Bereich(int idx, int anzahl, int erwartet)
    {
        Assert.Equal(erwartet, NaviLogik.NaechsterIndex(idx, anzahl));
    }

    // ---- Reststrecke (Gesamtlänge minus zurückgelegt, nie negativ) ----
    [Fact]
    public void Reststrecke_zieht_zurueckgelegte_Strecke_ab()
    {
        Assert.Equal(700, NaviLogik.Reststrecke(1000, 300));
    }

    [Fact]
    public void Reststrecke_wird_nie_negativ()
    {
        // entlang > Gesamt (z. B. GPS-Sprung übers Ziel hinaus) → 0, nicht negativ
        Assert.Equal(0, NaviLogik.Reststrecke(500, 800));
    }

    // ---- IstAbseitsRoute (Off-Route-Schwelle, strikt größer) ----
    [Theory]
    [InlineData(60, 50, true)]    // 60 m daneben → off-route
    [InlineData(40, 50, false)]   // 40 m → noch auf der Route
    [InlineData(50, 50, false)]   // exakt auf der Schwelle → noch nicht abseits (strikt >)
    public void IstAbseitsRoute_vergleicht_strikt_groesser(double abstand, double schwelle, bool erwartet)
    {
        Assert.Equal(erwartet, NaviLogik.IstAbseitsRoute(abstand, schwelle));
    }

    // ---- AbweichungVonRoute (Abstand der Projektion auf die Route, Meter) ----
    [Fact]
    public void AbweichungVonRoute_auf_der_Route_ist_etwa_Null()
    {
        // Route entlang Breite 52.50 von 13.40 nach 13.41; Punkt liegt direkt darauf.
        var pts = new List<(double lat, double lon)> { (52.50, 13.40), (52.50, 13.41) };
        var kum = NavGeo.Kumulativ(pts);
        double d = NaviLogik.AbweichungVonRoute(52.50, 13.405, pts, kum);
        Assert.True(d < 1, $"erwartet ~0, war {d}");
    }

    [Fact]
    public void AbweichungVonRoute_senkrecht_daneben_ist_etwa_111m()
    {
        // 0,001° Breite nördlich der Linie ≈ 111 m
        var pts = new List<(double lat, double lon)> { (52.50, 13.40), (52.50, 13.41) };
        var kum = NavGeo.Kumulativ(pts);
        double d = NaviLogik.AbweichungVonRoute(52.501, 13.405, pts, kum);
        Assert.InRange(d, 100, 122);
    }

    // ---- BearingZumNaechsten (Kompasskurs Standort → nächster Routenpunkt) ----
    [Fact]
    public void BearingZumNaechsten_nach_Norden_ist_etwa_0()
    {
        var pts = new List<(double lat, double lon)> { (52.50, 13.40), (52.51, 13.40) };
        double b = NaviLogik.BearingZumNaechsten(52.50, 13.40, pts, 1);
        double normiert = b > 180 ? b - 360 : b;   // 359° wie -1° behandeln
        Assert.InRange(normiert, -2, 2);
    }

    [Fact]
    public void BearingZumNaechsten_nach_Osten_ist_etwa_90()
    {
        var pts = new List<(double lat, double lon)> { (52.50, 13.40), (52.50, 13.41) };
        double b = NaviLogik.BearingZumNaechsten(52.50, 13.40, pts, 1);
        Assert.InRange(b, 88, 92);
    }

    [Fact]
    public void BearingZumNaechsten_klemmt_den_Zielindex()
    {
        // Index über das Ende hinaus → letzter Punkt (kein IndexOutOfRange).
        var pts = new List<(double lat, double lon)> { (52.50, 13.40), (52.50, 13.41) };
        double b = NaviLogik.BearingZumNaechsten(52.50, 13.40, pts, 99);
        Assert.InRange(b, 88, 92);
    }

    // ---- NaechstesManoever (erstes Manöver, das distanzmäßig noch vor uns liegt) ----
    [Fact]
    public void NaechstesManoever_ueberspringt_das_Start_Manoever()
    {
        // Start-Manöver an Index 0 (Distanz 0), Abbiegung an Index 2.
        var kum = new double[] { 0, 100, 200, 300 };
        var begin = new[] { 0, 2 };
        int next = NaviLogik.NaechstesManoever(begin, kum, idx: 0, entlang: 0);
        Assert.Equal(1, next);   // nicht das Start-Manöver (Distanz 0), sondern die Abbiegung
    }

    [Fact]
    public void NaechstesManoever_liefert_minus1_wenn_alles_passiert_ist()
    {
        var kum = new double[] { 0, 100, 200, 300 };
        var begin = new[] { 0, 2 };
        // Schon an der Abbiegung vorbei (entlang 250 > kum[2] 200).
        int next = NaviLogik.NaechstesManoever(begin, kum, idx: 2, entlang: 250);
        Assert.Equal(-1, next);
    }

    [Fact]
    public void NaechstesManoever_klemmt_BeginIndex_auf_kum_Laenge()
    {
        // BeginIndex außerhalb von kum → wird auf den letzten gültigen Index geklemmt (kein Crash).
        var kum = new double[] { 0, 100, 200 };
        var begin = new[] { 99 };
        int next = NaviLogik.NaechstesManoever(begin, kum, idx: 0, entlang: 50);
        Assert.Equal(0, next);   // kum[^1]=200 > 50 → voraus
    }

    // ---- DistanzBisManoever (Meter bis zum Manöver-Stützpunkt, nie negativ) ----
    [Fact]
    public void DistanzBisManoever_misst_ab_zurueckgelegter_Strecke()
    {
        var kum = new double[] { 0, 100, 200, 300 };
        Assert.Equal(50, NaviLogik.DistanzBisManoever(kum, beginIndex: 2, entlang: 150));
    }

    [Fact]
    public void DistanzBisManoever_klemmt_Index_und_bleibt_nicht_negativ()
    {
        var kum = new double[] { 0, 100, 200, 300 };
        // BeginIndex 99 → kum[^1]=300; entlang 350 darüber hinaus → 0 statt negativ.
        Assert.Equal(0, NaviLogik.DistanzBisManoever(kum, beginIndex: 99, entlang: 350));
    }
}
