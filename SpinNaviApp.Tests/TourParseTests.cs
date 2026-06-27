using Xunit;

namespace SpinNaviApp.Tests;

public class TourParseTests
{
    [Fact]
    public void ParseTouren_liest_Felder_und_Route()
    {
        const string json = """
        [
          {"id": 7, "name": "Gelber Balken Geltow", "km": 8.6, "dauer": 130,
           "kat_label": "Wanderung", "route": [[52.40, 13.06], [52.41, 13.07], [52.42, 13.08]]}
        ]
        """;
        var t = TourService.ParseTouren(json);
        Assert.Single(t);
        Assert.Equal(7, t[0].Id);
        Assert.Equal("Gelber Balken Geltow", t[0].Name);
        Assert.Equal(8.6, t[0].Km, 3);
        Assert.Equal(130, t[0].DauerMin);
        Assert.Equal("Wanderung", t[0].Kategorie);
        Assert.Equal(3, t[0].Route.Count);
        Assert.Equal(52.40, t[0].Route[0].lat, 3);
    }

    [Fact]
    public void ParseTouren_ueberspringt_Touren_ohne_brauchbare_Route()
    {
        const string json = """
        [
          {"id": 1, "name": "Ohne Route", "route": []},
          {"id": 2, "name": "Nur ein Punkt", "route": [[52.5, 13.4]]},
          {"id": 3, "name": "Gueltig", "route": [[52.5, 13.4], [52.5, 13.41]]}
        ]
        """;
        var t = TourService.ParseTouren(json);
        Assert.Single(t);
        Assert.Equal(3, t[0].Id);
    }

    [Fact]
    public void ParseTouren_robust_bei_fehlenden_Feldern()
    {
        const string json = """
        [{"route": [[52.5, 13.4], [52.5, 13.41]]}]
        """;
        var t = TourService.ParseTouren(json);
        Assert.Single(t);
        Assert.Equal(0, t[0].Id);
        Assert.Equal("", t[0].Name);
        Assert.Equal(0, t[0].Km);
    }

    [Fact]
    public void ParseTouren_bei_Nicht_Array_Wurzel_leere_Liste()
    {
        Assert.Empty(TourService.ParseTouren("{\"foo\": 1}"));
    }

    [Fact]
    public void ParseTouren_liest_Facetten_Farbe_und_Start()
    {
        const string json = """
        [{"id": 9, "name": "T", "km": 8, "dauer": 90, "kat_label": "Wanderung",
          "facetten": ["wandern", "qualitytour"], "farbe": "#15803d",
          "start": [52.5, 13.4], "grad": "mittel", "bahn": true,
          "route": [[52.5, 13.4], [52.51, 13.41]]}]
        """;
        var t = TourService.ParseTouren(json)[0];
        Assert.Contains("wandern", t.Facetten);
        Assert.Contains("qualitytour", t.Facetten);
        Assert.Equal("#15803d", t.Farbe);
        Assert.NotNull(t.Start);
        Assert.Equal(52.5, t.Start!.Value.lat, 3);
        Assert.True(t.Bahn);
        Assert.Equal("mittel", t.Grad);
    }

    [Fact]
    public void ParseTouren_Farbe_faellt_auf_Default_zurueck()
    {
        var t = TourService.ParseTouren("[{\"route\":[[52.5,13.4],[52.5,13.41]]}]")[0];
        Assert.Equal("#0d9488", t.Farbe);
        Assert.Empty(t.Facetten);
    }

    [Fact]
    public void ParseTouren_ueberspringt_nicht_numerische_Koordinaten_ohne_Crash()
    {
        // Müll-Koordinaten ["a","b"] werden übersprungen; bleibt nur 1 gültiger Punkt
        // -> Tour fällt mangels >=2 Punkten raus, kein Crash.
        const string json = """
        [{"id": 5, "name": "Müll", "route": [["a","b"], [52.5, 13.41]]}]
        """;
        Assert.Empty(TourService.ParseTouren(json));
    }
}
