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
    private async Task ErstLadenAsync()
    {
        try
        {
            _alle = await TourService.LadeTourenAsync();   // Fetch + Parse (Parse läuft off-thread)
            if (_fotoAn && _fotos.Count == 0)              // Fotos beim Start aktiviert → vor dem Zeichnen laden
            {
                try { _fotos = await FotoService.LadeAsync(); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
            Anwenden();                                    // Filtern + Zeichnen ebenfalls off-thread
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("ue_st_touren_fehler")); }
    }

    // ---- Filter / Zeichnen ----------------------------------------------------
    // Filtern UND Feature-Bau laufen vollständig im Hintergrund-Task; nur das Setzen der
    // Layer-Features + DataHasChanged passiert auf dem UI-Thread. So bleibt die Karte beim
    // Öffnen und bei jedem Filter flüssig (kein UI-Thread-Block → kein ANR).
    // statusNachher: Text, der nach dem Zeichnen angezeigt wird (null = ausblenden).
    private int _anwendenSeq;
    private void Anwenden(string? statusNachher = null)
    {
        int seq = ++_anwendenSeq;
        // Filter-Kriterien + UI-Eigenschaften auf dem UI-Thread abgreifen, dann im Hintergrund arbeiten.
        var alle = _alle;
        string suche = _suche, facette = _facette;
        bool nurBahnhof = _nurBahnhof, umkreis = _umkreis;
        var zentrum = _zentrum; int radius = _radiusKm;
        bool listeSichtbar = ListePanel.IsVisible;
        _ = Task.Run(() =>
        {
            IEnumerable<TourInfo> q = alle;
            if (!string.IsNullOrEmpty(suche))
                q = q.Where(t => t.Name.Contains(suche, StringComparison.OrdinalIgnoreCase));
            if (nurBahnhof)                                   // Checkbox „Bahnhof am Start"
                q = q.Where(t => t.Bahn);
            if (!string.IsNullOrEmpty(facette))
                q = q.Where(t => t.Facetten.Contains(facette));
            if (umkreis && zentrum is { } z)                  // bei Umkreis: nach Entfernung sortieren
                q = q.Where(t => ImUmkreis(t, z, radius)).OrderBy(t => Entfernung(t, z));
            var gefiltert = q.ToList();
            var features = BaueTourFeatures(gefiltert.Take(MaxRouten).ToList());
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (seq != _anwendenSeq) return;   // ein neuerer Filterlauf ist unterwegs → verwerfen
                // NUR die tatsächlich gezeichneten Routen als „gefiltert" merken (gleicher Deckel wie beim
                // Zeichnen). Sonst matcht NaechsteTourRoute auch NICHT gezeichnete Routen (>MaxRouten) und
                // öffnet ein Popup zu einer Route, die gar nicht auf der Karte liegt.
                _gefiltert = gefiltert.Take(MaxRouten).ToList();
                _tourLayer.Features = features;
                _tourLayer.DataHasChanged();
                // Filter/Suche sichtbar machen: Die Zoom-Glättung deaktiviert _tourLayer (und Fotos)
                // kurzzeitig. Wäre der Layer genau jetzt noch deaktiviert, bliebe die Filteränderung
                // UNSICHTBAR (die Karte zeigt weiter die alten Routen). Darum die Vektor-Layer sicher
                // wieder aktivieren und einmal neu rendern – ein Filterlauf ist nie eine Zoom-Geste.
                _vektorenVerborgen = false;
                _tourLayer.Enabled = true;
                _fotoLayer.Enabled = _fotoAn;
                UmkreisKreisZeichnen();
                FotoFilterAnwenden();
                if (listeSichtbar) ListePanel.ItemsSource = gefiltert.Take(MaxRouten).ToList();
                Status(statusNachher);
                _map.RefreshGraphics();
            });
        });
    }

    private static bool ImUmkreis(TourInfo t, (double lat, double lon) z, int radiusKm)
    {
        double r = radiusKm * 1000.0;
        if (t.Start is { } s && NavGeo.Haversine(z.lat, z.lon, s.lat, s.lon) <= r) return true;
        for (int i = 0; i < t.Route.Count; i += 2)
            if (NavGeo.Haversine(z.lat, z.lon, t.Route[i].lat, t.Route[i].lon) <= r) return true;
        return false;
    }

    private static double Entfernung(TourInfo t, (double lat, double lon) z)
    {
        var p = t.Start ?? (t.Route.Count > 0 ? t.Route[0] : z);
        return NavGeo.Haversine(z.lat, z.lon, p.lat, p.lon);
    }

    // Sichtbarer Umkreis-Kreis (blau, gestrichelt) wie in der Web-Übersicht.
    private void UmkreisKreisZeichnen()
    {
        if (!_umkreis || _zentrum is not { } z)
        {
            _kreisLayer.Features = new List<IFeature>();
            _kreisLayer.DataHasChanged();
            return;
        }
        double rKm = _radiusKm, dLat = rKm / 111.0, dLon = rKm / (111.0 * Math.Cos(z.lat * Math.PI / 180));
        var ring = new Coordinate[49];
        for (int i = 0; i <= 48; i++)
        {
            double w = i / 48.0 * 2 * Math.PI;
            var (x, y) = SphericalMercator.FromLonLat(z.lon + dLon * Math.Cos(w), z.lat + dLat * Math.Sin(w));
            ring[i] = new Coordinate(x, y);
        }
        ring[48] = ring[0];   // Ring exakt schließen (NTS verlangt identischen Start/Endpunkt)
        var f = new GeometryFeature { Geometry = new Polygon(new LinearRing(ring)) };
        f.Styles.Add(new VectorStyle
        {
            Line = new Pen(Mapsui.Styles.Color.FromString("#1a56db"), 2) { PenStyle = PenStyle.Dash },
            Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromArgb(15, 26, 86, 219)),
        });
        _kreisLayer.Features = new List<IFeature> { f };
        _kreisLayer.DataHasChanged();
    }

    // Reine Daten-/Geometrie-Erzeugung (keine UI) → läuft im Hintergrund-Task.
    private static List<IFeature> BaueTourFeatures(List<TourInfo> touren)
    {
        var features = new List<IFeature>();
        foreach (var t in touren)
        {
            if (t.Route.Count < 2)   // Start-only-Tour → Marker am Startpunkt
            {
                if (t.Start is not { } s) continue;
                var (mx, my) = SphericalMercator.FromLonLat(s.lon, s.lat);
                var mf = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(mx, my) };
                mf.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Ellipse, SymbolScale = 0.55,
                    Fill = new Mapsui.Styles.Brush(KarteHelfer.Farbe(t.Farbe)),
                    Outline = new Pen(Mapsui.Styles.Color.White, 2),
                });
                features.Add(mf);
                continue;
            }
            // Übersichts-Linie stark ausdünnen: nur ~12 Punkte je Tour (Start/Ende behalten).
            // Die volle Geometrie zeichnet erst die Navi-Seite. So bleibt Verschieben flüssig
            // (statt 250 × 337 = ~54.000 nur noch ~3.000 Punkte gesamt). Beim Zoom werden die
            // Vektoren ohnehin kurz ausgeblendet (BeiViewportAenderung).
            const int maxPunkte = 36;   // feiner als zuvor (12) → die Linie folgt der Route besser,
            var route = t.Route;        //   damit ein Tipp auf die sichtbare Linie auch die richtige Route trifft
            int schritt = route.Count <= maxPunkte ? 1 : (int)Math.Ceiling(route.Count / (double)maxPunkte);
            var liste = new List<Coordinate>(maxPunkte + 2);
            for (int i = 0; i < route.Count; i += schritt)
            {
                var (x, y) = SphericalMercator.FromLonLat(route[i].lon, route[i].lat);
                liste.Add(new Coordinate(x, y));
            }
            if (schritt > 1 && (route.Count - 1) % schritt != 0)   // Endpunkt sicher behalten
            {
                var (xe, ye) = SphericalMercator.FromLonLat(route[^1].lon, route[^1].lat);
                liste.Add(new Coordinate(xe, ye));
            }
            var coords = liste.ToArray();
            bool quali = t.Facetten.Contains("qualitytour");
            var f = new GeometryFeature { Geometry = new LineString(coords) };
            f.Styles.Add(new VectorStyle
            {
                Line = new Pen(KarteHelfer.Farbe(t.Farbe), quali ? 5 : 3) { PenStyle = PenStyle.Solid }
            });
            features.Add(f);
        }
        return features;
    }

    // Wanderungs-Generator: erzeugt 5 farbige Rundtour-Vorschläge ab dem Punkt (bevorzugt
    // Foto-Sehenswürdigkeiten) und bietet sie zum Abwandern an.
    private async Task NeueWanderung(double lat, double lon)
    {
        string opt5 = L.T("nw_5km"), opt10 = L.T("nw_10km"), opt15 = L.T("nw_15km"), opt20 = L.T("nw_20km_rad");
        string dWahl = await DisplayActionSheet(L.T("nw_titel"), L.T("abbrechen"), null, opt5, opt10, opt15, opt20);
        if (string.IsNullOrEmpty(dWahl) || dWahl == L.T("abbrechen")) return;
        double km; string costing;
        if (dWahl == opt5) { km = 5; costing = "pedestrian"; }
        else if (dWahl == opt10) { km = 10; costing = "pedestrian"; }
        else if (dWahl == opt15) { km = 15; costing = "pedestrian"; }
        else { km = 20; costing = "bicycle"; }

        Status(L.T("nw_erzeuge", km));
        var vorschlaege = await WanderGenService.ErzeugeAsync(lat, lon, km, costing);
        Status(null);
        if (vorschlaege.Count == 0) { StatusKurz(L.T("nw_keine"), 4); return; }
        _genWanderungen = vorschlaege;
        _genCosting = costing;                 // Modus merken (für Dialog/Navigation der generierten Routen)
        GenLoeschenBtn.IsVisible = true;       // jetzt liegen berechnete Routen → Lösch-Knopf zeigen

        var features = new List<IFeature>();
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        foreach (var w in vorschlaege)
        {
            var coords = new Coordinate[w.Route.Count];
            for (int i = 0; i < w.Route.Count; i++)
            {
                var (x, y) = SphericalMercator.FromLonLat(w.Route[i].lon, w.Route[i].lat);
                coords[i] = new Coordinate(x, y);
                if (x < minx) minx = x; if (x > maxx) maxx = x;
                if (y < miny) miny = y; if (y > maxy) maxy = y;
            }
            var f = new GeometryFeature { Geometry = new LineString(coords) };
            f.Styles.Add(new VectorStyle { Line = new Pen(KarteHelfer.Farbe(w.Farbe), 5) { PenStyle = PenStyle.Solid } });
            features.Add(f);
        }
        _genLayer.Features = features;
        _genLayer.DataHasChanged();
        if (minx <= maxx) _map.Navigator.ZoomToBox(new MRect(minx, miny, maxx, maxy));
        _map.RefreshGraphics();
        // KEIN „Welche Wanderung?"-Popup und KEINE automatische Navigation mehr: alle berechneten
        // Routen bleiben einfach auf der Karte liegen (der Lösch-Knopf ist eingeblendet). Der Nutzer
        // tippt selbst eine Route an und öffnet damit deren Detail-Dialog (siehe OnKarteTipp/LongTap).
    }

    // Eine generierte Rundwanderung → TourInfo (Id = -1 markiert „selbst errechnet": keine
    // Server-Details wie POIs/GPX/Termine). Für Detail-Dialog UND Navigation genutzt.
    private TourInfo GenZuTour(GenWanderung w) => new TourInfo(
        -1, w.Name, w.Km, w.DauerMin,
        _genCosting == "bicycle" ? "radtour" : "wanderung", w.Route,
        new List<string> { "rundtour" }, w.Farbe, w.Route.Count > 0 ? w.Route[0] : default,
        "", "", "", false, "");

    // Nächstgelegene selbst errechnete Rundwanderung zum Tipp-Punkt (null, wenn keine in ~12 px).
    private GenWanderung? NaechsteGenWanderung(double lat, double lon)
    {
        if (_genWanderungen.Count == 0) return null;
        double res = _map.Navigator.Viewport.Resolution;
        if (res <= 0) return null;
        double mProPixel = res * Math.Cos(lat * Math.PI / 180);
        double tol = mProPixel * 12;
        GenWanderung? best = null;
        double bestD = tol;
        foreach (var w in _genWanderungen)
        {
            if (w.Route.Count < 2) continue;
            for (int i = 1; i < w.Route.Count; i++)
            {
                double d = NavGeo.DistanzZuSegment(lat, lon, w.Route[i - 1], w.Route[i]);
                if (d < bestD) { bestD = d; best = w; }
            }
        }
        return best;
    }

    // „Berechnete Routen löschen": entfernt alle generierten Rundwanderungen von der Karte.
    private void OnGenLoeschen(object? sender, EventArgs e)
    {
        _genWanderungen = new List<GenWanderung>();
        _genLayer.Features = new List<IFeature>();
        _genLayer.DataHasChanged();
        GenLoeschenBtn.IsVisible = false;
        // Sauberen, antippbaren Zustand herstellen, damit ein Tipp auf die freie Karte sofort wieder
        // das Kontextmenü zeigt (neue Wanderung ab Punkt): evtl. hängenden Langdruck-Block lösen,
        // keine „gewählte" Tour mehr halten und die Vektor-Layer (Touren/Fotos) sicher reaktivieren –
        // die Zoom-Glättung könnte _tourLayer sonst deaktiviert zurückgelassen haben.
        _letztLangdruckMs = 0;
        _gewaehlt = null;
        _vektorenVerborgen = false;
        _tourLayer.Enabled = true;
        _fotoLayer.Enabled = _fotoAn;
        _map.RefreshGraphics();
        StatusKurz(L.T("ue_gen_geloescht"), 3);
    }

    private async void OnOrtSuche(object? sender, EventArgs e)
    {
        string q = (OrtEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q)) return;
        Status(L.T("ue_st_ort_suchen"));
        try
        {
            var treffer = await GeocodeService.SucheAsync(q);
            if (treffer.Count == 0) { Status(L.T("ue_st_ort_nicht_gefunden")); return; }
            var o = treffer[0];
            var (x, y) = SphericalMercator.FromLonLat(o.Lon, o.Lat);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), Aufloesung(12));
            _zentrum = (o.Lat, o.Lon);
            MittelpunktSetzen(o.Name);
            UmkreisCheck.IsChecked = true;   // aktiviert Umkreis (CheckedChanged → Anwenden)
            Anwenden(L.T("ue_ort_gefunden", o.Name, _radiusKm));   // Status erst NACH dem Zeichnen setzen (kein Überschreiben)
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("ue_st_ortssuche_fehlgeschlagen")); }
    }

    // ---- Suchzeile ------------------------------------------------------------
    private void OnSuche(object? sender, TextChangedEventArgs e)
    {
        _suche = (e.NewTextValue ?? "").Trim();
        var meins = _suche;   // Debounce: erst nach 280 ms ohne weitere Eingabe filtern/zeichnen
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(280), () => { if (_suche == meins) Anwenden(); });
    }

    // Kompakter Suchmodus (Einst.KompakteSuche): ist er aktiv, zeigt der Peek statt des dauerhaft
    // sichtbaren Suchfelds nur ein Such-Symbol; ein Tipp darauf blendet das Suchfeld ein. Wird beim
    // Erscheinen der Seite angewandt, damit eine Änderung in den Einstellungen sofort greift.
    private void KompakteSucheAnwenden()
    {
        bool kompakt = Einst.KompakteSuche;
        Suchfeld.IsVisible = !kompakt;
        KompaktSuchBtn.IsVisible = kompakt;
    }

    // Tipp auf das Such-Symbol: Suchfeld einblenden (und fokussieren), Symbol ausblenden.
    private void OnKompaktSuche(object? sender, EventArgs e)
    {
        KompaktSuchBtn.IsVisible = false;
        Suchfeld.IsVisible = true;
        try { Suchfeld.Focus(); } catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // ---- Chips ----------------------------------------------------------------
    private void ChipsBauen()
    {
        foreach (var (key, txtKey) in FacettenChips)
        {
            var b = new Button
            {
                Text = L.T(txtKey), FontSize = 13, Padding = new Thickness(14, 6), CornerRadius = 16,
                CommandParameter = key,
                AutomationId = "osm_chip_" + (string.IsNullOrEmpty(key) ? "alle" : key.Trim('_')),
            };
            b.Clicked += OnChip;
            _chips[key] = b;
            ChipLeiste.Add(b);
        }
        ChipsMarkieren();
    }

    // Chip-Beschriftungen bei Sprachwechsel neu setzen (die Chips werden im Code erzeugt,
    // daher keine automatische {loc:Translate}-Aktualisierung).
    private void ChipsTexteSetzen()
    {
        foreach (var (key, txtKey) in FacettenChips)
            if (_chips.TryGetValue(key, out var b)) b.Text = L.T(txtKey);
    }

    private void OnChip(object? sender, EventArgs e)
    {
        if (sender is Button b) { _facette = b.CommandParameter as string ?? ""; ChipsMarkieren(); Anwenden(); }
    }

    private void ChipsMarkieren()
    {
        foreach (var (key, b) in _chips)
        {
            bool aktiv = key == _facette;
            b.BackgroundColor = aktiv ? Microsoft.Maui.Graphics.Color.FromArgb("#1a56db") : Microsoft.Maui.Graphics.Color.FromArgb("#ffffff");
            b.TextColor = aktiv ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Color.FromArgb("#5b6470");
        }
    }

    // ---- Umkreis / Bahnhof (Checkboxen im Aufklapp-Fenster) -------------------
    private void OnUmkreisCheck(object? sender, CheckedChangedEventArgs e)
    {
        _umkreis = e.Value;
        if (_umkreis && _zentrum == null)
        {
            var (lon, lat) = SphericalMercator.ToLonLat(_map.Navigator.Viewport.CenterX, _map.Navigator.Viewport.CenterY);
            _zentrum = (lat, lon);
            MittelpunktSetzen(L.T("mittelpunkt_kartenmitte"));
        }
        Anwenden();
    }

    private void OnBahnhof(object? sender, CheckedChangedEventArgs e)
    {
        _nurBahnhof = e.Value;
        Anwenden();
    }

    private void MittelpunktSetzen(string name)
    {
        if (MittelpunktLabel != null) MittelpunktLabel.Text = L.T("mittelpunkt_label", name);
    }

    // ---- Aufklapp-Fenster (maps.me-Stil): halbhoch, Griff tippen = zu/auf, wischen = ziehen ----
    private double _sheetHoehe;
    private const double SheetPeek = 100;   // sichtbarer Peek (Griff + Suchfeld)
    private bool _sheetOffen;
    private double _panBasis;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (height > 0)
        {
            _sheetHoehe = height * 0.58;
            StartSheet.HeightRequest = _sheetHoehe;
            if (!_sheetOffen) StartSheet.TranslationY = _sheetHoehe - SheetPeek;
        }
    }

    private void SheetSetzen(bool offen, bool animiert = true)
    {
        _sheetOffen = offen;
        double ziel = offen ? 0 : Math.Max(0, _sheetHoehe - SheetPeek);
        if (animiert) _ = StartSheet.TranslateTo(0, ziel, 220, Easing.CubicOut);
        else StartSheet.TranslationY = ziel;
    }

    private void OnStartSheet(object? sender, EventArgs e) => SheetSetzen(!_sheetOffen);

    private void OnStartSheetPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panBasis = StartSheet.TranslationY;
                break;
            case GestureStatus.Running:
                StartSheet.TranslationY = Math.Clamp(_panBasis + e.TotalY, 0, Math.Max(0, _sheetHoehe - SheetPeek));
                break;
            case GestureStatus.Completed:
                SheetSetzen(StartSheet.TranslationY < (_sheetHoehe - SheetPeek) / 2);
                break;
        }
    }

    private void RadiusBauen()
    {
        foreach (var km in RadiusPresets)
        {
            var b = new Button
            {
                Text = $"{km} km", FontSize = 12, Padding = new Thickness(10, 4), CornerRadius = 14,
                CommandParameter = km,
                AutomationId = $"osm_radius_{km}",
            };
            b.Clicked += OnRadius;
            _radiusBtns[km] = b;
            RadiusChips.Add(b);
        }
        RadiusMarkieren();
    }

    private void OnRadius(object? sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is int km) { _radiusKm = km; RadiusMarkieren(); Anwenden(); }
    }

    private void RadiusMarkieren()
    {
        foreach (var (km, b) in _radiusBtns)
        {
            bool aktiv = km == _radiusKm;
            b.BackgroundColor = aktiv ? Microsoft.Maui.Graphics.Color.FromArgb("#1a56db") : Microsoft.Maui.Graphics.Color.FromArgb("#f1f5f9");
            b.TextColor = aktiv ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Color.FromArgb("#334155");
        }
    }
}
