using System.Diagnostics;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices.Sensors;
using NetTopologySuite.Geometries;

namespace WalkHop;

public partial class UebersichtPage
{
    // ---- Detail-Dialog --------------------------------------------------------
    private void DialogZeigen(TourInfo t)
    {
        _gewaehlt = t;
        bool generiert = t.Id < 0;   // selbst errechnete Rundwanderung: keine Server-Details (POIs/GPX/Termine)
        DlgKat.Text = generiert ? L.T("ue_gen_kat") : t.Kategorie;
        DlgName.Text = t.Name;
        string dauer = t.DauerMin >= 60 ? L.T("dlg_dauer_h", t.DauerMin / 60, t.DauerMin % 60) : L.T("dlg_dauer_min", t.DauerMin);
        DlgBadges.Text = L.T("dlg_badges", t.Km, dauer) + (string.IsNullOrEmpty(t.Grad) ? "" : L.T("dlg_badges_grad", t.Grad));
        DlgBeschr.Text = t.Beschreibung;
        DlgBeschr.IsVisible = !string.IsNullOrEmpty(t.Beschreibung);
        // Oben die Mini-Karte mit der GPS-Route zeigen (Web-Vorbild). Steht eine Karte,
        // bleibt das Titelbild aus (sonst zwei 180-px-Blöcke übereinander) – wie im Web
        // (Karte ODER Bild im Visual-Bereich).
        bool karteDa = DlgRouteZeichnen(t);
        if (!karteDa && !string.IsNullOrEmpty(t.Bild)) { DlgBild.Source = t.Bild; DlgBild.IsVisible = true; }
        else DlgBild.IsVisible = false;
        DlgExternGrid.IsVisible = !generiert;   // GPX/Termine nur für echte Server-Touren
        DlgOfflineBtn.IsVisible = karteDa;      // „Tour offline speichern" nur bei echter Route (≥2 Punkte)
        DlgPois.Clear();
        DlgPoiTitel.IsVisible = false;
        Dialog.IsVisible = true;
        // Jetzt ist der Dialog sichtbar → die Mini-Karte bekommt ihre Größe: erneut zoomen, damit
        // Kartenausschnitt + rote Route beim ERSTEN Öffnen zuverlässig erscheinen (zusätzlich zum
        // SizeChanged-Hook und der Selbst-Wiederholung in DlgZoomAnwenden).
        if (karteDa) DlgZoomAnwenden();
        if (!generiert) _ = PoisLaden(t.Id);       // echte Tour: POIs vom Server (per Id)
        else _ = GenFotosLaden(t.Route);           // berechnete Route: Fotos entlang der Route aus der Foto-Ebene
    }

    // „Tour offline speichern": Kacheln entlang der Routen-bbox (z11–16, gedeckelt) + die
    // verkleinerten Fotos ≤150 m an der Route. Zeigt vorher eine Größen-Schätzung zur Bestätigung.
    private async void OnDialogOffline(object? sender, EventArgs e)
    {
        var t = _gewaehlt;
        if (t == null || t.Route.Count < 2) return;
        DlgOfflineBtn.IsEnabled = false;
        try
        {
            try { if (_fotos.Count == 0) _fotos = await FotoService.LadeAsync(); }
            catch (Exception ex) { Debug.WriteLine(ex); }
            var schaetz = OfflineManager.SchaetzeTour(t.Route, _fotos);
            double mb = schaetz.Bytes / 1024.0 / 1024.0;
            bool los = await DisplayAlert(t.Name,
                L.T("region_schaetzung", schaetz.Kacheln, schaetz.Fotos, mb.ToString("0")),
                L.T("region_laden_btn"), L.T("abbrechen"));
            if (!los) return;
            var quelle = MapQuellen.Quelle(Einst.Karte);
            var prog = new Progress<(int done, int total, string phase)>(p =>
                MainThread.BeginInvokeOnMainThread(() =>
                    Status(L.T(p.phase == "fotos" ? "region_fortschritt_fotos" : "region_fortschritt_kacheln", p.done, p.total))));
            string tourId = t.Id > 0 ? t.Id.ToString()
                : $"gen-{t.Route[0].lat:F4}-{t.Route[0].lon:F4}";   // generierte Route: stabile Id aus dem Startpunkt
            var erg = await Task.Run(() => OfflineManager.LadeTourAsync(tourId, t.Name, t.Route, quelle, _fotos, prog));
            Status(null);
            if (erg.Ok)
                await DisplayAlert(L.T("offline_titel"),
                    L.T("offline_paket_fertig", t.Name, erg.Kacheln, erg.Fotos, (erg.Bytes / 1024.0 / 1024.0).ToString("0")), L.T("ok"));
            else
                await DisplayAlert(L.T("offline_laden_titel"), L.T("offline_fehler"), L.T("ok"));
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(null); }
        finally { DlgOfflineBtn.IsEnabled = true; }
    }

    // Sehenswürdigkeiten-Fotos, die NAH (≤150 m) an einer berechneten Route liegen, als POI-Zeilen
    // in den Dialog laden (die generierte Route hat keine Server-POIs). Quelle: die Foto-Punkte der
    // Übersichtskarte (bei Bedarf nachgeladen). Tipp auf ein Foto zeigt es groß (über PoiZeile).
    private async Task GenFotosLaden(List<(double lat, double lon)> route)
    {
        try { if (_fotos.Count == 0) _fotos = await FotoService.LadeAsync(); }
        catch (Exception ex) { Debug.WriteLine(ex); }
        if (_gewaehlt == null || _gewaehlt.Id >= 0) return;   // Dialog inzwischen gewechselt
        if (route.Count < 2 || _fotos.Count == 0) return;

        // grobe Vorfilterung per Bounding-Box (+ ~300 m Rand), dann Abstand zum Routen-Polygonzug.
        double minLa = 90, maxLa = -90, minLo = 180, maxLo = -180;
        foreach (var p in route)
        {
            if (p.lat < minLa) minLa = p.lat; if (p.lat > maxLa) maxLa = p.lat;
            if (p.lon < minLo) minLo = p.lon; if (p.lon > maxLo) maxLo = p.lon;
        }
        const double rand = 0.003;
        var treffer = new List<(FotoPunkt f, int dist)>();
        foreach (var f in _fotos)
        {
            if (f.Lat < minLa - rand || f.Lat > maxLa + rand || f.Lon < minLo - rand || f.Lon > maxLo + rand) continue;
            double best = double.MaxValue;
            for (int i = 1; i < route.Count; i++)
            {
                double d = NavGeo.DistanzZuSegment(f.Lat, f.Lon, route[i - 1], route[i]);
                if (d < best) best = d;
                if (best <= 40) break;
            }
            if (best <= 150) treffer.Add((f, (int)best));
        }
        if (treffer.Count == 0) return;
        treffer.Sort((a, b) => a.dist.CompareTo(b.dist));   // nächstgelegene zuerst

        DlgPoiTitel.IsVisible = true;
        var gesehen = new HashSet<string>();
        int n = 0;
        foreach (var (f, dist) in treffer)
        {
            if (!gesehen.Add(f.Url)) continue;   // dasselbe Foto nicht doppelt
            string name = !string.IsNullOrWhiteSpace(f.Text) ? f.Text : f.Tour;
            DlgPois.Add(PoiZeile(new TourPoi(name, f.Tour, f.Url, f.Lat, f.Lon, dist, f.Id)));
            if (++n >= 12) break;
        }
    }

    private async Task PoisLaden(int id)
    {
        try
        {
            var pois = await TourDetailService.PoisAsync(id);
            if (_gewaehlt?.Id != id || pois.Count == 0) return;   // Dialog evtl. schon gewechselt
            DlgPoiTitel.IsVisible = true;
            foreach (var p in pois.Take(12))
                DlgPois.Add(PoiZeile(p));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // Eine Sehenswürdigkeit-Zeile: Foto-Thumbnail (44×44, abgerundet) links neben Name + Distanz,
    // wie im Web-Detail-Dialog (.tdlg-poi). Bild-URL kommt aus details.json (relativ → ApiBase davor).
    private View PoiZeile(TourPoi p)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 4),
        };
        // Thumbnail in abgerundetem Rahmen (clippt das Bild auf runde Ecken)
        var rahmen = new Border
        {
            WidthRequest = 44, HeightRequest = 44, StrokeThickness = 0,
            BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#e2e8f0"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            VerticalOptions = LayoutOptions.Center,
        };
        if (!string.IsNullOrEmpty(p.Bild))
        {
            var bild = new Image { Aspect = Aspect.AspectFill, WidthRequest = 44, HeightRequest = 44 };
            try { bild.Source = Bildquelle(p.Id, p.Bild); } catch (Exception ex) { Debug.WriteLine(ex); }
            rahmen.Content = bild;
        }
        else   // kein Foto → Anfangsbuchstabe als Platzhalter (wie die .ph-Box im Web)
        {
            rahmen.Content = new Label
            {
                Text = string.IsNullOrEmpty(p.Name) ? "•" : p.Name.Substring(0, 1),
                FontAttributes = FontAttributes.Bold, FontSize = 16,
                TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#64748b"),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            };
        }
        Grid.SetColumn(rahmen, 0);

        var name = new Label
        {
            Text = p.Name, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#0f172a"),
        };
        var unter = p.Kategorie;
        if (p.DistM > 0) unter = string.IsNullOrEmpty(unter) ? L.T("poi_dist", p.DistM) : L.T("poi_dist_kat", unter, p.DistM);
        var texte = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        texte.Add(name);
        if (!string.IsNullOrEmpty(unter))
            texte.Add(new Label { Text = unter, FontSize = 11, TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#64748b") });
        Grid.SetColumn(texte, 1);

        grid.Children.Add(rahmen);
        grid.Children.Add(texte);
        // Tipp auf eine Sehenswürdigkeit mit Foto → das Bild groß in einem Popup zeigen (offline-fähig per Id).
        if (!string.IsNullOrEmpty(p.Bild))
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => _ = BildBetrachten(p.Bild, p.Name, p.Id, p.DistM);
            grid.GestureRecognizers.Add(tap);
        }
        return grid;
    }

    // Zeigt ein Bild (Sehenswürdigkeit) groß in einem eigenen Modal-Popup (offline-fähig per Foto-Id).
    private async Task BildBetrachten(string url, string titel, int id = 0, int distM = 0)
    {
        var bild = new Image { Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Fill };
        try { bild.Source = Bildquelle(id, url); } catch (Exception ex) { Debug.WriteLine(ex); }
        // Entfernung der Sehenswürdigkeit/des Fotos von der Route in die Bildunterschrift schreiben.
        string unterschrift = distM > 0 ? $"{titel}\n📍 {L.T("poi_dist", distM)}" : titel;
        var titelLabel = new Label
        {
            Text = unterschrift, TextColor = Microsoft.Maui.Graphics.Colors.White, FontSize = 14,
            Padding = new Thickness(14, 10), BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#003153"),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        var zuBtn = new Button
        {
            Text = L.T("schliessen"), BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#334155"),
            TextColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 10, Margin = new Thickness(12),
        };
        var grid = new Grid
        {
            RowDefinitions = { new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto },
                               new RowDefinition { Height = GridLength.Auto } },
            BackgroundColor = Microsoft.Maui.Graphics.Colors.Black,
        };
        Grid.SetRow(bild, 0); Grid.SetRow(titelLabel, 1); Grid.SetRow(zuBtn, 2);
        grid.Children.Add(bild); grid.Children.Add(titelLabel); grid.Children.Add(zuBtn);
        var seite = new ContentPage { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black, Content = grid };
        zuBtn.Clicked += async (s, e) => await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(seite);
    }

    // ---- Detail-Dialog: Mini-Karte mit der Tour-Route -------------------------
    // Baut die zweite Mapsui-Karte im Dialog einmalig auf (Tile-Basislayer wie die Hauptkarte
    // + eine Memory-Ebene für die Route). GPU-Rendering ist global aktiv – nichts extra nötig.
    private void DlgKarteVorbereiten()
    {
        if (_dlgMap != null) return;
        _dlgMap = new Mapsui.Map();
        // Schlichtes OSM (wie der Django-Dialog) – NICHT der Wander-/Topo-Stil, der vorhandene
        // Wander-/Radrouten als bunte Linien rendert (die als „andere Routen" erscheinen würden).
        _dlgMap.Layers.Add(new TileLayer(MapQuellen.Quelle(Kartenmodus.Standard)) { Name = "Basis" });
        // Style = null: kein Layer-Default-Symbol hinter den Start/Ziel-Punkten (grauer Kreis).
        _dlgRouteLayer = new MemoryLayer("DlgRoute") { Style = null };
        _dlgMap.Layers.Add(_dlgRouteLayer);
        DlgMap.Map = _dlgMap;
        DlgMap.UseDoubleTap = false;   // Tipps reagieren sofort (kein 200-ms-Doppeltipp-Warten)
        // Sobald die Dialog-Karte ihre Größe bekommt (beim ersten Öffnen ist sie noch 0 groß, weil
        // der Dialog gerade erst sichtbar wird), zuverlässig auf die Route zoomen – sonst bleibt der
        // obere Kartenbereich beim ersten Öffnen leer (betraf v. a. die berechneten Routen).
        DlgMap.SizeChanged += (s, e) => DlgZoomAnwenden();
    }

    // Zeichnet die Route der Tour in die Dialog-Karte und zoomt darauf. Liefert true, wenn eine
    // Karte gezeigt wird (Route ≥ 2 Punkte), sonst false (dann blendet DialogZeigen das Titelbild ein).
    private bool DlgRouteZeichnen(TourInfo t)
    {
        if (t.Route.Count < 2) { DlgMap.IsVisible = false; _dlgBox = null; return false; }
        DlgKarteVorbereiten();
        DlgMap.IsVisible = true;
        var coords = new Coordinate[t.Route.Count];
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        for (int i = 0; i < t.Route.Count; i++)
        {
            var (x, y) = SphericalMercator.FromLonLat(t.Route[i].lon, t.Route[i].lat);
            coords[i] = new Coordinate(x, y);
            if (x < minx) minx = x; if (x > maxx) maxx = x;
            if (y < miny) miny = y; if (y > maxy) maxy = y;
        }
        var linie = new GeometryFeature { Geometry = new LineString(coords) };
        // NUR die ausgewählte Route, in ROT (wie der Django-Dialog) – nicht in der Tour-Farbe.
        linie.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#dc2626"), 4) { PenStyle = PenStyle.Solid } });
        var feats = new List<IFeature>
        {
            linie,
            DlgPunkt(t.Route[0], "#15803d"),    // Start grün
            DlgPunkt(t.Route[^1], "#dc2626"),   // Ziel rot
        };
        _dlgRouteLayer.Features = feats;
        _dlgRouteLayer.DataHasChanged();
        // Etwas Rand um die Route lassen (12 % + 50 m), damit Start/Ziel nicht am Kartenrand kleben.
        double padx = (maxx - minx) * 0.12 + 50, pady = (maxy - miny) * 0.12 + 50;
        _dlgBox = new MRect(minx - padx, miny - pady, maxx + padx, maxy + pady);
        // Zoomen, sobald die Mini-Karte eine Größe hat: beim ERSTEN Öffnen ist sie noch 0 px groß
        // (der Dialog wird gerade erst sichtbar). DlgZoomAnwenden wiederholt sich selbst, bis die
        // Karte gemessen ist; zusätzlich löst der SizeChanged-Hook den Zoom beim Sichtbarwerden aus.
        DlgZoomAnwenden();
        return true;
    }

    // Zoomt die Dialog-Mini-Karte auf die Routen-Box. Robust gegen den Fall, dass die MapControl
    // beim ersten Öffnen noch KEINE Größe hat: ohne gültige Viewport-Größe würde ZoomToBox eine
    // kaputte (unendliche) Auflösung erzeugen → leere Karte. Darum erst zoomen, wenn die Karte
    // gemessen ist; sonst kurz später erneut versuchen (begrenzte Versuchszahl). Dieser Schutz fängt
    // auch den SizeChanged-Hook ab, der beim Schließen (Größe → 0) sonst die Auflösung zerstören würde.
    private void DlgZoomAnwenden(int versuche = 8)
    {
        if (_dlgMap == null || _dlgBox == null) return;
        var vp = _dlgMap.Navigator.Viewport;
        if (vp.Width < 1 || vp.Height < 1)
        {
            if (versuche > 0)
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () => DlgZoomAnwenden(versuche - 1));
            return;
        }
        try { _dlgMap.Navigator.ZoomToBox(_dlgBox); _dlgMap.RefreshGraphics(); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // Start-/Ziel-Punkt (gefüllter Kreis mit weißem Rand) für die Dialog-Mini-Karte.
    private static GeometryFeature DlgPunkt((double lat, double lon) p, string hex)
    {
        var (x, y) = SphericalMercator.FromLonLat(p.lon, p.lat);
        var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(x, y) };
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse, SymbolScale = 0.5,
            Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString(hex)),
            Outline = new Pen(Mapsui.Styles.Color.White, 2),
        });
        return f;
    }

    private void OnDialogZu(object? sender, EventArgs e) => Dialog.IsVisible = false;

    private async void OnDialogGpx(object? sender, EventArgs e)
    {
        if (_gewaehlt == null) return;
        try { await Launcher.OpenAsync(AppConfig.ApiBase + $"/ausfluege/{_gewaehlt.Id}/route.gpx"); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private async void OnDialogTermine(object? sender, EventArgs e)
    {
        if (_gewaehlt == null || string.IsNullOrEmpty(_gewaehlt.DetailUrl)) return;
        var url = _gewaehlt.DetailUrl.StartsWith("http") ? _gewaehlt.DetailUrl : AppConfig.ApiBase + _gewaehlt.DetailUrl;
        try { await Launcher.OpenAsync(url); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private async void OnDialogNavigieren(object? sender, EventArgs e)
    {
        if (_gewaehlt == null) return;
        MainPage.GeplanteTour = _gewaehlt;
        Dialog.IsVisible = false;
        await Shell.Current.GoToAsync("//navigation");
    }
}
