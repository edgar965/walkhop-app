using Xunit;

namespace SpinNaviApp.Tests;

public class GeocodeDetailTests
{
    [Fact]
    public void Geocode_Parse_liest_Orte()
    {
        const string json = "[{\"name\":\"Potsdam\",\"lat\":52.4,\"lng\":13.06},{\"name\":\"Berlin\",\"lat\":52.52,\"lng\":13.4}]";
        var o = GeocodeService.Parse(json);
        Assert.Equal(2, o.Count);
        Assert.Equal("Potsdam", o[0].Name);
        Assert.Equal(52.4, o[0].Lat, 3);
        Assert.Equal(13.06, o[0].Lon, 3);
    }

    [Fact]
    public void Geocode_Parse_robust()
    {
        Assert.Empty(GeocodeService.Parse("[]"));
        Assert.Empty(GeocodeService.Parse("<html>err</html>"));
        Assert.Empty(GeocodeService.Parse("{\"x\":1}"));
    }

    [Fact]
    public void Geocode_Parse_liest_Reverse_Antwort()
    {
        // Der neue Reverse-Modus (/ausfluege/geocode.json?lat=&lon=) liefert dieselbe Form:
        // ein Element [{name, lat, lng}] → Name für „Letzte Ziele" aus Karten-Tipps.
        const string json = "[{\"name\":\"Brandenburger Tor\",\"lat\":52.5163,\"lng\":13.3777}]";
        var o = GeocodeService.Parse(json);
        Assert.Single(o);
        Assert.Equal("Brandenburger Tor", o[0].Name);
        Assert.Equal(52.5163, o[0].Lat, 4);
    }

    [Fact]
    public void Details_ParsePois_liest_Sehenswuerdigkeiten()
    {
        const string json = "{\"pois\":[{\"name\":\"Schloss\",\"kategorie\":\"Kultur\",\"bild\":\"/m/s.jpg\",\"lat\":52.4,\"lng\":13.06,\"dist_m\":120}]}";
        var p = TourDetailService.ParsePois(json);
        Assert.Single(p);
        Assert.Equal("Schloss", p[0].Name);
        Assert.Equal("Kultur", p[0].Kategorie);
        Assert.Equal(120, p[0].DistM);
        Assert.Equal(52.4, p[0].Lat, 3);
    }

    [Fact]
    public void Details_ParsePois_robust()
    {
        Assert.Empty(TourDetailService.ParsePois("{\"pois\":[]}"));
        Assert.Empty(TourDetailService.ParsePois("<html>502</html>"));
        Assert.Empty(TourDetailService.ParsePois("{}"));
    }

    [Fact]
    public void TourService_behaelt_Start_only_Tour()
    {
        // Tour ohne brauchbare Route, aber mit Startpunkt → bleibt erhalten (Übersicht-Marker).
        const string json = "[{\"id\":7,\"name\":\"Nur Start\",\"route\":[],\"start\":[52.5,13.4]}]";
        var t = TourService.ParseTouren(json);
        Assert.Single(t);
        Assert.Equal(7, t[0].Id);
        Assert.NotNull(t[0].Start);
    }
}
