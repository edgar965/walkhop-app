using System.Diagnostics;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using NetTopologySuite.Geometries;

namespace SpinNaviApp;

public partial class UebersichtPage : ContentPage
{
    private const double WELT0 = 156543.03392804097;
    private const int MaxRouten = 250;   // Obergrenze gezeichneter Routen (Performance)
    private const int ZentrierZoom = 14; // Zoomstufe beim Zentrieren auf den Live-Standort

    private static readonly (string key, string label)[] FacettenChips =
    {
        ("", "Alle"), ("stadt", "🏙 Stadt"), ("see", "🌊 See & Wasser"),
        ("wandern_kurz", "🥾 bis 5 km"), ("wandern_mittel", "🥾 5–15 km"), ("wandern_lang", "🥾 ab 15 km"),
        ("wandern", "🥾 Wandern"), ("radtour", "🚴 Radtour"), ("rundtour", "🔄 Rundtour"),
        ("bahnanreise", "🚉 Bahn"), ("qualitytour", "⭐ QualityTour"), ("edgars", "🧭 Edgars"),
    };
    private static readonly int[] RadiusPresets = { 5, 15, 30, 60 };

    private Mapsui.Map _map = null!;
    private TileLayer _basisLayer = null!;
    private MemoryLayer _tourLayer = null!;
    private MemoryLayer _fotoLayer = null!;
    private MemoryLayer _kreisLayer = null!;
    private MemoryLayer _markerLayer = null!;
    private readonly Dictionary<string, Button> _chips = new();
    private readonly Dictionary<int, Button> _radiusBtns = new();

    private List<TourInfo> _alle = new();
    private List<TourInfo> _gefiltert = new();
    private List<FotoPunkt> _fotos = new();
    private string _facette = "";
    private string _suche = "";
    private bool _umkreis, _fotoAn, _geladen, _nurBahnhof;
    private (double lat, double lon)? _zentrum;
    private int _radiusKm = 20;
    private Kartenmodus _modus = Kartenmodus.Wandern;
    private TourInfo? _gewaehlt;

    // ---- Live-Standort (Beam) + Kompass (wie Navigation-Karte) ----
    private MemoryLayer _posLayer = null!;
    private MPoint? _letztePos;
    private (double lat, double lon)? _letzteGeo;
    private double _heading, _gpsKurs, _gezeichnetHeading = -999;
    private (double lat, double lon)? _letzteKursGeo;   // letzte Position für die Kursberechnung aus Bewegung
    private bool _kompassHatWert;
    private bool _folgen, _fahrtrichtung;
    private bool _zentrierenNaechsterFix = true;   // erster GPS-Fix → auf Position zentrieren (Norden oben)
    private bool _gpsLaeuft, _kompassLaeuft, _positionsSchleifeLaeuft;
    private int _beamBitmapId = -1;

    public UebersichtPage()
    {
        InitializeComponent();

        _map = new Mapsui.Map();
        _basisLayer = new TileLayer(MapQuellen.Quelle(_modus)) { Name = "Basis" };
        _map.Layers.Add(_basisLayer);
        _kreisLayer = new MemoryLayer("Umkreis");
        _map.Layers.Add(_kreisLayer);
        _tourLayer = new MemoryLayer("Touren");
        _map.Layers.Add(_tourLayer);
        _fotoLayer = new MemoryLayer("Fotos");
        _map.Layers.Add(_fotoLayer);
        _markerLayer = new MemoryLayer("Marker");
        _map.Layers.Add(_markerLayer);
        // Style = null: KEIN Layer-Default-Symbol (Mapsui zeichnet sonst einen grau/weißen Kreis
        // hinter jedes Punkt-Feature). Die Position bekommt ihren Style allein über das Feature.
        _posLayer = new MemoryLayer("Position") { Style = null };   // Live-Standort-Beam ganz oben
        _map.Layers.Add(_posLayer);
        var (bx, by) = SphericalMercator.FromLonLat(13.405, 52.52);
        _map.Home = n => n.CenterOnAndZoomTo(new MPoint(bx, by), Aufloesung(9));
        UeMap.Map = _map;
        _map.Info += OnKarteTipp;

        _fotoAn = Einst.FotosBeimStart;   // Foto-Ebene beim Start nur, wenn in Einstellungen → Allgemein aktiviert (Standard: aus)
        FotoKnopfAnzeigen();

        ChipsBauen();
        RadiusBauen();
    }

    private static double Aufloesung(int zoom) => WELT0 / Math.Pow(2, zoom);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = SensorenStarten();   // Live-Standort-Beam + Kompass (jedes Mal, da OnDisappearing stoppt)
        KompassIconAktualisieren();
        if (_geladen) return;
        _geladen = true;
        Status("Touren werden geladen …");
        // Erst den ersten Frame (Karte + Bedienelemente) rendern lassen, dann im Hintergrund
        // laden. So blockieren Fetch/Parse/Filter nie den UI-Thread (kein Einfrieren/ANR).
        Dispatcher.Dispatch(() => _ = ErstLadenAsync());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SensorenStoppen();   // Akku/Leaks: Standort-Schleife + Kompass beim Verlassen stoppen
    }

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
        catch (Exception ex) { Debug.WriteLine(ex); Status("Touren konnten nicht geladen werden"); }
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
                _gefiltert = gefiltert;
                _tourLayer.Features = features;
                _tourLayer.DataHasChanged();
                UmkreisKreisZeichnen();
                FotoFilterAnwenden();
                if (listeSichtbar) ListePanel.ItemsSource = gefiltert.Take(MaxRouten).ToList();
                Status(statusNachher);
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
                    Fill = new Mapsui.Styles.Brush(Farbe(t.Farbe)),
                    Outline = new Pen(Mapsui.Styles.Color.White, 2),
                });
                features.Add(mf);
                continue;
            }
            // Übersichts-Linie stark ausdünnen: nur ~24 Punkte je Tour (Start/Ende behalten).
            // Die volle Geometrie zeichnet erst die Navi-Seite. So bleibt Verschieben/Zoomen flüssig
            // (statt 250 × 337 = ~54.000 nur noch ~6.000 Punkte gesamt).
            const int maxPunkte = 24;
            var route = t.Route;
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
                Line = new Pen(Farbe(t.Farbe), quali ? 5 : 3) { PenStyle = PenStyle.Solid }
            });
            features.Add(f);
        }
        return features;
    }

    private static Mapsui.Styles.Color Farbe(string hex)
    {
        try { return Mapsui.Styles.Color.FromString(hex); }
        catch { return Mapsui.Styles.Color.FromString("#0d9488"); }
    }

    // ---- Karten-Tipp → nächste Tour öffnen -----------------------------------
    // Karten-Tipp → Kontextmenü: Navigation zu / GPS-Position / Marker setzen.
    private async void OnKarteTipp(object? sender, MapInfoEventArgs e)
    {
        var wp = e.MapInfo?.WorldPosition;
        if (wp == null) return;
        var (lon, lat) = SphericalMercator.ToLonLat(wp.X, wp.Y);
        // Liegt der Tipp nah an einer angezeigten GPS-Route? Dann diese direkt abnavigierbar machen.
        var nah = NaechsteTourRoute(lat, lon);
        var optionen = new List<string> { "🧭 Navigation zu" };
        if (nah != null) optionen.Add("🧭 GPS-Route abwandern");
        optionen.Add("📍 GPS-Position");
        optionen.Add("📌 Marker setzen");
        string wahl = await DisplayActionSheet("Was möchtest du tun?", "Abbrechen", null, optionen.ToArray());
        if (wahl == "🧭 Navigation zu")
        {
            MainPage.GeplantesZiel = (lat, lon);
            await Shell.Current.GoToAsync("//navigation");
        }
        else if (wahl == "🧭 GPS-Route abwandern" && nah != null)
        {
            MainPage.GeplanteTour = nah;   // MainPage startet die Tour beim Erscheinen
            await Shell.Current.GoToAsync("//navigation");
        }
        else if (wahl == "📍 GPS-Position") OnStandort(this, EventArgs.Empty);
        else if (wahl == "📌 Marker setzen")
        {
            string name = await DisplayPromptAsync("Marker setzen", "Name des Markers:",
                                                   "Setzen", "Abbrechen", "z. B. Treffpunkt");
            if (name == null) return;   // abgebrochen
            MarkerSetzen(lat, lon, string.IsNullOrWhiteSpace(name) ? "Marker" : name.Trim());
        }
    }

    // Nächstgelegene angezeigte Tour-Route zum Tipp-Punkt (null, wenn keine in Reichweite).
    // Abstand zum LINIENSEGMENT (nicht nur zu Stützpunkten) und Toleranz abhängig vom Zoom
    // (~22 px Tap-Radius), damit man die Linie auch herausgezoomt zuverlässig trifft.
    private TourInfo? NaechsteTourRoute(double lat, double lon)
    {
        double res = _map.Navigator.Viewport.Resolution;            // Mercator-Meter/Pixel
        double mProPixel = res * Math.Cos(lat * Math.PI / 180);     // ≈ reale Meter/Pixel
        double tol = Math.Clamp(mProPixel * 22, 30, 250);
        TourInfo? best = null;
        double bestD = tol;
        foreach (var t in _gefiltert)
        {
            if (t.Route.Count < 2) continue;
            for (int i = 1; i < t.Route.Count; i++)
            {
                double d = NavGeo.DistanzZuSegment(lat, lon, t.Route[i - 1], t.Route[i]);
                if (d < bestD) { bestD = d; best = t; }
            }
        }
        return best;
    }

    private void MarkerSetzen(double lat, double lon, string name)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(x, y) };
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse, SymbolScale = 0.8,
            Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString("#e2231a")),
            Outline = new Pen(Mapsui.Styles.Color.White, 2),
        });
        f.Styles.Add(new LabelStyle
        {
            Text = name, Offset = new Offset(0, 18), Font = new Mapsui.Styles.Font { Size = 13, Bold = true },
            ForeColor = Mapsui.Styles.Color.FromString("#0f172a"),
            BackColor = new Mapsui.Styles.Brush(Mapsui.Styles.Color.White),
            Halo = new Pen(Mapsui.Styles.Color.White, 2),
        });
        _markerLayer.Features = new List<IFeature> { f };
        _markerLayer.DataHasChanged();
        StatusKurz($"📌 Marker {name} gesetzt", 4);   // blendet sich nach 4 s aus
    }

    // ---- Detail-Dialog --------------------------------------------------------
    private void DialogZeigen(TourInfo t)
    {
        _gewaehlt = t;
        DlgKat.Text = t.Kategorie;
        DlgName.Text = t.Name;
        string dauer = t.DauerMin >= 60 ? $"{t.DauerMin / 60}:{t.DauerMin % 60:00} h" : $"{t.DauerMin} Min.";
        DlgBadges.Text = $"📏 {t.Km:0.0} km   🕒 ≈ {dauer}" + (string.IsNullOrEmpty(t.Grad) ? "" : $"   💪 {t.Grad}");
        DlgBeschr.Text = t.Beschreibung;
        if (!string.IsNullOrEmpty(t.Bild)) { DlgBild.Source = t.Bild; DlgBild.IsVisible = true; }
        else DlgBild.IsVisible = false;
        DlgPois.Clear();
        DlgPoiTitel.IsVisible = false;
        Dialog.IsVisible = true;
        _ = PoisLaden(t.Id);
    }

    private async Task PoisLaden(int id)
    {
        try
        {
            var pois = await TourDetailService.PoisAsync(id);
            if (_gewaehlt?.Id != id || pois.Count == 0) return;   // Dialog evtl. schon gewechselt
            DlgPoiTitel.IsVisible = true;
            foreach (var p in pois.Take(12))
            {
                var zeile = string.IsNullOrEmpty(p.Kategorie) ? p.Name : $"{p.Name} · {p.Kategorie}";
                if (p.DistM > 0) zeile += $" · {p.DistM} m vom Weg";
                DlgPois.Add(new Label { Text = "• " + zeile, FontSize = 13, TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#334155") });
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
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

    private async void OnOrtSuche(object? sender, EventArgs e)
    {
        string q = (OrtEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q)) return;
        Status("Ort wird gesucht …");
        try
        {
            var treffer = await GeocodeService.SucheAsync(q);
            if (treffer.Count == 0) { Status("Ort nicht gefunden"); return; }
            var o = treffer[0];
            var (x, y) = SphericalMercator.FromLonLat(o.Lon, o.Lat);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), Aufloesung(12));
            _zentrum = (o.Lat, o.Lon);
            MittelpunktSetzen(o.Name);
            UmkreisCheck.IsChecked = true;   // aktiviert Umkreis (CheckedChanged → Anwenden)
            Anwenden($"📍 {o.Name} · {_radiusKm} km");   // Status erst NACH dem Zeichnen setzen (kein Überschreiben)
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status("Ortssuche fehlgeschlagen"); }
    }

    private async void OnDialogNavigieren(object? sender, EventArgs e)
    {
        if (_gewaehlt == null) return;
        MainPage.GeplanteTour = _gewaehlt;
        Dialog.IsVisible = false;
        await Shell.Current.GoToAsync("//navigation");
    }

    // ---- Suchzeile ------------------------------------------------------------
    private void OnSuche(object? sender, TextChangedEventArgs e)
    {
        _suche = (e.NewTextValue ?? "").Trim();
        var meins = _suche;   // Debounce: erst nach 280 ms ohne weitere Eingabe filtern/zeichnen
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(280), () => { if (_suche == meins) Anwenden(); });
    }

    private async void OnStandort(object? sender, EventArgs e)
    {
        try
        {
            var st = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (st != PermissionStatus.Granted) st = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            var loc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));
            if (loc == null) { Status("Kein Standort"); return; }
            var (x, y) = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), Aufloesung(12));
            _zentrum = (loc.Latitude, loc.Longitude);
            if (_umkreis) Anwenden();
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status("Standort nicht verfügbar"); }
    }

    // ---- Live-Standort (Beam) + Kompass --------------------------------------
    private async Task SensorenStarten()
    {
        try
        {
            var st = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (st != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        await GpsStart();
        KompassStart();
    }

    private void SensorenStoppen()
    {
        _gpsLaeuft = false;   // beendet die Live-Positions-Schleife
        try { Compass.Default.Stop(); } catch (Exception ex) { Debug.WriteLine(ex); }
        Compass.Default.ReadingChanged -= AufKompass;
        _kompassLaeuft = false;
    }

    private Task GpsStart()
    {
        if (_gpsLaeuft) return Task.CompletedTask;
        _gpsLaeuft = true;
        // Foreground-Listening hat auf Android einen 50-m-Distanzfilter (im Stand nie ein Update),
        // darum sofort die letzte bekannte Position zeigen und dann live nachführen.
        _ = ErstFixHolen();
        _ = PositionsSchleife();
        return Task.CompletedTask;
    }

    private async Task ErstFixHolen()
    {
        try
        {
            var letzte = await Geolocation.Default.GetLastKnownLocationAsync();
            if (letzte != null && _letztePos == null) VerarbeitePosition(letzte);
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    /// <summary>Fordert ununterbrochen den nächsten Fix an und verarbeitet ihn sofort – kein
    /// fester Takt; umgeht den 50-m-Distanzfilter. Taktung = GPS (Consumer ~1 Fix/Sek.).</summary>
    private async Task PositionsSchleife()
    {
        if (_positionsSchleifeLaeuft) return;
        _positionsSchleifeLaeuft = true;
        try
        {
            while (_gpsLaeuft)
            {
                long start = Environment.TickCount64;
                try
                {
                    var loc = await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)));
                    if (loc != null) VerarbeitePosition(loc);
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
                long dauer = Environment.TickCount64 - start;
                if (dauer < 200) await Task.Delay(200 - (int)dauer);   // Schutz gegen Leerlauf-Spin (max 5 Hz)
            }
        }
        finally { _positionsSchleifeLaeuft = false; }
    }

    private void VerarbeitePosition(Microsoft.Maui.Devices.Sensors.Location loc)
    {
        // Fahrtrichtung aus der BEWEGUNG berechnen (loc.Course ist auf vielen Geräten leer; N55 hat
        // keinen Kompass). Erst ab ~6 m Bewegung → stabile Richtung, kein Zittern im Stand.
        if (_letzteKursGeo is { } prev)
        {
            if (NavGeo.Haversine(prev.lat, prev.lon, loc.Latitude, loc.Longitude) >= 6)
            {
                _gpsKurs = NavGeo.Bearing(prev.lat, prev.lon, loc.Latitude, loc.Longitude);
                _letzteKursGeo = (loc.Latitude, loc.Longitude);
            }
        }
        else _letzteKursGeo = (loc.Latitude, loc.Longitude);
        _letzteGeo = (loc.Latitude, loc.Longitude);
        var (x, y) = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        _letztePos = new MPoint(x, y);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PositionZeichnen();
            double kurs = _kompassHatWert ? _heading : _gpsKurs;   // ohne Kompass-HW (N55): GPS-Fahrtrichtung
            if (_zentrierenNaechsterFix)
            {
                _zentrierenNaechsterFix = false;
                var ziel = _letztePos;
                void Zentriere()
                {
                    if (ziel == null) return;
                    _map.Navigator.CenterOnAndZoomTo(ziel, Aufloesung(ZentrierZoom));
                    if (_fahrtrichtung) _map.Navigator.RotateTo(-kurs);
                }
                Zentriere();
                // _map.Home wird beim ersten Viewport oft NACH dem ersten Fix angewandt und überschreibt
                // die Zentrierung – darum kurz danach erneut zentrieren, damit der Standort gewinnt.
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), Zentriere);
            }
            else if (_folgen) _map.Navigator.CenterOn(_letztePos);
            // Kompass-Modus OHNE Kompass-Hardware: Karte in GPS-Fahrtrichtung drehen (greift nur bei Bewegung).
            if (_fahrtrichtung && !_kompassHatWert) _map.Navigator.RotateTo(-_gpsKurs);
        });
    }

    private void KompassStart()
    {
        if (_kompassLaeuft) return;
        try
        {
            Compass.Default.ReadingChanged -= AufKompass;
            Compass.Default.ReadingChanged += AufKompass;
            if (!Compass.Default.IsMonitoring) Compass.Default.Start(SensorSpeed.UI);
            _kompassLaeuft = true;
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void AufKompass(object? sender, CompassChangedEventArgs e)
    {
        _heading = e.Reading.HeadingMagneticNorth;
        _kompassHatWert = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Beam nur bei spürbarer Kurs-Änderung neu zeichnen (Redraw drosseln).
            if (Math.Abs(((_heading - _gezeichnetHeading + 540) % 360) - 180) > 3)
            { _gezeichnetHeading = _heading; PositionZeichnen(); }
            if (_fahrtrichtung) _map.Navigator.RotateTo(-_heading);   // Kompass-Modus: Karte dreht mit
        });
    }

    // Standort-Beam (Google-Stil): dunkelgrüner Punkt + breiter Trichter (radialer Alpha-Verlauf
    // ab der Position, dunkelgrün → transparent, ohne Ring/Kontur). Einmalig gerendert, drehbar.
    private int BeamBitmapId()
    {
        if (_beamBitmapId >= 0) return _beamBitmapId;
        const int g = 240;
        const float c = g / 2f;
        const float radius = 104f;
        const float halb = 39f * (float)(Math.PI / 180);
        var dunkel = new SkiaSharp.SKColor(22, 101, 52);
        using var bitmap = new SkiaSharp.SKBitmap(g, g);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            using var shader = SkiaSharp.SKShader.CreateRadialGradient(
                new SkiaSharp.SKPoint(c, c), radius,
                new[] { dunkel.WithAlpha(210), dunkel.WithAlpha(0) },
                new[] { 0f, 1f }, SkiaSharp.SKShaderTileMode.Clamp);
            using var beam = new SkiaSharp.SKPaint { Shader = shader, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
            using var pfad = new SkiaSharp.SKPath();
            pfad.MoveTo(c, c);
            for (int i = 0; i <= 28; i++)
            {
                float ang = -halb + 2 * halb * i / 28f;
                pfad.LineTo(c + radius * (float)Math.Sin(ang), c - radius * (float)Math.Cos(ang));
            }
            pfad.Close();
            canvas.DrawPath(pfad, beam);
            using var fuell = new SkiaSharp.SKPaint { Color = dunkel, IsAntialias = true };
            canvas.DrawCircle(c, c, 15f, fuell);
        }
        using var img = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        _beamBitmapId = Mapsui.Styles.BitmapRegistry.Instance.Register(new System.IO.MemoryStream(png.ToArray()), "uebersicht_beam");
        return _beamBitmapId;
    }

    private void PositionZeichnen()
    {
        if (_letztePos == null || _letzteGeo == null)
        {
            _posLayer.Features = new List<IFeature>();
            _posLayer.DataHasChanged();
            return;
        }
        double kursGrad = _kompassHatWert ? _heading : _gpsKurs;
        var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(_letztePos.X, _letztePos.Y) };
        f.Styles.Add(new SymbolStyle
        {
            BitmapId = BeamBitmapId(),
            SymbolScale = 0.5,
            SymbolRotation = kursGrad,
            RotateWithMap = true,
            Fill = null, Outline = null,   // kein Standard-Ellipsen-Symbol (heller Ring) hinter der Bitmap
        });
        _posLayer.Features = new List<IFeature> { f };
        _posLayer.DataHasChanged();
    }

    // ---- Chips ----------------------------------------------------------------
    private void ChipsBauen()
    {
        foreach (var (key, label) in FacettenChips)
        {
            var b = new Button
            {
                Text = label, FontSize = 13, Padding = new Thickness(14, 6), CornerRadius = 16,
                CommandParameter = key,
                AutomationId = "osm_chip_" + (string.IsNullOrEmpty(key) ? "alle" : key.Trim('_')),
            };
            b.Clicked += OnChip;
            _chips[key] = b;
            ChipLeiste.Add(b);
        }
        ChipsMarkieren();
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
            MittelpunktSetzen("Kartenmitte");
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
        if (MittelpunktLabel != null) MittelpunktLabel.Text = $"Mittelpunkt: {name}";
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

    // ---- Layer / Zentrieren / Foto -------------------------------------------
    // Zentrieren/Kompass-Toggle (wie Navigation): zentriert auf die Live-Position und schaltet
    // zwischen Kompass-Modus (Karte dreht mit, rotes Fadenkreuz) und Norden-Modus (fix Norden,
    // „N"-Knopf, KEINE Drehung) um.
    private void OnZentrieren(object? sender, EventArgs e)
    {
        bool warFolgen = _folgen;
        _folgen = true;
        // Erstes Antippen (aus „nicht folgen") → Kompass-Modus; weitere Tipps schalten Norden ↔ Kompass.
        _fahrtrichtung = warFolgen ? !_fahrtrichtung : true;
        try { _map.Navigator.RotationLock = false; } catch (Exception ex) { Debug.WriteLine(ex); }
        if (_letztePos != null)
        {
            _map.Navigator.CenterOnAndZoomTo(_letztePos, Aufloesung(ZentrierZoom));
            _map.Navigator.RotateTo(_fahrtrichtung ? -_heading : 0);
        }
        else
        {
            _zentrierenNaechsterFix = true;   // sobald der erste Fix kommt, dorthin zentrieren
            Status("Warte auf GPS-Standort …");
        }
        // Norden-Modus: Karten-Drehung sperren (dreht sich nicht). Kompass-Modus: entsperrt (App dreht).
        try { _map.Navigator.RotationLock = !_fahrtrichtung; } catch (Exception ex) { Debug.WriteLine(ex); }
        KompassIconAktualisieren();
        // Ohne Kompass-Hardware (z. B. Doogee N55) kann die Karte im Stand nicht zur Blickrichtung drehen.
        if (_fahrtrichtung)
        {
            bool kompass = false;
            try { kompass = Compass.Default.IsSupported; } catch (Exception ex) { Debug.WriteLine(ex); }
            if (!kompass) StatusKurz("Kein Kompass im Gerät – die Karte dreht in Laufrichtung, sobald du gehst.", 6);
        }
    }

    private void KompassIconAktualisieren()
    {
        bool nord = _folgen && !_fahrtrichtung;
        OsmLocateIcon.IsVisible = !nord;
        OsmNordIcon.IsVisible = nord;
        OrtBorder.BackgroundColor = nord
            ? Microsoft.Maui.Graphics.Colors.White
            : Microsoft.Maui.Graphics.Color.FromArgb("#e2231a");
    }


    private void OnLayer(object? sender, EventArgs e)
    {
        _modus = (Kartenmodus)(((int)_modus + 1) % 4);   // Wandern → Standard → Satellit → Dunkel
        var neu = new TileLayer(MapQuellen.Quelle(_modus)) { Name = "Basis" };
        var alt = _basisLayer;
        _map.Layers.Remove(alt);
        _map.Layers.Insert(0, neu);
        _basisLayer = neu;
        (alt as IDisposable)?.Dispose();   // alten TileLayer freigeben (kein Ressourcen-Leak)
        _map.RefreshGraphics();
    }

    // Vollbild: Kopfleiste (Shell-NavBar) + Aufklapp-Fenster ausblenden → maximale Karte.
    private bool _vollbild;
    private void OnVollbild(object? sender, EventArgs e)
    {
        _vollbild = !_vollbild;
        Shell.SetNavBarIsVisible(this, !_vollbild);
        StartSheet.IsVisible = !_vollbild;
        // Aktiv: roter Knopf + weißes Icon.
        VollBorder.BackgroundColor = _vollbild
            ? Microsoft.Maui.Graphics.Color.FromArgb("#e2231a")
            : Microsoft.Maui.Graphics.Colors.White;
        VollIcon.Stroke = new SolidColorBrush(_vollbild
            ? Microsoft.Maui.Graphics.Colors.White
            : Microsoft.Maui.Graphics.Color.FromArgb("#0f172a"));
    }

    private void OnListeGewaehlt(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not TourInfo t) return;
        ListePanel.SelectedItem = null;
        DialogZeigen(t);
    }

    private async void OnFoto(object? sender, EventArgs e)
    {
        _fotoAn = !_fotoAn;
        FotoKnopfAnzeigen();
        if (_fotoAn && _fotos.Count == 0)
        {
            Status("Fotos werden geladen …");
            try { _fotos = await FotoService.LadeAsync(); Status(null); }
            catch (Exception ex) { Debug.WriteLine(ex); Status("Fotos nicht verfügbar"); }
        }
        FotoFilterAnwenden();
    }

    // Foto-Knopf-Optik an _fotoAn ausrichten: aktiv = blauer Knopf + weißes Kamera-Icon; sonst weiß + dunkel.
    private void FotoKnopfAnzeigen()
    {
        FotoBorder.BackgroundColor = _fotoAn ? Microsoft.Maui.Graphics.Color.FromArgb("#1d4ed8") : Microsoft.Maui.Graphics.Colors.White;
        var fi = new SolidColorBrush(_fotoAn ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Color.FromArgb("#0f172a"));
        FotoIcon.Stroke = fi;
        FotoLinse.Stroke = fi;
    }

    private void FotoFilterAnwenden()
    {
        if (!_fotoAn) { _fotoLayer.Features = new List<IFeature>(); _fotoLayer.DataHasChanged(); return; }
        var sichtbar = new HashSet<int>(_gefiltert.Select(t => t.Id));
        var features = new List<IFeature>();
        foreach (var f in _fotos.Where(p => sichtbar.Contains(p.TourId)))
        {
            var (x, y) = SphericalMercator.FromLonLat(f.Lon, f.Lat);
            var pt = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(x, y) };
            pt.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString("#f59e0b")),
                Outline = new Pen(Mapsui.Styles.Color.White, 1.5),
                SymbolScale = 0.6,
            });
            features.Add(pt);
        }
        _fotoLayer.Features = features;
        _fotoLayer.DataHasChanged();
    }

    // ---- Status ---------------------------------------------------------------
    private void Status(string? text)
    {
        UeStatus.Text = text ?? "";
        UeStatusPille.IsVisible = !string.IsNullOrEmpty(text);
    }

    // Status, der sich nach `sekunden` selbst ausblendet (z. B. „Marker gesetzt").
    private IDispatcherTimer? _statusTimer;
    private void StatusKurz(string text, int sekunden = 4)
    {
        Status(text);
        _statusTimer?.Stop();
        _statusTimer = Dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(sekunden);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, __) => Status(null);
        _statusTimer.Start();
    }
}
