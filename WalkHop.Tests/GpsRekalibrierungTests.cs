using Xunit;

namespace WalkHop.Tests;

/// <summary>Simuliert den gemeldeten Fall: „Navigation zu" wird an einem noch UNGENAUEN GPS-Punkt (A)
/// getippt (Route wird von A berechnet); danach REKALIBRIERT das GPS und der Standort springt ~300 m zum
/// korrekten Punkt (B). Erwartet: die gewanderte Route zieht KEINE Linie über den Sprung (die „graue
/// Linie"), und die Vorschau-Route wird vom korrigierten Standort B NEU berechnet.</summary>
public class GpsRekalibrierungTests
{
    // Punkt A (Kaltstart, falsch) und B (rekalibriert, korrekt) ~300 m auseinander (Bucher Chaussee-Gegend).
    private const double ALat = 52.6000, ALon = 13.5000;
    private const double BLat = 52.6027, BLon = 13.5000;   // +0,0027° lat ≈ 300 m nördlich

    private static double Sprung() => NavGeo.Haversine(ALat, ALon, BLat, BLon);

    [Fact]
    public void Rekalibrierungs_Sprung_ist_etwa_300_m()
    {
        double d = Sprung();
        Assert.InRange(d, 250, 350);
    }

    [Fact]
    public void Sprung_trennt_die_gewanderte_Route_kein_grauer_Strich()
    {
        Assert.True(GpsFilter.SpurTrennen(Sprung()), "300-m-Rekalibrierung muss die Breadcrumb-Linie trennen");
    }

    [Fact]
    public void Kurzer_echter_Gehweg_trennt_die_Spur_nicht()
    {
        double gehen = NavGeo.Haversine(ALat, ALon, 52.60036, ALon);   // ~40 m
        Assert.InRange(gehen, 30, 55);
        Assert.False(GpsFilter.SpurTrennen(gehen), "40 m Gehen darf die Spur NICHT trennen");
    }

    [Fact]
    public void Standort_Korrektur_loest_Vorschau_Neuberechnung_aus()
    {
        Assert.True(GpsFilter.VorschauNeuBerechnen(Sprung()),
            "300-m-Korrektur muss die Vorschau-Route neu berechnen (nicht am falschen Punkt lassen)");
    }

    [Fact]
    public void Kleine_GPS_Zitterei_loest_keine_Neuberechnung_aus()
    {
        double zittern = NavGeo.Haversine(BLat, BLon, 52.60283, BLon);   // ~15 m
        Assert.InRange(zittern, 8, 25);
        Assert.False(GpsFilter.VorschauNeuBerechnen(zittern), "15 m Zittern darf KEIN Neuberechnen auslösen (kein Flackern)");
    }
}
