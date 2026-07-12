using System.Collections.Generic;
using Xunit;

namespace WalkHop.Tests;

/// <summary>Doubletten-Erkennung der Routenvorschläge (<see cref="NavGeo.RoutenAehnlich"/>). Genau diese
/// Logik war der Kern des Bugs „3 Routen werden nicht angezeigt": die drei Offroad-Costing-Varianten
/// lieferten praktisch dieselbe Route und wurden zu EINER entdoppelt. Der Fix nutzt jetzt echte Router-
/// Alternativen (verschiedene Länge/Verlauf) – die hier als NICHT-ähnlich (also behalten) geprüft werden.</summary>
public class RoutenAehnlichTests
{
    private static RouteErgebnis R(double km, params (double lat, double lon)[] pts)
        => new(new List<(double lat, double lon)>(pts), km, km * 12, new List<Manoever>());

    [Fact]
    public void Identische_Route_ist_aehnlich()
    {
        var a = R(2.8, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        var b = R(2.8, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        Assert.True(NavGeo.RoutenAehnlich(a, b));
    }

    [Fact]
    public void Echte_Alternative_mit_anderer_Laenge_wird_behalten()
    {
        // Wie Alexanderplatz→Brandenburger Tor am Live-Server: 2.811 vs 3.058 km → verschieden → behalten.
        var a = R(2.811, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        var b = R(3.058, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        Assert.False(NavGeo.RoutenAehnlich(a, b));
    }

    [Fact]
    public void Gleiche_Laenge_aber_anderer_Verlauf_wird_behalten()
    {
        var a = R(2.8, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        // Umweg: Mittelpunkt ~220 m nach Norden verschoben → Geometrie weicht ab.
        var b = R(2.8, (52.52, 13.40), (52.517, 13.39), (52.51, 13.38));
        Assert.False(NavGeo.RoutenAehnlich(a, b));
    }

    [Fact]
    public void Fast_gleiche_Costing_Varianten_werden_entdoppelt()
    {
        // Die 3 Offroad-Varianten am Live-Server: 2.811 vs 2.826 km, gleicher Verlauf → Doublette
        // (= Grund, warum vorher nur 1 statt 3 Routen erschienen).
        var a = R(2.811, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        var b = R(2.826, (52.52, 13.40), (52.515, 13.39), (52.51, 13.38));
        Assert.True(NavGeo.RoutenAehnlich(a, b));
    }
}
