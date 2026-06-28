using Xunit;

namespace WalkHop.Tests;

public class PoiParseTests
{
    [Fact]
    public void ParsePoi_liest_Treffer()
    {
        const string json = "{\"poi\":[{\"name\":\"Museum\",\"lat\":52.5,\"lng\":13.4,\"kat\":\"Kultur\"}],\"gekappt\":false}";
        var t = PoiService.ParsePoi(json);
        Assert.Single(t);
        Assert.Equal("Museum", t[0].Name);
        Assert.Equal(52.5, t[0].Lat, 3);
        Assert.Equal(13.4, t[0].Lon, 3);
        Assert.Equal("Kultur", t[0].Kategorie);
    }

    [Fact]
    public void ParsePoi_ohne_poi_Schluessel_leer()
    {
        Assert.Empty(PoiService.ParsePoi("{\"x\":1}"));
    }

    [Fact]
    public void ParsePoi_ueberspringt_Eintraege_ohne_Koordinaten()
    {
        // Eintrag ohne lat/lng bzw. mit nicht-numerischen Koordinaten -> übersprungen.
        const string json = "{\"poi\":[{\"name\":\"Ohne\"},{\"name\":\"Müll\",\"lat\":\"x\",\"lng\":13.4}]}";
        Assert.Empty(PoiService.ParsePoi(json));
    }

    [Fact]
    public void ParsePoi_bei_Nicht_JSON_leere_Liste_statt_Crash()
    {
        Assert.Empty(PoiService.ParsePoi("<html>502</html>"));
        Assert.Empty(PoiService.ParsePoi(""));
    }
}
