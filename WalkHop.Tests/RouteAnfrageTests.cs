using System.Text.Json;
using Xunit;

namespace WalkHop.Tests;

public class RouteAnfrageTests
{
    [Fact]
    public void CostingOptionen_Fuss_Neutral_ist_leer()
    {
        var o = RouteService.CostingOptionen("pedestrian", "neutral", true, true, true);
        Assert.Empty(o);
    }

    [Fact]
    public void CostingOptionen_Fuss_Offroad_und_Befestigt()
    {
        var natur = RouteService.CostingOptionen("pedestrian", "natur", false, false, false);
        Assert.Equal(1.0, (double)natur["use_tracks"]);
        Assert.Equal(0.5, (double)natur["walkway_factor"]);

        var fest = RouteService.CostingOptionen("pedestrian", "fest", false, false, false);
        Assert.Equal(0.0, (double)fest["use_tracks"]);
        Assert.Equal(1.0, (double)fest["walkway_factor"]);
    }

    [Fact]
    public void CostingOptionen_Auto_Vermeidungen()
    {
        var an = RouteService.CostingOptionen("auto", "", true, true, false);
        Assert.Equal(0.25, (double)an["use_highways"]);   // Autobahn vermeiden
        Assert.Equal(0.0, (double)an["use_tracks"]);      // unbefestigt vermeiden

        var aus = RouteService.CostingOptionen("auto", "", false, false, false);
        Assert.Equal(1.0, (double)aus["use_highways"]);
        Assert.Equal(0.5, (double)aus["use_tracks"]);
    }

    [Fact]
    public void CostingOptionen_Rad_setzt_Oberflaeche()
    {
        var an = RouteService.CostingOptionen("bicycle", "", false, false, true);
        Assert.Equal(0.4, (double)an["use_roads"]);
        Assert.Equal("Hybrid", (string)an["bicycle_type"]);
        Assert.Equal(0.8, (double)an["avoid_bad_surfaces"]);

        var aus = RouteService.CostingOptionen("bicycle", "", false, false, false);
        Assert.Equal(0.0, (double)aus["avoid_bad_surfaces"]);
    }

    [Fact]
    public void BaueAnfrageJson_enthaelt_costing_options_und_Sprache()
    {
        var opt = RouteService.CostingOptionen("bicycle", "", false, false, true);
        var json = RouteService.BaueAnfrageJson(52.52, 13.40, 52.50, 13.42, "bicycle", opt, "en-US");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("bicycle", root.GetProperty("costing").GetString());
        Assert.Equal("en-US", root.GetProperty("language").GetString());
        Assert.Equal(2, root.GetProperty("locations").GetArrayLength());
        Assert.Equal(52.52, root.GetProperty("locations")[0].GetProperty("lat").GetDouble(), 3);
        // costing_options flach (Server verschachtelt unter costing)
        Assert.Equal(0.8, root.GetProperty("costing_options").GetProperty("avoid_bad_surfaces").GetDouble(), 3);
    }

    [Fact]
    public void BaueAnfrageJson_setzt_alternates()
    {
        var json = RouteService.BaueAnfrageJson(52.5, 13.4, 52.4, 13.3, "pedestrian",
            new System.Collections.Generic.Dictionary<string, object>(), "de-DE", 2);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("alternates").GetInt32());
    }

    [Fact]
    public void ParseAlternativen_liest_alternate_Trips()
    {
        const string roh = "{\"trip\":{\"legs\":[{\"shape\":\"_p~iF~ps|U_ulLnnqC\",\"maneuvers\":[]}],\"summary\":{\"length\":3,\"time\":600}}," +
            "\"alternates\":[{\"trip\":{\"legs\":[{\"shape\":\"_p~iF~ps|U_ulLnnqC\",\"maneuvers\":[]}],\"summary\":{\"length\":4,\"time\":700}}}]}";
        var alt = RouteService.ParseAlternativen(roh);
        Assert.Single(alt);
        Assert.Equal(4.0, alt[0].Km, 2);
    }

    [Fact]
    public void ParseAlternativen_leer_wenn_keine()
    {
        Assert.Empty(RouteService.ParseAlternativen("{\"trip\":{\"legs\":[]}}"));
        Assert.Empty(RouteService.ParseAlternativen("<html>x</html>"));
    }
}
