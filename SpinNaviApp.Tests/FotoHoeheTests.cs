using Xunit;

namespace SpinNaviApp.Tests;

public class FotoHoeheTests
{
    [Fact]
    public void ParseFotos_liest_verortete_Fotos()
    {
        const string json = "[{\"lat\":52.5,\"lng\":13.4,\"url\":\"/m/a.jpg\",\"text\":\"Schloss\",\"tour\":\"Tour A\"}]";
        var f = FotoService.ParseFotos(json);
        Assert.Single(f);
        Assert.Equal(52.5, f[0].Lat, 3);
        Assert.Equal("Schloss", f[0].Text);
        Assert.Equal("Tour A", f[0].Tour);
    }

    [Fact]
    public void ParseFotos_ueberspringt_ohne_Koordinaten_und_robust_bei_Muell()
    {
        Assert.Empty(FotoService.ParseFotos("[{\"url\":\"/m/a.jpg\"}]"));
        Assert.Empty(FotoService.ParseFotos("<html>err</html>"));
        Assert.Empty(FotoService.ParseFotos("{\"x\":1}"));
    }

    [Fact]
    public void ParseHoehe_liest_range_height()
    {
        const string json = "{\"range_height\":[[0,34],[250,40],[500,28]]}";
        var h = HoeheService.ParseHoehe(json);
        Assert.Equal(3, h.Count);
        Assert.Equal(0, h[0].dist, 1);
        Assert.Equal(34, h[0].hoehe, 1);
        Assert.Equal(28, h[2].hoehe, 1);
    }

    [Fact]
    public void ParseHoehe_robust_bei_leer_und_Nicht_JSON()
    {
        Assert.Empty(HoeheService.ParseHoehe("{\"range_height\":[]}"));
        Assert.Empty(HoeheService.ParseHoehe("<html>502</html>"));
        Assert.Empty(HoeheService.ParseHoehe("{}"));
    }
}
