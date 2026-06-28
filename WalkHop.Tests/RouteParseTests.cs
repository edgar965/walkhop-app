using Xunit;

namespace WalkHop.Tests;

public class RouteParseTests
{
    private const string BeispielTrip =
        "{\"trip\":{\"legs\":[{" +
        "\"shape\":\"_p~iF~ps|U_ulLnnqC\"," +
        "\"maneuvers\":[" +
        "{\"instruction\":\"Geradeaus laufen.\",\"length\":1.5,\"time\":120,\"begin_shape_index\":0,\"type\":1}," +
        "{\"instruction\":\"Rechts abbiegen.\",\"length\":0.7,\"time\":90,\"begin_shape_index\":1,\"type\":10}" +
        "]}]," +
        "\"summary\":{\"length\":3.2,\"time\":600}}}";

    [Fact]
    public void Parst_Summary_Punkte_und_Manoever()
    {
        var r = RouteService.ParseTrip(BeispielTrip);

        Assert.NotNull(r);
        Assert.Equal(3.2, r!.Km, 2);
        Assert.Equal(10.0, r.Minuten, 2);          // 600 s / 60
        Assert.True(r.Punkte.Count >= 2);
        Assert.Equal(2, r.Manoever.Count);
        Assert.Equal("Geradeaus laufen.", r.Manoever[0].Anweisung);
        Assert.Equal("Rechts abbiegen.", r.Manoever[1].Anweisung);
        Assert.Equal(10, r.Manoever[1].Typ);
    }

    [Fact]
    public void Antwort_ohne_trip_gibt_null()
    {
        Assert.Null(RouteService.ParseTrip("{\"error\":\"keine Route\"}"));
    }

    [Fact]
    public void Antwort_ohne_legs_gibt_null()
    {
        Assert.Null(RouteService.ParseTrip("{\"trip\":{\"legs\":[]}}"));
    }

    [Fact]
    public void Leg_ohne_shape_gibt_Ergebnis_mit_leeren_Punkten()
    {
        var r = RouteService.ParseTrip("{\"trip\":{\"legs\":[{\"maneuvers\":[]}],\"summary\":{\"length\":1,\"time\":60}}}");
        Assert.NotNull(r);
        Assert.Empty(r!.Punkte);          // leere Geometrie -> UI fängt das per Punkte.Count < 2 ab
        Assert.Equal(1.0, r.Km, 2);
    }

    [Fact]
    public void Fehlende_Summary_ergibt_Km_und_Minuten_null()
    {
        var r = RouteService.ParseTrip("{\"trip\":{\"legs\":[{\"shape\":\"_p~iF~ps|U_ulLnnqC\",\"maneuvers\":[]}]}}");
        Assert.NotNull(r);
        Assert.Equal(0, r!.Km);
        Assert.Equal(0, r.Minuten);
        Assert.True(r.Punkte.Count >= 2);
    }

    [Fact]
    public void Nicht_JSON_Antwort_gibt_null_statt_Exception()
    {
        Assert.Null(RouteService.ParseTrip("<html>502 Bad Gateway</html>"));
        Assert.Null(RouteService.ParseTrip(""));
    }
}
