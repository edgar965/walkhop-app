using System.Diagnostics;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using NetTopologySuite.Geometries;

namespace WalkHop;

public partial class MainPage
{
    // ---- Routenvorschläge (Google-Stil) ------------------------------------
    // Bei „Navigation zu" werden bis zu drei Varianten berechnet und farbig auf der Karte gezeigt;
    // Antippen (Karte ODER Chip) wählt eine aus, „Start" navigiert die gewählte.
    //   • Vorschlag 1 (blau, RouteFarbeHex): wie in den Einstellungen (inkl. Offroad-Prozent).
    //   • Vorschlag 2 (violett): die schnellste/direkte Route.
    //   • Vorschlag 3 (orange): ein Mittelding (halber Offroad-Prozent).
    private const string VorschlagSchnellHex = "#7c3aed";   // violett – schnellste
    private const string VorschlagMittelHex = "#f59e0b";    // orange – Mittelding

    private sealed record Vorschlag(RouteErgebnis Route, string FarbeHex, string Label);
    private List<Vorschlag> _vorschlaege = new();
    private int _vorschlagWahl;

    /// <summary>Berechnet bis zu drei Routenvarianten (nur die erste zählt aufs Routen-Kontingent,
    /// die beiden Zusatzvarianten laufen als „Folge"). Geometrisch (nahezu) gleiche Varianten werden
    /// verworfen, sodass keine Doubletten gezeigt werden.</summary>
    private async Task<List<Vorschlag>> BerechneVorschlaege(double sLat, double sLon, double zLat, double zLon)
    {
        (string label, string farbe, string wegtyp, int offroad, bool folge)[] varianten =
        {
            (L.T("vorschlag_einst"),   RouteFarbeHex,       Einst.Wegtyp, Einst.OffroadProzent,     false),
            (L.T("vorschlag_schnell"), VorschlagSchnellHex, "neutral",    0,                         true),
            (L.T("vorschlag_mittel"),  VorschlagMittelHex,  Einst.Wegtyp, Einst.OffroadProzent / 2,  true),
        };
        // Alle Varianten parallel anfragen (unabhängige Requests) – spart Wartezeit gegenüber seriell.
        var aufgaben = varianten.Select(v =>
        {
            var opt = RouteService.CostingOptionen(Einst.Profil, v.wegtyp,
                Einst.VermeideAutobahn, Einst.VermeideUnbefestigt, Einst.VermeideSchlechteOberflaeche, v.offroad);
            return RouteService.RouteAsync(sLat, sLon, zLat, zLon, Einst.Profil, opt, Einst.NaviLocale, folge: v.folge);
        }).ToArray();

        var liste = new List<Vorschlag>();
        for (int i = 0; i < aufgaben.Length; i++)
        {
            RouteErgebnis? r = null;
            try { r = await aufgaben[i]; }
            catch (PaywallException) when (i == 0) { throw; }   // nur die erste (zählende) Variante löst die Paywall aus
            catch (Exception ex) { Debug.WriteLine(ex); }
            if (r != null && r.Punkte.Count >= 2 && !liste.Any(x => Aehnlich(x.Route, r)))
                liste.Add(new Vorschlag(r, varianten[i].farbe, varianten[i].label));
        }
        return liste;
    }

    /// <summary>Zwei Routen gelten als (nahezu) gleich, wenn Länge UND Geometrie an fünf Stützstellen
    /// dicht beieinander liegen – so verschwinden Doubletten, wenn eine Variante nichts Neues bringt.</summary>
    private static bool Aehnlich(RouteErgebnis a, RouteErgebnis b)
    {
        if (Math.Abs(a.Km - b.Km) > Math.Max(0.05, 0.03 * Math.Max(a.Km, b.Km))) return false;
        var ka = NavGeo.Kumulativ(a.Punkte);
        var kb = NavGeo.Kumulativ(b.Punkte);
        if (ka.Length < 2 || kb.Length < 2) return false;
        for (int i = 1; i <= 5; i++)
        {
            var pa = PunktBeiEntlang(a.Punkte, ka, ka[^1] * i / 6.0);
            var pb = PunktBeiEntlang(b.Punkte, kb, kb[^1] * i / 6.0);
            if (NavGeo.Haversine(pa.lat, pa.lon, pb.lat, pb.lon) > 40) return false;
        }
        return true;
    }

    /// <summary>Wählt einen Vorschlag als (Vorschau-)Route: setzt ihn als Nav-Route und zeichnet alle
    /// Varianten neu (gewählte hervorgehoben). „Start" navigiert danach genau diese Route.</summary>
    private void VorschlagWaehlen(int i, bool fit)
    {
        if (i < 0 || i >= _vorschlaege.Count) return;
        _vorschlagWahl = i;
        var v = _vorschlaege[i];
        _navMinuten = v.Route.Minuten;
        var ank = DateTime.Now.AddMinutes(v.Route.Minuten).ToString("HH:mm");
        NavStart(v.Route.Punkte, v.Route.Manoever,
            L.T("route_zusammenfassung", FmtKmVon(v.Route.Km), v.Route.Minuten), ank, fitKamera: fit, vorschlagModus: true);
        VorschlagChipsAktualisieren();
    }

    /// <summary>Zeichnet alle Vorschläge farbig in die Routen-Ebene: nicht gewählte dünn + halbtransparent
    /// (darunter), die gewählte kräftig + oben. Optional wird die Kamera auf die gewählte Route gefittet.</summary>
    private void VorschlaegeZeichnen(bool fit)
    {
        if (_vorschlaege.Count == 0)
        { _routeLayer.Features = new List<IFeature>(); _routeLayer.DataHasChanged(); return; }
        var features = new List<IFeature>();
        for (int i = 0; i < _vorschlaege.Count; i++) if (i != _vorschlagWahl) VorschlagFeatures(features, i, false);
        VorschlagFeatures(features, _vorschlagWahl, true);   // gewählte zuletzt → oben
        _routeLayer.Features = features;
        _routeLayer.DataHasChanged();
        if (fit) KameraAufPunkte(_vorschlaege[_vorschlagWahl].Route.Punkte);
    }

    private void VorschlagFeatures(List<IFeature> features, int i, bool gewaehlt)
    {
        var pts = _vorschlaege[i].Route.Punkte;
        var coords = new Coordinate[pts.Count];
        for (int j = 0; j < pts.Count; j++)
        { var (x, y) = ZuMercator(pts[j].lat, pts[j].lon); coords[j] = new Coordinate(x, y); }
        var casing = new GeometryFeature { Geometry = new LineString(coords) };
        casing.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#ffffff"), gewaehlt ? 11 : 8) });
        features.Add(casing);
        var farbe = gewaehlt ? Mapsui.Styles.Color.FromString(_vorschlaege[i].FarbeHex) : HexArgb(_vorschlaege[i].FarbeHex, 140);
        var line = new GeometryFeature { Geometry = new LineString(coords) };
        line.Styles.Add(new VectorStyle { Line = new Pen(farbe, gewaehlt ? 7 : 5) });
        features.Add(line);
    }

    // „#rrggbb" + Alpha → Mapsui-Farbe (ohne Abhängigkeit von Farb-Kanal-Properties).
    private static Mapsui.Styles.Color HexArgb(string hex, int alpha)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return Mapsui.Styles.Color.FromArgb(alpha, r, g, b);
    }

    /// <summary>Kamera auf eine Punktliste fitten (Bounding-Box + Rand), Ansicht um eine halbe Peek-Höhe
    /// nach oben schieben (die untere Schublade verdeckt sonst den Rand). Gemeinsam für Route + Vorschläge.</summary>
    private void KameraAufPunkte(List<(double lat, double lon)> pts)
    {
        if (pts.Count < 2) return;
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        foreach (var p in pts)
        {
            var (x, y) = ZuMercator(p.lat, p.lon);
            if (x < minx) minx = x; if (y < miny) miny = y;
            if (x > maxx) maxx = x; if (y > maxy) maxy = y;
        }
        double mx = (maxx - minx) * 0.15, my = (maxy - miny) * 0.15;
        if (mx <= 0) mx = 200; if (my <= 0) my = 200;
        _map.Navigator.ZoomToBox(new MRect(minx - mx, miny - my, maxx + mx, maxy + my));
        var vp = _map.Navigator.Viewport;
        if (vp.Resolution > 0 && vp.Height > 0)
        {
            double versatzPx = SheetPeek / 2.0 + 16;
            _map.Navigator.CenterOn(new MPoint(vp.CenterX, vp.CenterY - versatzPx * vp.Resolution));
        }
    }

    /// <summary>Nächstgelegener Vorschlag zu einem Geo-Punkt (Abstand zum Liniensegment), −1 wenn keiner
    /// in ~18 px Reichweite. Für die Antipp-Auswahl direkt auf der Karte.</summary>
    private int NaechsterVorschlag(double lat, double lon)
    {
        double res = _map.Navigator.Viewport.Resolution;
        if (res <= 0) return -1;
        double tol = res * Math.Cos(lat * Math.PI / 180) * 18;   // ~18 px in Meter
        int best = -1; double bestD = tol;
        for (int i = 0; i < _vorschlaege.Count; i++)
        {
            var pts = _vorschlaege[i].Route.Punkte;
            for (int j = 1; j < pts.Count; j++)
            {
                double d = NavGeo.DistanzZuSegment(lat, lon, pts[j - 1], pts[j]);
                if (d < bestD) { bestD = d; best = i; }
            }
        }
        return best;
    }

    // Kleine, antippbare Vorschlags-Chips (Farbe · Label · Zeit) in der Vorschau; nur bei >1 Variante.
    private void VorschlagChipsAktualisieren()
    {
        VorschlagChips.Children.Clear();
        if (_vorschlaege.Count <= 1 || _navAktiv) { VorschlagChips.IsVisible = false; return; }
        VorschlagChips.IsVisible = true;
        for (int i = 0; i < _vorschlaege.Count; i++)
        {
            int idx = i;
            var v = _vorschlaege[i];
            bool sel = i == _vorschlagWahl;
            var punkt = new Border
            {
                WidthRequest = 12, HeightRequest = 12, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb(v.FarbeHex), StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
            };
            var text = new Label
            {
                Text = $"{v.Label} · {FmtZeit(v.Route.Minuten)}", FontSize = 12, VerticalOptions = LayoutOptions.Center,
                FontAttributes = sel ? FontAttributes.Bold : FontAttributes.None,
                TextColor = Microsoft.Maui.Graphics.Color.FromArgb(sel ? "#0f172a" : "#64748b"),
            };
            var zeile = new HorizontalStackLayout { Spacing = 6 };
            zeile.Add(punkt); zeile.Add(text);
            var chip = new Border
            {
                Margin = new Thickness(4), Padding = new Thickness(10, 6), Content = zeile,
                BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb(sel ? "#e2e8f0" : "#f8fafc"),
                StrokeThickness = sel ? 2 : 0,
                Stroke = sel ? Microsoft.Maui.Graphics.Color.FromArgb(v.FarbeHex) : Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => VorschlagWaehlen(idx, fit: false);
            chip.GestureRecognizers.Add(tap);
            VorschlagChips.Children.Add(chip);
        }
    }

    // Vorschläge verwerfen (Navigation gestartet/beendet oder anderer Routen-Flow) + Chips ausblenden.
    private void VorschlaegeVerwerfen()
    {
        _vorschlaege = new();
        _vorschlagWahl = 0;
        if (VorschlagChips != null) VorschlagChips.IsVisible = false;
    }
}
