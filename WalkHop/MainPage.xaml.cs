using System.Diagnostics;
using BruTile.Web;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using NetTopologySuite.Geometries;

namespace WalkHop;

public partial class MainPage : ContentPage
{
    // OSM-Web-Mercator-Auflösung (Meter/Pixel) bei Zoom 0; /2^z ergibt Stufe z.
    private const double MercatorAufloesungZoom0 = 156543.03392804097;
    private const int HomeZoom = 13;            // Start-Zoom
    private const int ZentrierZoom = 16;        // Zoom beim Zentrieren auf den Standort
    private const int MaxOsmZoom = 19;          // höchste sinnvolle OSM-Kachelstufe
    private const double ZielRadiusMeter = 25;  // ab hier gilt das Ziel als erreicht
    private const int OfflineExtraZoom = 2;     // beim Offline-Laden so viele Stufen tiefer
    private const int OfflineMaxKacheln = 300;  // Obergrenze pro Offline-Download
    private const int AppKontoOfflineP5 = 3;    // Abo 5 €: 3 Offline-Karten inklusive (vgl. AppKonto.OFFLINE_INKLUSIVE_P5)
    // Richtungspfeil (maps.me-/Web-Konzept): lila Schaft ab der Position nach vorne auf
    // der Route. Grund-Länge; steht eine Abbiegung in Reichweite, wächst er bis dahin.
    private const double VorausMin = 90;        // Grund-Länge des Richtungspfeils (m)
    private const double VorausAbbieg = 150;    // Abbiegung <= so nah → Pfeil bis dahin zeigen
    private const double VorausUeber = 20;      // …und ein Stück darüber hinaus

    private static readonly Microsoft.Maui.Graphics.Color Dunkel = Microsoft.Maui.Graphics.Color.FromArgb("#0f172a");
    private static readonly Microsoft.Maui.Graphics.Color Weiss = Microsoft.Maui.Graphics.Colors.White;
    private static readonly Microsoft.Maui.Graphics.Color Rot = Microsoft.Maui.Graphics.Color.FromArgb("#e2231a");
    private static readonly Microsoft.Maui.Graphics.Color Teal = Microsoft.Maui.Graphics.Color.FromArgb("#0d9488");

    private Mapsui.Map _map = null!;
    private MemoryLayer _positionLayer = null!;   // dezenter grüner Positions-Chevron (Blickrichtung)
    private MemoryLayer _routeLayer = null!;
    private MemoryLayer _tourLayer = null!;        // alle GPS-Tour-Routen (zum Antippen → „abwandern")
    private List<TourInfo> _touren = new();
    private bool _tourenGezeichnet;
    private MemoryLayer _richtungLayer = null!;   // lila Richtungspfeil (Schaft + Spitze) voraus auf der Route
    private MemoryLayer _breadcrumbLayer = null!; // dezente „Brotkrumen"-Spur des zurückgelegten Weges (unter der Route)
    private readonly List<(double lat, double lon)> _breadcrumb = new();   // gesammelte GPS-Spur
    private readonly object _breadcrumbLock = new();
    // Gruppen-Position: eigene Live-Position teilen + Mitglieder als Marker zeigen (Code-basiert).
    private MemoryLayer _gruppeLayer = null!;
    private string _gruppeCode = "";              // aktiver Gruppen-Code (leer = nicht in einer Gruppe)
    private long _letztGruppeSendeMs = -100000;   // letzte gesendete eigene Position (Drosselung)
    private IDispatcherTimer? _gruppeTimer;       // pollt die Mitglieder-Positionen
    private double _gezeichnetHeading = -999;     // zuletzt gezeichnete Blickrichtung (Redraw-Drosselung)
    private double _gedrehtHeading = -999;        // zuletzt gedrehte Kartenrichtung (RotateTo-Drosselung)
    private double _letzteRes = -1;               // zuletzt gezeichnete Auflösung (Chevron-Größen-Redraw)
    private TileLayer _basisLayer = null!;
    private TileLayer _wanderLayer = null!;
    private HttpTileSource _aktiveQuelle = null!;
    private Kartenmodus _modusJetzt;     // aktuell angewandter Kartenmodus (Abgleich mit Einstellungen-Seite)
    private string _profilJetzt = "";    // aktuell angewandtes Fortbewegungsprofil
    private string _ankunftText = "";
    private readonly HoehenProfil _hoehe = new();
    private MPoint? _letztePos;
    private (double lat, double lon)? _letzteGeo;
    private (double lat, double lon)? _startUeberschreibung;   // „Von hier starten"
    private double _heading;
    private bool _kompassHatWert;     // erst nach der ersten Kompass-Lesung true (sonst _heading == 0)
    private double _gpsKurs;          // Fahrtrichtung aus GPS (Fallback ohne Kompass)
    private (double lat, double lon)? _letzteKursGeo;   // letzte Position für Kursberechnung aus Bewegung
    private double _letztEntlang;     // zuletzt projizierte Distanz entlang der Route (für Zoom-Redraw)
    private bool _folgen, _fahrtrichtung, _vollbild;
    private volatile bool _userBeruehrt;   // Finger berührt die Karte → Kamera nicht programmatisch bewegen
    private long _letzteBeruehrungMs;
    private bool KameraFrei =>
        !_userBeruehrt && Environment.TickCount64 - _letzteBeruehrungMs > 350
        || Environment.TickCount64 - _letzteBeruehrungMs > 4000;   // Auto-Reset bei verpasstem TouchEnded
    private bool _zentrierenNaechsterFix = true;   // erster GPS-Fix (oder Zentrieren ohne Fix) → auf Position zoomen+ausrichten
    private long _letztViewportRedrawMs;           // Debounce für den Kegel-Redraw bei Zoom (gegen Ruckeln)
    private long _letztLangdruckMs;                // Zeitpunkt des letzten Karten-Langdrucks (entkoppelt Langdruck vom Tap)
    // Zoom-Glättung: Route + Richtungspfeil (volle Vektor-Geometrie) während der Zoom-Geste ausblenden.
    private double _letzteZoomRes = -1;
    private bool _vektorenVerborgen;
    private IDispatcherTimer? _zoomTimer;
    private bool _gpsLaeuft, _kompassLaeuft, _berechtigungGeprueft;
    private bool _positionsSchleifeLaeuft;   // genau eine Live-Positions-Schleife (umgeht den 50-m-Distanzfilter)

    // Live-Navigation
    private List<(double lat, double lon)>? _navPunkte;
    private double[]? _navKum;
    private List<Manoever> _navManoever = new();
    private string _letztNotifText = "";   // letzter an die Uhr gespiegelte Abbiege-Hinweis
    private double _navGesamt;
    private int _letztGesprochen = -1, _vorabGesprochen = -1, _navIdx;
    private int _tonManoever = -1;   // Manöver, für das zuletzt ein Benachrichtigungston gespielt wurde (1×/Manöver)
    private bool _zielAngesagt;
    // Auto-Reroute + Tour-Zustand
    private bool _istTour, _reroutLaeuft;
    private List<(double lat, double lon)>? _tourOriginal;
    private (double lat, double lon)? _navZiel;
    private double _navMinuten;
    // Reroute-Drosselung über eine MONOTONE Uhr (Environment.TickCount64, ms seit Systemstart),
    // nicht DateTime.Now – sonst könnte ein Uhren-/NTP-/DST-Sprung die Sperre verklemmen.
    private long _letztRerouteMs = -10000;
    // Track-Aufnahme (lokal speichern, beim App-Ende hochladen)
    private bool _aufnahme;
    private bool _autoAufnahmeProbiert;   // Auto-Aufnahme nur einmal pro App-Sitzung starten
    private DateTime _aufnahmeStart;
    private readonly List<(double lat, double lon)> _track = new();
    private readonly object _trackLock = new();
    // Alternativrouten
    private List<RouteErgebnis> _alternativen = new();
    // Routenplan (mehrere Wegpunkte)
    private readonly List<(double lat, double lon)> _plan = new();

    /// <summary>Von der Startseite gewählte Tour, die beim Erscheinen gestartet wird.</summary>
    public static TourInfo? GeplanteTour { get; set; }

    /// <summary>Von der Karte (Kontextmenü „Navigation zu") gesetztes Ziel.</summary>
    public static (double lat, double lon)? GeplantesZiel { get; set; }

    private static MainPage? _aktuell;

    public MainPage()
    {
        InitializeComponent();
        _aktuell = this;
        _folgen = Einst.Folgen;
        _fahrtrichtung = true;   // Karte beim Start in Blickrichtung ausrichten (course-up); Kompass-Knopf schaltet auf Norden um

        _map = new Mapsui.Map();
        // Effektiver Basiskarten-Modus = Nutzer-Kartenwahl, ggf. vom Farbmodus (Nacht/Auto) auf Dunkel
        // umgebogen. So startet die Navigations-Karte gleich im richtigen Tag-/Nacht-Stil.
        var startModus = EffektiverKartenmodus();
        _aktiveQuelle = MapQuellen.Quelle(startModus);
        _basisLayer = new TileLayer(_aktiveQuelle) { Name = "Basis" };
        _map.Layers.Add(_basisLayer);
        _wanderLayer = new TileLayer(MapQuellen.Wanderwege(Einst.Profil == "bicycle"))
        { Name = "Wanderwege", Enabled = Einst.Wanderwege };
        _map.Layers.Add(_wanderLayer);
        _modusJetzt = startModus; _profilJetzt = Einst.Profil;   // Ausgangszustand für den Einstellungs-Abgleich
        // Tour-Routen-Overlay (unter der aktiven Route): dezent, antippbar zum „Abwandern".
        _tourLayer = new MemoryLayer("Touren");
        _map.Layers.Add(_tourLayer);
        // Breadcrumb-Spur (zurückgelegter Weg): dezent, liegt ÜBER dem Tour-Overlay, aber
        // UNTER der aktiven Route und dem Positionsmarker. Style = null: kein Layer-Default-Symbol.
        _breadcrumbLayer = new MemoryLayer("Breadcrumb") { Style = null };
        _map.Layers.Add(_breadcrumbLayer);
        _routeLayer = new MemoryLayer("Route");
        _map.Layers.Add(_routeLayer);
        // Lila Richtungspfeil: liegt ÜBER der (blauen) Route, aber UNTER dem Positionsmarker.
        _richtungLayer = new MemoryLayer("Richtung");
        _map.Layers.Add(_richtungLayer);
        // Gruppen-Mitglieder (Live-Marker): über der Route/dem Richtungspfeil, aber UNTER dem
        // eigenen Positions-Beam. Style = null: jedes Feature bringt seinen eigenen Style mit.
        _gruppeLayer = new MemoryLayer("Gruppe") { Style = null };
        _map.Layers.Add(_gruppeLayer);
        // Positionsmarker als dezenter grüner Chevron (maps.me-Stil), oben auf allen Ebenen:
        // zeigt über den Kompass die Blickrichtung („wohin ich schaue").
        // Style = null: kein Layer-Default-Symbol (sonst grau/weißer Kreis hinter dem Punkt-Feature).
        _positionLayer = new MemoryLayer("Position") { Style = null };
        _map.Layers.Add(_positionLayer);

        var (bx, by) = SphericalMercator.FromLonLat(13.405, 52.52);
        _map.Home = n => n.CenterOnAndZoomTo(new MPoint(bx, by), Aufloesung(HomeZoom));
        MapCtrl.Map = _map;
        // maps.me-Stil: beim 2-Finger-Zoom NICHT mitdrehen (sonst unruhig). Die Karte dreht
        // erst, wenn man bewusst über 30° verdreht – kleine Nebendrehungen beim Zoom werden
        // ignoriert. Bewusstes Drehen funktioniert weiter.
        MapCtrl.UnSnapRotationDegrees = 30;
        MapCtrl.ReSnapRotationDegrees = 8;
        // Mapsuis 200-ms-„Warte-auf-Doppeltipp" abschalten: Tipps (Kontextmenü/Detail) reagieren
        // sofort statt verzögert; Zoom per Pinch (GPU-flüssig). Doppeltipp-Zoom entfällt (es gibt +/- Knöpfe).
        MapCtrl.UseDoubleTap = false;
        // Während der Finger die Karte berührt: keine programmatische Kamera-Bewegung (Folgen/Drehen),
        // sonst kämpft die Live-Schleife gegen die Pinch-Geste → Zittern (maps.me pausiert das ebenso).
        MapCtrl.TouchStarted += (s, e) => { _userBeruehrt = true; _letzteBeruehrungMs = Environment.TickCount64; };
        MapCtrl.TouchEnded += (s, e) => { _userBeruehrt = false; _letzteBeruehrungMs = Environment.TickCount64; };
        _map.Info += AufKarteTipp;
        // Bei Zoom-Änderung den (pixelgroßen) Positions-Chevron neu zeichnen, damit er
        // gleich groß bleibt – sonst behält er die Größe vom letzten GPS-Takt.
        _map.Navigator.ViewportChanged += (s, e) => ViewportGeaendert();
        RotationssperreAktualisieren();   // manuelle Drehung je nach Einstellung sperren

        KompassIconAktualisieren();
        TonIconAktualisieren();
        _gruppeCode = Einst.GruppenCode;   // ggf. zuletzt beigetretene Gruppe fortsetzen
        GruppeIconAktualisieren();
        HoeheView.Drawable = _hoehe;
        // Code-gesetzte Platzhalter mehrsprachig vorbelegen (kein Translate-Binding, da sie zur
        // Laufzeit mit Routeninfo überschrieben werden).
        ZielLabel.Text = L.T("ziel");
        HoeheInfo.Text = L.T("mp_hoehenprofil");
        // Bei Laufzeit-Sprachwechsel die Platzhalter neu setzen, solange keine Route läuft
        // (während aktiver Navigation tragen sie Routeninfos, die nicht überschrieben werden dürfen).
        L.Geaendert += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_navPunkte == null) { ZielLabel.Text = L.T("ziel"); HoeheInfo.Text = L.T("mp_hoehenprofil"); }
        });
        LangdruckEinrichten();
#if ANDROID
        WheelZoomEinrichten();
#endif
    }

    // Langdruck → Kontextmenü über Mapsuis eingebautes LongTap-Event. Mapsui erkennt die Geste selbst
    // (kein eigener Touch-Hook nötig), Schwenken/Zoom bleibt unberührt. e.ScreenPosition ist bereits in
    // Mapsui-Screen-Koordinaten → direkt per ScreenToWorld nach Geo.
    private void LangdruckEinrichten()
    {
        MapCtrl.LongTap += (s, e) =>
        {
            _letztLangdruckMs = Environment.TickCount64;   // den evtl. folgenden Tap (Map.Info) unterdrücken
            var welt = _map.Navigator.Viewport.ScreenToWorld(e.ScreenPosition.X, e.ScreenPosition.Y);
            var (lon, lat) = ZuGeo(welt.X, welt.Y);
            MainThread.BeginInvokeOnMainThread(() => _ = KontextmenueZeigen(lat, lon));
        };
    }

    private static double Aufloesung(int zoom) => MercatorAufloesungZoom0 / Math.Pow(2, zoom);

    private (double x, double y) ZuMercator(double lat, double lon) => SphericalMercator.FromLonLat(lon, lat);
    private (double lon, double lat) ZuGeo(double x, double y) => SphericalMercator.ToLonLat(x, y);

    // Zoom per On-Screen-Knopf (zuverlässig auf Emulator UND Gerät; das Mausrad ist am
    // Emulator unzuverlässig). Sofort, ohne Animation, zur Bildschirmmitte.
    private void OnZoomRein(object? sender, TappedEventArgs e) => ZoomSchritt(0.5);
    private void OnZoomRaus(object? sender, TappedEventArgs e) => ZoomSchritt(2.0);
    private void ZoomSchritt(double faktor)
    {
        var vp = _map.Navigator.Viewport;
        var mitte = new MPoint(vp.Width / 2.0, vp.Height / 2.0);
        _map.Navigator.ZoomTo(vp.Resolution * faktor, mitte, 0L, null);
    }

    // Transiente Status-Pille (unten, nur bei Aktionen) – KEINE dauerhafte GPS-Anzeige.
    private void Status(string? text, bool autoAus = false)
    {
        StatusLabel.Text = text ?? "";
        StatusPille.IsVisible = !string.IsNullOrEmpty(text);
        if (autoAus && StatusPille.IsVisible)
        {
            var meins = text;
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), () => { if (StatusLabel.Text == meins) Status(null); });
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Einstellungen, die sich zwischenzeitlich (Einstellungs-Seite) geändert haben können,
        // beim Zurückkehren auf die Karte anwenden.
        try { DeviceDisplay.Current.KeepScreenOn = Einst.BildschirmWach; } catch (Exception ex) { Debug.WriteLine(ex); }
        RotationssperreAktualisieren();
        KartenEinstellungenAnwenden();   // Kartenmodus/Profil/Overlay aus der Einstellungen-Seite übernehmen
        if (!_berechtigungGeprueft)
        {
            _berechtigungGeprueft = true;
            try
            {
                var s = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (s != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        await GpsStart();
        KompassStart();
        if (_gruppeCode.Length > 0) GruppeStart();   // Gruppen-Polling fortsetzen, falls beigetreten

        // Track-Aufnahme automatisch starten (Default), wenn in den Einstellungen aktiviert.
        if (Einst.AutoAufnahme && !_aufnahme && !_autoAufnahmeProbiert)
        {
            _autoAufnahmeProbiert = true;
            AufnahmeStart();
            Status(L.T("st_aufnahme_laeuft"), autoAus: true);
        }

        if (GeplanteTour is { } tour)   // von der Startseite übergebene Tour starten
        {
            GeplanteTour = null;
            await TourStarten(tour);
        }
        else if (GeplantesZiel is { } ziel)   // von der Karte (Kontextmenü „Navigation zu")
        {
            GeplantesZiel = null;
            await RouteZu(ziel.lat, ziel.lon);
        }
    }

    // ---- Tour-Routen-Overlay (antippbar zum „Abwandern") ----
    private async void TourenLaden()
    {
        if (_tourenGezeichnet) return;
        _tourenGezeichnet = true;
        try { _touren = await TourService.LadeTourenAsync(); }
        catch (Exception ex) { Debug.WriteLine(ex); _tourenGezeichnet = false; return; }
        var features = await Task.Run(() => BaueTourFeatures(_touren));
        _tourLayer.Features = features;
        _tourLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    // Übersichts-Linie stark ausdünnen (~12 Punkte) – die volle Geometrie zieht erst das Abwandern.
    private static List<IFeature> BaueTourFeatures(List<TourInfo> touren)
    {
        var features = new List<IFeature>();
        foreach (var t in touren)
        {
            if (t.Route.Count < 2) continue;
            const int maxPunkte = 12;
            var route = t.Route;
            int schritt = route.Count <= maxPunkte ? 1 : (int)Math.Ceiling(route.Count / (double)maxPunkte);
            var liste = new List<Coordinate>(maxPunkte + 2);
            for (int i = 0; i < route.Count; i += schritt)
            {
                var (x, y) = SphericalMercator.FromLonLat(route[i].lon, route[i].lat);
                liste.Add(new Coordinate(x, y));
            }
            var (xe, ye) = SphericalMercator.FromLonLat(route[^1].lon, route[^1].lat);
            liste.Add(new Coordinate(xe, ye));
            var f = new GeometryFeature { Geometry = new LineString(liste.ToArray()) };
            f.Styles.Add(new VectorStyle { Line = new Pen(FarbeTour(t.Farbe), 3) { PenStyle = PenStyle.Solid } });
            features.Add(f);
        }
        return features;
    }

    private static Mapsui.Styles.Color FarbeTour(string hex)
    {
        try { return Mapsui.Styles.Color.FromString(hex); }
        catch { return Mapsui.Styles.Color.FromString("#0d9488"); }
    }

    // Nächstgelegene angezeigte Tour-Route zum Punkt (null, wenn keine in ~18 px Reichweite).
    private TourInfo? NaechsteTourRoute(double lat, double lon)
    {
        if (_touren.Count == 0) return null;
        double res = _map.Navigator.Viewport.Resolution;
        if (res <= 0) return null;
        double mProPixel = res * Math.Cos(lat * Math.PI / 180);
        double tol = mProPixel * 10;   // nur ~10 px Treffer („direkt auf der Route", nicht nur in der Nähe)
        TourInfo? best = null;
        double bestD = tol;
        foreach (var t in _touren)
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

    private async Task TourStarten(TourInfo tour)
    {
        _folgen = false;
        if (tour.Route.Count < 2)   // Start-only-Tour: einfach dorthin navigieren
        {
            if (tour.Start is { } st) await RouteZu(st.lat, st.lon);
            return;
        }
        Status(L.T("st_tour_vorbereiten"));
        // Echte Abbiege-Manöver entlang der GPX-Route per Map-Matching (trace.json).
        string costing = tour.Facetten.Contains("radtour") ? "bicycle" : "pedestrian";
        RouteErgebnis? erg = null;
        try { erg = await RouteService.TraceAsync(tour.Route, costing, Einst.NaviLocale); }
        catch (Exception ex) { Debug.WriteLine(ex); }
        var punkte = erg != null && erg.Punkte.Count >= 2 ? erg.Punkte : tour.Route;
        var man = erg?.Manoever ?? new List<Manoever>();
        _istTour = true; _tourOriginal = punkte; _navZiel = punkte[^1];
        double dauer = erg != null && erg.Minuten > 0 ? erg.Minuten : tour.DauerMin;
        _navMinuten = dauer;
        var ank = DateTime.Now.AddMinutes(dauer).ToString("HH:mm");
        _alternativen.Clear();
        NavStart(punkte, man, $"{tour.Name} · {FmtKmVon(tour.Km)}", ank);
        Status(null);
        NaviAktivieren();   // aus dem Tour-Dialog gestartet → sofort aktiv (keine Vorschau mit „Start")
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SensorenStoppen();   // Lifecycle-Gegenstück zu OnAppearing: Akku/Leaks vermeiden
        GruppeStop();        // Gruppen-Polling pausieren (Code bleibt erhalten)
    }

    /// <summary>Beendet eine laufende Navigation + stoppt Sensoren (für Logout).</summary>
    public static void AktiveSitzungBeenden()
    {
        if (_aktuell == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _aktuell.NavigationBeenden();
            _aktuell.SensorenStoppen();
        });
    }

    // ---- GPS ---------------------------------------------------------------
    private static void GpsLog(string msg) => Debug.WriteLine("[GPS] " + msg);

    private async Task GpsStart()
    {
        if (_gpsLaeuft) return;
        GpsLog("GpsStart");
        try
        {
            Geolocation.Default.LocationChanged -= AufPosition;
            Geolocation.Default.LocationChanged += AufPosition;
            await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)));
            _gpsLaeuft = true;
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("st_gps_nicht_verfuegbar"), autoAus: true); }

        // WICHTIG: Foreground-Listening liefert auf Android wegen eines 50-m-Distanzfilters erst
        // NACH spürbarer Bewegung ein Update – im Stand (Testen!) käme nie eine Position, dann
        // bleibt _letztePos null → kein Beam, Kompass tut nichts, Navigation kann nicht starten.
        // Darum sofort die letzte bekannte Position zeigen und dann live nachführen.
        _ = ErstFixHolen();
        _ = PositionsSchleife();
    }

    /// <summary>Zeigt SOFORT die zuletzt bekannte Position an (instant, ohne auf einen frischen
    /// Fix zu warten) – damit Beam/Karte beim Start nicht leer bleiben.</summary>
    private async Task ErstFixHolen()
    {
        try
        {
            var letzte = await Geolocation.Default.GetLastKnownLocationAsync();
            GpsLog($"LastKnown = {(letzte == null ? "NULL" : $"{letzte.Latitude:F5},{letzte.Longitude:F5}")}");
            if (letzte != null && _letztePos == null) VerarbeitePosition(letzte);
        }
        catch (Exception ex) { GpsLog("LastKnown-Fehler: " + ex.Message); }
    }

    /// <summary>Live-Positions-Schleife: fordert ununterbrochen den nächsten Fix an und verarbeitet
    /// ihn SOFORT, sobald er da ist – kein fester Sekundentakt. Umgeht den 50-m-Distanzfilter des
    /// Foreground-Listenings (der im Stand nie ein Update liefert). Die Taktung ergibt sich allein
    /// aus dem GPS (Consumer-GPS liefert physikalisch ~1 Fix/Sekunde).</summary>
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
                    GpsLog($"Live-Fix = {(loc == null ? "NULL (Timeout)" : $"{loc.Latitude:F5},{loc.Longitude:F5}")}");
                    if (loc != null) VerarbeitePosition(loc);
                }
                catch (Exception ex) { GpsLog("Live-Fehler: " + ex.Message); }
                // Schutz gegen Leerlauf-Spin, falls GetLocationAsync sofort (ohne echten Fix) zurückkäme:
                long dauer = Environment.TickCount64 - start;
                if (dauer < 200) await Task.Delay(200 - (int)dauer);
            }
        }
        finally { _positionsSchleifeLaeuft = false; }
    }

    private void SensorenStoppen()
    {
        _gpsLaeuft = false;   // beendet die Live-Positions-Schleife
        try { Geolocation.Default.StopListeningForeground(); } catch (Exception ex) { Debug.WriteLine(ex); }
        Geolocation.Default.LocationChanged -= AufPosition;
        try { Compass.Default.Stop(); } catch (Exception ex) { Debug.WriteLine(ex); }
        Compass.Default.ReadingChanged -= AufKompass;
        _kompassLaeuft = false;
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void AufPosition(object? sender, GeolocationLocationChangedEventArgs e) => VerarbeitePosition(e.Location);

    /// <summary>Verarbeitet einen Standort – egal ob aus Foreground-Listening, Erst-Fix oder Poll.</summary>
    private void VerarbeitePosition(Microsoft.Maui.Devices.Sensors.Location loc)
    {
        // Felder dürfen vom Worker-Thread gesetzt werden; UI/Karte nur auf dem UI-Thread.
        // Fahrtrichtung aus der BEWEGUNG berechnen (loc.Course ist auf vielen Geräten leer; N55 hat
        // keinen Kompass). Erst ab ~6 m → stabile Richtung, kein Zittern im Stand.
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
        // Gruppen-Sharing: eigene Position (gedrosselt ~6 s) in die Gruppe schreiben (fire-and-forget).
        if (_gruppeCode.Length > 0 && Environment.TickCount64 - _letztGruppeSendeMs > 6000)
        {
            _letztGruppeSendeMs = Environment.TickCount64;
            _ = GruppeService.SendePositionAsync(_gruppeCode, GruppenAnzeigename(), loc.Latitude, loc.Longitude);
        }
        if (_aufnahme) lock (_trackLock) _track.Add((loc.Latitude, loc.Longitude));
        var (x, y) = ZuMercator(loc.Latitude, loc.Longitude);
        _letztePos = new MPoint(x, y);
        // Breadcrumb-Spur sammeln: neuen Punkt nur aufnehmen, wenn er spürbar (>8 m) vom letzten
        // Krumen-Punkt entfernt ist – das filtert GPS-Rauschen im Stand und hält die Linie schlank.
        bool breadcrumbNeu = false;
        lock (_breadcrumbLock)
        {
            if (_breadcrumb.Count == 0 ||
                NavGeo.Haversine(_breadcrumb[^1].lat, _breadcrumb[^1].lon, loc.Latitude, loc.Longitude) > 8)
            {
                _breadcrumb.Add((loc.Latitude, loc.Longitude));
                breadcrumbNeu = true;
            }
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PositionZeichnen();
            if (breadcrumbNeu) BreadcrumbZeichnen();
            double kurs = _kompassHatWert ? _heading : _gpsKurs;   // ohne Kompass-HW (N55): GPS-Fahrtrichtung
            // Route-VORSCHAU (Route liegt, aber Navigation noch nicht gestartet): die ganze Route bleibt
            // gefittet stehen – NICHT auf die Position folgen/zoomen/drehen (sonst rückt das Ziel aus dem Bild).
            bool inVorschau = _navPunkte != null && !_navAktiv;
            if (_zentrierenNaechsterFix && !inVorschau)
            {
                // Erster Fix (oder „Zentrieren" ohne vorherigen Fix): auf die Position zoomen und ausrichten.
                _zentrierenNaechsterFix = false;
                _map.Navigator.CenterOnAndZoomTo(_letztePos, Aufloesung(ZentrierZoom));
                if (_fahrtrichtung) _map.Navigator.RotateTo(-kurs);
            }
            else if (_folgen && KameraFrei && !inVorschau) _map.Navigator.CenterOn(_letztePos);
            // Kompass-Modus OHNE Kompass-Hardware: Karte in GPS-Fahrtrichtung drehen (greift nur bei Bewegung).
            if (_fahrtrichtung && !_kompassHatWert && KameraFrei && !inVorschau) _map.Navigator.RotateTo(-_gpsKurs);
            if (_navPunkte != null) AktualisiereNav(loc.Latitude, loc.Longitude);
        });
    }

    // ---- Kompass -----------------------------------------------------------
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
            // Chevron nur bei spürbarer Kurs-Änderung neu zeichnen (Redraw drosseln).
            if (Math.Abs(((_heading - _gezeichnetHeading + 540) % 360) - 180) > 3)
            { _gezeichnetHeading = _heading; PositionZeichnen(); }
            // RotateTo NUR bei spürbarer Kurs-Änderung (>1,5°): sonst dreht jeder Sensor-Tick (~16 Hz) die
            // Karte und löst einen Voll-Redraw aus (Akku/Jitter). Nicht während Touch / nicht in der Vorschau.
            if (_fahrtrichtung && KameraFrei && !(_navPunkte != null && !_navAktiv)
                && Math.Abs(((_heading - _gedrehtHeading + 540) % 360) - 180) > 1.5)
            { _gedrehtHeading = _heading; _map.Navigator.RotateTo(-_heading); }
        });
    }

    // ---- Karten-Tipp → Kontextmenü (wie Web: „Hierhin navigieren") ----------
    private async void AufKarteTipp(object? sender, MapInfoEventArgs e)
    {
        var wp = e.MapInfo?.WorldPosition;
        if (wp == null) return;
        if (Environment.TickCount64 - _letztLangdruckMs < 700) return;   // Langdruck hat das Menü gerade gezeigt
        var (lon, lat) = ZuGeo(wp.X, wp.Y);
        await KontextmenueZeigen(lat, lon);
    }

    // Kontextmenü „Was möchtest du tun?" – per kurzem Tipp (Map.Info) UND per Langdruck (Android) aufrufbar.
    private async Task KontextmenueZeigen(double lat, double lon)
    {
        string hierhin = L.T("ktx_hierhin"), vonHier = L.T("ktx_von_hier"), zumPlan = L.T("ktx_zum_plan");
        var optionen = new List<string> { hierhin, vonHier, zumPlan };
        optionen.Add(Standort.EntfernungZeile(lat, lon, _letzteGeo));   // Info-Zeile vor „Abbrechen"
        // „Bereich offline laden" ist in Einstellungen → Karte umgezogen (dort als „Umgebung offline laden").
        string wahl = await DisplayActionSheet(null, L.T("abbrechen"), null, optionen.ToArray());
        if (wahl == hierhin) await RouteZu(lat, lon);
        else if (wahl == vonHier)
        {
            // Läuft eine Navigation mit Ziel? → sofort umrouten: Route NEU ab dem geklickten
            // Punkt zum bisherigen Ziel berechnen (RouteZu nimmt _startUeberschreibung als Start).
            if (_navAktiv && _navZiel is { } ziel)
            {
                _startUeberschreibung = (lat, lon);
                await RouteZu(ziel.lat, ziel.lon);
            }
            else
            {
                // Keine Navigation aktiv: Startpunkt nur für die nächste Routenberechnung merken.
                _startUeberschreibung = (lat, lon);
                Status(L.T("st_start_gesetzt"), autoAus: true);
            }
        }
        else if (wahl == zumPlan) { _plan.Add((lat, lon)); PlanAnzeigen(); }
    }

    private async Task RouteZu(double zielLat, double zielLon, string? zielName = null)
    {
        var start = _startUeberschreibung ?? _letzteGeo;
        if (start == null)   // kurz auf den ersten GPS-Fix warten (Race beim Seitenwechsel/Kaltstart)
        {
            Status(L.T("st_warte_gps"));
            for (int i = 0; i < 24 && _letzteGeo == null; i++) await Task.Delay(250);
            start = _startUeberschreibung ?? _letzteGeo;
        }
        if (start == null) { Status(L.T("st_noch_kein_gps"), autoAus: true); return; }
        Status(L.T("st_route_berechnet"));
        try
        {
            var opt = RouteService.CostingOptionen(Einst.Profil, Einst.Wegtyp,
                Einst.VermeideAutobahn, Einst.VermeideUnbefestigt, Einst.VermeideSchlechteOberflaeche);
            var (r, alt) = await RouteService.RouteVollAsync(start.Value.lat, start.Value.lon, zielLat, zielLon,
                Einst.Profil, opt, Einst.NaviLocale, 2);
            if (r == null || r.Punkte.Count < 2) { Status(L.T("st_keine_route"), autoAus: true); return; }
            _startUeberschreibung = null;
            _istTour = false; _tourOriginal = null; _navZiel = (zielLat, zielLon);
            _alternativen = alt; _navMinuten = r.Minuten;
            var ank = DateTime.Now.AddMinutes(r.Minuten).ToString("HH:mm");
            NavStart(r.Punkte, r.Manoever, L.T("route_zusammenfassung", FmtKmVon(r.Km), r.Minuten), ank);
            Status(null);
            _ = ZielMerkenMitName(zielLat, zielLon, zielName);   // Name ggf. per Reverse-Geocoding (Hintergrund)
            _ = Auth.AktualisiereAsync();   // Tageszähler im Konto aktualisieren
        }
        catch (PaywallException) { await Paywall(); }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("st_routing_nicht_erreichbar"), autoAus: true); }
    }

    private async Task Paywall(string? text = null)
    {
        Status(null);
        text ??= L.T("paywall_text", Auth.GratisProTag);
        bool hin = await DisplayAlert(L.T("paywall_titel"), text, L.T("paywall_btn"), L.T("schliessen"));
        if (hin) await Shell.Current.GoToAsync("//konto");
    }

    // ---- gemeinsame Navigations-/Routen-Anzeige ----------------------------
    private void NavStart(List<(double lat, double lon)> punkte, List<Manoever> manoever, string infoText, string ankunft = "", bool fitKamera = true)
    {
        if (punkte == null || punkte.Count < 2) { Status(L.T("st_route_ungueltig"), autoAus: true); return; }
        _navAktiv = false;   // jede frisch berechnete Route startet in der Vorschau (Start-Knopf)
        ZeichneRoute(punkte, fitKamera);
        _navPunkte = punkte;
        _navKum = NavGeo.Kumulativ(punkte);
        _navManoever = manoever;
        _navGesamt = _navKum[^1];
        _ankunftText = ankunft;
        _letztGesprochen = -1;
        _vorabGesprochen = -1;
        _tonManoever = -1;
        _navIdx = 0;
        _zielAngesagt = false;
        NaviPanel.IsVisible = true;
        _ = HoeheLaden(punkte);
        DistNaechstLabel.Text = "";
        NaviInfoLabel.Text = infoText;
        ZielLabel.Text = string.IsNullOrEmpty(infoText) ? L.T("ziel") : infoText;
        VorschauSummary.Text = $"{FmtZeit(_navMinuten)} · {FmtKm(_navGesamt)}";   // Vorschau-Zusammenfassung
        TabsMarkieren();
        NaviZustandAnzeigen();
        AltAnzeigen();
        // Vorschau EINGEKLAPPT (Peek) zeigen, NICHT aufpoppen: der „Start"-Knopf sitzt jetzt klein
        // in der Peek-Zeile neben den Transport-Icons, daher muss die Schublade nicht aufklappen –
        // so bleibt die ganze (oben kamera-gefittete) Route sichtbar und zentriert.
        if (!_navAktiv) SheetSetzen(false, animiert: false);
        if (_navAktiv && _letzteGeo != null) AktualisiereNav(_letzteGeo.Value.lat, _letzteGeo.Value.lon);
    }

    // ---- Vorschau ⇄ Lauf (maps.me-Stil) ----
    private bool _navAktiv;

    private void NaviZustandAnzeigen()
    {
        NaviVorschau.IsVisible = !_navAktiv;
        NaviLaufKopf.IsVisible = _navAktiv;
        StartBtn.IsVisible = !_navAktiv;     // kleiner Start in der Transport-Zeile (Peek)
        StopBtnPeek.IsVisible = _navAktiv;   // einziger Stop, im immer sichtbaren Peek
        ZurueckBtn.IsVisible = !_navAktiv;   // „Zurück" zur Start-Seite nur in der Vorschau (vor Start / nach Stop)
        AnweisungBox.IsVisible = _navAktiv;
        TourAktionen.IsVisible = _istTour;   // Tour-Aktionen nur bei aktiver Tour (im Navi-Panel)
        if (_navAktiv) AltChip.IsVisible = false;   // nach Start keine graue Alternativ-Anzeige
    }

    private void TabsMarkieren()
    {
        TabAuto.StrokeThickness = TabFuss.StrokeThickness = TabRad.StrokeThickness = 0;
        var sel = Einst.Profil switch { "auto" => TabAuto, "bicycle" => TabRad, _ => TabFuss };
        sel.StrokeThickness = 2;
    }

    // Start-Knopf: Vorschau → aktive Navigation.
    private void OnStartNavigation(object? sender, EventArgs e) => NaviAktivieren();

    // Aktive Navigation einschalten (Start-Knopf der Vorschau ODER direkt beim Start aus dem
    // Tour-Detail-Dialog – dort soll ja keine Vorschau mit „Start" mehr kommen, die Navigation
    // läuft sofort und zeigt den Lauf-Kopf mit Stop).
    private void NaviAktivieren()
    {
        if (_navPunkte == null || _navPunkte.Count < 2) return;
        _navAktiv = true;
        _letztNotifText = "";
        _tonManoever = -1;
        _ = NaviNotif.BerechtigungAsync();   // Notification-Berechtigung fürs Watch-Spiegeln
        NaviZustandAnzeigen();
        SheetSetzen(false);   // Schublade auf kompakten Peek einklappen
        try { DeviceDisplay.Current.KeepScreenOn = Einst.BildschirmWach; } catch (Exception ex) { Debug.WriteLine(ex); }
        if (_letzteGeo != null) AktualisiereNav(_letzteGeo.Value.lat, _letzteGeo.Value.lon);
    }

    // Transport-Tab in der Vorschau: Modus wechseln + Route neu berechnen.
    private async void OnVorschauProfil(object? sender, TappedEventArgs e)
    {
        var profil = e.Parameter as string ?? "pedestrian";
        if (profil == Einst.Profil) return;
        Einst.Profil = profil;
        TabsMarkieren();
        if (_istTour && _tourOriginal != null) await NavStartGetracet(_tourOriginal, NaviInfoLabel.Text, _ankunftText, fit: false);
        else if (_navZiel is { } z) await RouteZu(z.lat, z.lon);
    }

    private void AktualisiereNav(double lat, double lon)
    {
        if (!_navAktiv || _navPunkte == null || _navKum == null) return;   // in der Vorschau noch keine Turn-by-Turn-Updates
        var (idx, entlang, abstand) = NavGeo.Projektion(lat, lon, _navPunkte, _navKum, _navIdx);
        _navIdx = idx;   // Fenster-Hinweis für den nächsten Tick (O(1) statt O(n))
        double rest = Math.Max(0, _navGesamt - entlang);
        RichtungPfeilAktualisieren(entlang);   // lila Richtungspfeil voraus mitführen

        // Auto-Reroute bei Abweichung von der Route (>50 m), gedrosselt.
        if (abstand > 50 && !_reroutLaeuft && Environment.TickCount64 - _letztRerouteMs > 6000)
        {
            _letztRerouteMs = Environment.TickCount64;
            _ = Reroute(lat, lon);
        }

        // Unten-Leiste (maps.me-Stil): Distanz zum Ziel · Restzeit · Ankunft (live aktualisiert).
        double restMin = _navGesamt > 1 ? _navMinuten * rest / _navGesamt : 0;
        DistZielLabel.Text = FmtKm(rest);
        ZeitLabel.Text = FmtZeit(restMin);
        AnkunftLabel.Text = DateTime.Now.AddMinutes(restMin).ToString("HH:mm");

        if (rest < ZielRadiusMeter)
        {
            // Ziel erreicht: einmal ansagen und die Navigation BEENDEN – sonst bliebe der
            // Turn-by-Turn-/Auto-Reroute-Lauf scharf (jede weitere Bewegung würde eine neue
            // Route zum bereits erreichten Ziel berechnen) und der Bildschirm wach.
            if (!_zielAngesagt)
            {
                _zielAngesagt = true;
                if (Einst.Ton) Sprich(NaviText("ansage_ziel_erreicht"));
                NavigationBeenden();   // setzt selbst Status(null) – daher Meldung DANACH setzen
                Status(L.T("st_ziel_erreicht"), autoAus: true);
            }
            return;
        }

        // nächstes Manöver = erstes, das DISTANZMÄSSIG noch vor uns liegt. Distanz statt
        // reinem Index-Vergleich (BeginIndex > idx) ist robust gegen GPS-Sprünge, die idx
        // genau auf einen Manöver-Stützpunkt setzen, und überspringt das Start-Manöver
        // (Distanz 0) ganz natürlich.
        int next = -1;
        for (int m = 0; m < _navManoever.Count; m++)
        {
            int bi = Math.Min(_navManoever[m].BeginIndex, _navKum.Length - 1);
            if (_navManoever[m].BeginIndex >= idx && _navKum[bi] > entlang) { next = m; break; }
        }

        if (next >= 0)
        {
            int bi = Math.Min(_navManoever[next].BeginIndex, _navKum.Length - 1);
            double distNext = Math.Max(0, _navKum[bi] - entlang);
            DistNaechstLabel.Text = FmtKm(distNext);
            AbbiegePfeil.Rotation = PfeilWinkel(_navManoever[next].Typ);
            AbbiegePfeil.IsVisible = true;
            AnweisungBox.IsVisible = true;   // Pfeil + Distanz einblenden
            // Watch-Spiegelung: Abbiege-Hinweis als (in-place) Notification → erscheint auf
            // gekoppelten Uhren (Apple Watch / Wear OS / Bluetooth-Uhren), ohne eigene Watch-App.
            string notifTxt = $"{FmtKm(distNext)}: {Saubere(_navManoever[next].Anweisung)}";
            if (notifTxt != _letztNotifText) { _letztNotifText = notifTxt; NaviNotif.Zeige(L.T("notif_navigation"), notifTxt); }
            // Benachrichtigungston: einmal je Manöver, sobald es in den Ansagebereich (<160 m) kommt –
            // unabhängig von den Sprachansagen (Einst.Ton), gesteuert über Einst.Benachrichtigungstoene.
            if (Einst.Benachrichtigungstoene && _tonManoever != next && distNext < 160)
            {
                _tonManoever = next;
                NaviNotif.Signalton();
            }
            // Zweistufige Ansage: Vorab „In N Metern …" (45–160 m), dann am Manöver die Anweisung.
            if (Einst.Ton)
            {
                if (distNext > 45 && distNext < 160 && _vorabGesprochen != next)
                { Sprich(NaviText("ansage_vorab", $"{Math.Round(distNext / 10) * 10:0}", Saubere(_navManoever[next].Anweisung))); _vorabGesprochen = next; }
                else if (distNext <= 45 && _letztGesprochen != next)
                { Sprich(Saubere(_navManoever[next].Anweisung)); _letztGesprochen = next; }
            }
        }
        else
        {
            AnweisungBox.IsVisible = false;   // keine Abbiegung voraus → nur der Route folgen, kein Pfeil
        }
    }

    // Distanz menschenlesbar – respektiert die Einheiten-Einstellung (Logik in Format.Strecke).
    private static string FmtKm(double m) => Format.Strecke(m, Einst.Einheiten == "imperial");

    // Strecken-Zusammenfassung; Eingabe in KILOMETERN (Route/Tour), Ausgabe einheitengerecht.
    private static string FmtKmVon(double km) => FmtKm(km * 1000);

    // Dauer: <1 min, Minuten, oder Stunden:Minuten.
    private static string FmtZeit(double min) => Format.Zeit(min);

    // Drehwinkel des Aufwärts-Pfeils nach Valhalla-Manövertyp (wie Web pfeilWinkel).
    private static double PfeilWinkel(int typ) => typ switch
    {
        9 or 18 or 23 => 45, 10 or 20 => 90, 11 => 135, 12 or 13 => 180,
        14 => -135, 15 or 21 => -90, 16 or 19 or 24 => -45, _ => 0,
    };

    private async Task Reroute(double lat, double lon)
    {
        _reroutLaeuft = true;
        try
        {
            var opt = RouteService.CostingOptionen(Einst.Profil, Einst.Wegtyp,
                Einst.VermeideAutobahn, Einst.VermeideUnbefestigt, Einst.VermeideSchlechteOberflaeche);
            if (_istTour && _tourOriginal != null)
            {
                var kum = NavGeo.Kumulativ(_tourOriginal);
                var (idx, _, _) = NavGeo.Projektion(lat, lon, _tourOriginal, kum);
                int ein = Math.Min(idx + 1, _tourOriginal.Count - 1);
                var rest = _tourOriginal.GetRange(ein, _tourOriginal.Count - ein);
                var r = await RouteService.RouteAsync(lat, lon, _tourOriginal[ein].lat, _tourOriginal[ein].lon,
                    Einst.Profil, opt, Einst.NaviLocale, folge: true);
                if (_navPunkte != null && r != null && r.Punkte.Count >= 2)   // Sitzung noch aktiv?
                {
                    var komb = new List<(double lat, double lon)>(r.Punkte);
                    komb.AddRange(rest);
                    await NavStartGetracet(komb, "", _ankunftText, fit: false);   // echte Manöver, ohne Kamera-Sprung
                    Status(L.T("st_route_neu"), autoAus: true);
                    if (Einst.Benachrichtigungstoene) NaviNotif.Signalton();   // Hinweis-Ton bei „Route neu"
                }
            }
            else if (_navZiel is { } z)
            {
                var (r, alt) = await RouteService.RouteVollAsync(lat, lon, z.lat, z.lon, Einst.Profil, opt, Einst.NaviLocale, 2, folge: true);
                if (_navPunkte != null && r != null && r.Punkte.Count >= 2)
                {
                    _alternativen = alt; _navMinuten = r.Minuten;
                    var ank = DateTime.Now.AddMinutes(r.Minuten).ToString("HH:mm");
                    NavStart(r.Punkte, r.Manoever, L.T("route_zusammenfassung", FmtKmVon(r.Km), r.Minuten), ank, fitKamera: false);
                    Status(L.T("st_route_neu"), autoAus: true);
                    if (Einst.Benachrichtigungstoene) NaviNotif.Signalton();   // Hinweis-Ton bei „Route neu"
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        finally { _reroutLaeuft = false; _letztRerouteMs = Environment.TickCount64; }   // auch nach Fehler 6 s Ruhe
    }

    // Valhalla sagt bei Rad „Radeln" – wie die Web-Navi auf „Fahren" glätten.
    private static string Saubere(string anw) => anw.Replace("Radeln", "Fahren").Replace("radeln", "fahren");

    /// <summary>Setzt eine Geometrie als Tour-Route, holt echte Manöver per Trace.</summary>
    private async Task NavStartGetracet(List<(double lat, double lon)> punkte, string info, string ank, bool fit)
    {
        string costing = Einst.Profil is "auto" or "bicycle" ? Einst.Profil : "pedestrian";
        RouteErgebnis? tr = null;
        try { tr = await RouteService.TraceAsync(punkte, costing, Einst.NaviLocale, folge: true); }   // Folge (Tour zählt schon)
        catch (Exception ex) { Debug.WriteLine(ex); }
        var pts = tr != null && tr.Punkte.Count >= 2 ? tr.Punkte : punkte;
        var man = tr?.Manoever ?? new List<Manoever>();
        _istTour = true; _tourOriginal = pts; _navZiel = pts[^1]; _alternativen.Clear();
        _navMinuten = tr?.Minuten ?? _navMinuten;
        NavStart(pts, man, info, ank, fit);
    }

    private async Task HoeheLaden(List<(double lat, double lon)> pts)
    {
        try
        {
            var profil = await HoeheService.ProfilAsync(pts);
            if (_navPunkte == null) return;   // Navigation zwischenzeitlich beendet
            _hoehe.Daten = profil;
            HoeheView.Invalidate();
            if (profil.Count > 1)
            {
                double auf = 0, ab = 0;
                for (int i = 1; i < profil.Count; i++)
                {
                    double d = profil[i].hoehe - profil[i - 1].hoehe;
                    if (d > 0) auf += d; else ab -= d;
                }
                HoeheInfo.Text = $"Höhenprofil  ↑ {auf:0} m  ↓ {ab:0} m";
                HoeheBlock.IsVisible = true;
            }
            else HoeheBlock.IsVisible = false;   // keine Höhendaten → Block ausblenden
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // ---- Aufklapp-Schublade (maps.me-Stil): halbhoch, Griff tippen = zu/auf, wischen = ziehen ----
    private double _sheetHoehe;            // volle Höhe der Schublade (~halbe Seite)
    private const double SheetPeek = 118;  // sichtbarer „Peek" im eingeklappten Zustand (Griff + Summary)
    private bool _sheetOffen;
    private double _panBasis;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (height > 0)
        {
            _sheetHoehe = height * 0.55;          // ~halbe Bildschirmhöhe
            NaviPanel.HeightRequest = _sheetHoehe;
            if (!_sheetOffen) NaviPanel.TranslationY = _sheetHoehe - SheetPeek;   // eingeklappt halten
        }
    }

    private void SheetSetzen(bool offen, bool animiert = true)
    {
        _sheetOffen = offen;
        double ziel = offen ? 0 : Math.Max(0, _sheetHoehe - SheetPeek);
        if (animiert) _ = NaviPanel.TranslateTo(0, ziel, 220, Easing.CubicOut);
        else NaviPanel.TranslationY = ziel;
        if (offen) HoeheView.Invalidate();
    }

    private void OnNaviPanel(object? sender, EventArgs e) => SheetSetzen(!_sheetOffen);

    private void OnNaviPanelPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panBasis = NaviPanel.TranslationY;
                break;
            case GestureStatus.Running:
                NaviPanel.TranslationY = Math.Clamp(_panBasis + e.TotalY, 0, Math.Max(0, _sheetHoehe - SheetPeek));
                break;
            case GestureStatus.Completed:
                SheetSetzen(NaviPanel.TranslationY < (_sheetHoehe - SheetPeek) / 2);   // näher an offen → offen
                break;
        }
    }

    // Passende TTS-Stimme zur NAVIGATIONS-Sprache (asynchron aufgelöst + gecacht). Die Ansagen
    // sollen in der Navi-Sprache klingen, NICHT in der App-Oberflächen- oder Gerätesprache.
    private Locale? _ttsLocale;
    private string _ttsLocaleFuer = "";

    private async void TtsLocaleAufloesen(string sprache)
    {
        _ttsLocaleFuer = sprache;   // sofort markieren → keine Doppel-Auflösung bei schnellen Ansagen
        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            // Bevorzugt eine Stimme mit passender Sprache (de/en); das Land (de-DE/de-AT …) ist egal.
            // StartsWith ist tolerant gegenüber 2-/3-Buchstaben-Codes je Plattform (de vs. deu).
            _ttsLocale = locales.FirstOrDefault(x =>
                !string.IsNullOrEmpty(x.Language) &&
                x.Language.StartsWith(sprache, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void Sprich(string text)
    {
        try
        {
            // Stimme passend zur Navi-Sprache wählen (asynchron auflösen + cachen). Solange noch nicht
            // aufgelöst, spricht das Gerät in seiner Standardstimme – ab der nächsten Ansage korrekt.
            if (_ttsLocaleFuer != Einst.NaviSprache) { _ttsLocale = null; TtsLocaleAufloesen(Einst.NaviSprache); }
            var opt = new SpeechOptions { Volume = (float)Math.Clamp(Einst.Ansagelautstaerke, 0, 1) };
            if (_ttsLocale != null && _ttsLocaleFuer == Einst.NaviSprache) opt.Locale = _ttsLocale;
            _ = TextToSpeech.Default.SpeakAsync(text, opt);
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // Ansage-Text in der NAVIGATIONS-Sprache (nicht in der App-Oberflächensprache). Die gesprochenen
    // Texte sollen zur Navi-Sprache passen, daher direkt aus der Texte-Tabelle in Einst.NaviSprache.
    private static string NaviText(string key) => Texte.Hole(key, Einst.NaviSprache);

    private static string NaviText(string key, params object[] args)
    {
        var muster = Texte.Hole(key, Einst.NaviSprache);
        try { return string.Format(muster, args); }
        catch (FormatException) { return muster; }
    }

    // „Stop" beendet die Turn-by-Turn-Navigation, kehrt aber zur Routen-VORSCHAU zurück
    // (maps.me-Stil): Panel + Route bleiben sichtbar, der „Start"-Knopf erscheint wieder,
    // damit man dieselbe Route neu starten oder ein neues Ziel wählen kann.
    // „Stop" beendet die Navigation und kehrt zur START-Seite zurück (nicht in die Vorschau mit „Start").
    private async void OnStop(object? sender, EventArgs e)
    {
        NavigationBeenden();
        try { await Shell.Current.GoToAsync("//start"); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // „Zurück"-Knopf (nur in der Vorschau): zurück zur Start-/Übersichtsseite.
    private async void OnZurueck(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("//start"); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void NavigationBeenden()
    {
        _navPunkte = null; _navKum = null; _navManoever = new();
        _letztGesprochen = -1; _zielAngesagt = false; _tonManoever = -1; _startUeberschreibung = null;
        _letztNotifText = ""; NaviNotif.Aus();   // Watch-Hinweis entfernen
        _alternativen.Clear(); AltChip.IsVisible = false;
        _navAktiv = false;
        _routeLayer.Features = new List<IFeature>();
        _routeLayer.DataHasChanged();
        RichtungAus();   // lila Richtungspfeil entfernen
        AnweisungBox.IsVisible = false;
        NaviPanel.IsVisible = false;
        SheetSetzen(false, animiert: false);   // Schublade eingeklappt zurücksetzen
        Status(null);
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void ZeichneRoute(List<(double lat, double lon)> pts, bool fitKamera = true)
    {
        var features = new List<IFeature>();
        // Alternativrouten grau gestrichelt (unter der Hauptroute)
        foreach (var alt in _alternativen)
        {
            if (alt.Punkte.Count < 2) continue;
            var ac = new Coordinate[alt.Punkte.Count];
            for (int i = 0; i < alt.Punkte.Count; i++)
            { var (x, y) = ZuMercator(alt.Punkte[i].lat, alt.Punkte[i].lon); ac[i] = new Coordinate(x, y); }
            var af = new GeometryFeature { Geometry = new LineString(ac) };
            af.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#94a3b8"), 5) { PenStyle = PenStyle.Dash } });
            features.Add(af);
        }
        var coords = new Coordinate[pts.Count];
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        for (int i = 0; i < pts.Count; i++)
        {
            var (x, y) = ZuMercator(pts[i].lat, pts[i].lon);
            coords[i] = new Coordinate(x, y);
            if (x < minx) minx = x; if (y < miny) miny = y;
            if (x > maxx) maxx = x; if (y > maxy) maxy = y;
        }
        // maps.me-Stil: weiße Kontur (Casing) + kräftige blaue Route darüber.
        var casing = new GeometryFeature { Geometry = new LineString(coords) };
        casing.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#ffffff"), 11) });
        features.Add(casing);
        var feature = new GeometryFeature { Geometry = new LineString(coords) };
        feature.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#1d6fe0"), 7) });
        features.Add(feature);
        _routeLayer.Features = features;
        _routeLayer.DataHasChanged();
        if (fitKamera && pts.Count > 1)
        {
            // Rand um die Route lassen (~15 %), damit sie nicht am Bildschirmrand klebt.
            double mx = (maxx - minx) * 0.15, my = (maxy - miny) * 0.15;
            if (mx <= 0) mx = 200; if (my <= 0) my = 200;
            _map.Navigator.ZoomToBox(new MRect(minx - mx, miny - my, maxx + mx, maxy + my));
            // Die untere Schublade (Peek) verdeckt den unteren Kartenrand. Darum die Ansicht um eine
            // halbe Peek-Höhe nach OBEN schieben (Center nach Süden) – so liegt die ganze Route inkl.
            // aktueller Position (Start) ÜBER dem Fenster und ist zentriert im sichtbaren Bereich.
            var vp = _map.Navigator.Viewport;
            if (vp.Resolution > 0 && vp.Height > 0)
            {
                double versatzPx = SheetPeek / 2.0 + 16;   // halbe Peek-Höhe + etwas Luft
                _map.Navigator.CenterOn(new MPoint(vp.CenterX, vp.CenterY - versatzPx * vp.Resolution));
            }
        }
    }

    // ---- Richtungspfeil (lila Schaft + Spitze) – Port aus navi_route.js/navi.js ----
    // Punkt + Segmentindex an kumulativer Distanz `e` (Meter) entlang der Route.
    private static (int idx, double lat, double lon) PunktBeiEntlang(
        List<(double lat, double lon)> route, double[] kum, double e)
    {
        int i = 0;
        while (i < kum.Length - 1 && kum[i + 1] < e) i++;
        double seg = Math.Max(1, kum[i + 1] - kum[i]);
        double t = Math.Clamp((e - kum[i]) / seg, 0, 1);
        var a = route[i];
        var b = route[Math.Min(i + 1, route.Count - 1)];
        return (i, a.lat + (b.lat - a.lat) * t, a.lon + (b.lon - a.lon) * t);
    }

    // Routen-Ausschnitt ab Distanz `von` (Meter) über `laenge` Meter nach VORNE –
    // inklusive aller Stützpunkte, damit der Pfeil sich bei Kurven mitbiegt.
    private static List<(double lat, double lon)> VorausPfad(
        List<(double lat, double lon)> route, double[] kum, double von, double laenge)
    {
        var pts = new List<(double lat, double lon)>();
        if (route.Count < 2) return pts;
        double gesamt = kum[^1];
        von = Math.Clamp(von, 0, gesamt);
        double bis = Math.Min(von + Math.Max(0, laenge), gesamt);
        if (bis - von < 0.5) return pts;               // praktisch am Ziel → nichts zeigen
        var s = PunktBeiEntlang(route, kum, von);
        var e = PunktBeiEntlang(route, kum, bis);
        pts.Add((s.lat, s.lon));
        for (int i = s.idx + 1; i <= e.idx; i++) pts.Add(route[i]);
        pts.Add((e.lat, e.lon));
        return pts;
    }

    // Meter bis zur nächsten ECHTEN Abbiegung ab `entlang` (∞ = keine in Sicht).
    private double AbbiegungVoraus(double entlang)
    {
        if (_navKum == null) return double.PositiveInfinity;
        foreach (var m in _navManoever)
        {
            if (PfeilWinkel(m.Typ) == 0 && m.Typ != 26 && m.Typ != 27) continue;   // geradeaus/Start/Ziel
            double s = _navKum[Math.Min(m.BeginIndex, _navKum.Length - 1)];
            if (s > entlang + 3) return s - entlang;
        }
        return double.PositiveInfinity;
    }

    // Richtungspfeil mitführen: lila Schaft (Linie + weißes Casing) entlang der Route
    // ab der Position nach vorne, der in einer Pfeilspitze (Chevron) endet.
    private void RichtungPfeilAktualisieren(double entlang)
    {
        _letztEntlang = entlang;   // für Neuzeichnen bei Zoom (Pfeilspitze ist pixelgroß)
        if (!_navAktiv || _navPunkte == null || _navKum == null || _navPunkte.Count < 2) { RichtungAus(); return; }
        double laenge = VorausMin;
        double dAbbieg = AbbiegungVoraus(entlang);
        if (dAbbieg <= VorausAbbieg)
            laenge = Math.Max(VorausMin, Math.Min(dAbbieg + VorausUeber, VorausAbbieg + VorausUeber));
        var pfad = VorausPfad(_navPunkte, _navKum, entlang, laenge);
        if (pfad.Count < 2) { RichtungAus(); return; }   // am Ziel → kein Pfeil

        var coords = new Coordinate[pfad.Count];
        for (int i = 0; i < pfad.Count; i++)
        { var (x, y) = ZuMercator(pfad[i].lat, pfad[i].lon); coords[i] = new Coordinate(x, y); }

        var feats = new List<IFeature>();
        // Schaft: weißes Casing + lila Linie
        var schaftCasing = new GeometryFeature { Geometry = new LineString(coords) };
        schaftCasing.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#ffffff"), 11) });
        feats.Add(schaftCasing);
        var schaft = new GeometryFeature { Geometry = new LineString(coords) };
        schaft.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#7c3aed"), 7) });
        feats.Add(schaft);

        // Gefüllte, gekerbte Pfeilspitze am Ende – EXAKT die Web-Form (pfeilSpitzeSvg).
        double tipX = coords[^1].X, tipY = coords[^1].Y;
        double dx = tipX - coords[^2].X, dy = tipY - coords[^2].Y;
        if (dx * dx + dy * dy > 0.0001)
        {
            double res = _map.Navigator.Viewport.Resolution;
            if (res <= 0) res = Aufloesung(ZentrierZoom);
            double th = Math.Atan2(dx, dy);   // Vorwärtsrichtung (vorwärts = (sinθ, cosθ))
            var ring = PfeilGeometrie(RichtungsSpitze, tipX, tipY, th, res);
            var spitze = new GeometryFeature { Geometry = new Polygon(new LinearRing(ring)) };
            spitze.Styles.Add(new VectorStyle
            {
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString("#7c3aed")),
                Outline = new Pen(Mapsui.Styles.Color.FromString("#ffffff"), 2.4f),
            });
            feats.Add(spitze);
        }

        _richtungLayer.Features = feats;
        _richtungLayer.DataHasChanged();
    }

    private void RichtungAus()
    {
        if (_richtungLayer.Features.Any())
        { _richtungLayer.Features = new List<IFeature>(); _richtungLayer.DataHasChanged(); }
    }

    // Bei spürbarer Zoom-Änderung (>5 %) den Chevron in neuer Pixelgröße nachzeichnen.
    private void ViewportGeaendert()
    {
        double res = _map.Navigator.Viewport.Resolution;
        if (res <= 0) return;
        // Nur bei echtem ZOOM (Auflösungsänderung) eingreifen – Pan/Zentrieren (GPS-Folgen) ignorieren.
        if (_letzteZoomRes > 0 && Math.Abs(res - _letzteZoomRes) / _letzteZoomRes < 0.002) return;
        _letzteZoomRes = res;
        // Route + Richtungspfeil (volle Vektor-Geometrie) während der Geste ausblenden → kein Vektor-
        // Rendering pro Frame, nur die flüssigen Kacheln + der bildschirm-konstante Beam bleiben.
        if (!_vektorenVerborgen)
        {
            _vektorenVerborgen = true;
            _routeLayer.Enabled = false;
            _richtungLayer.Enabled = false;
            _tourLayer.Enabled = false;
            _breadcrumbLayer.Enabled = false;
        }
        if (_zoomTimer == null)
        {
            _zoomTimer = Dispatcher.CreateTimer();
            _zoomTimer.Interval = TimeSpan.FromMilliseconds(150);
            _zoomTimer.IsRepeating = false;
            _zoomTimer.Tick += (_, __) => VektorenWiederZeigen();
        }
        _zoomTimer.Stop();
        _zoomTimer.Start();   // Debounce: erst 150 ms nach dem letzten Zoom-Tick wieder einblenden
    }

    private void VektorenWiederZeigen()
    {
        _vektorenVerborgen = false;
        _routeLayer.Enabled = true;
        _richtungLayer.Enabled = true;
        _tourLayer.Enabled = true;
        _breadcrumbLayer.Enabled = true;
        // Pfeilspitze ist pixelgroß → einmal auf die neue Auflösung neu zeichnen.
        if (_navAktiv && _navPunkte != null) RichtungPfeilAktualisieren(_letztEntlang);
        _map.RefreshGraphics();
    }

    // Web-Pfeilform (navi.js) für die lila Richtungsspitze: lokale Punkte, Ursprung = Drehzentrum, y = vorwärts.
    private static readonly (double x, double y)[] RichtungsSpitze =
        { (0, 15), (14, -13), (0, -5), (-14, -13) };                                 // pfeilSpitzeSvg

    // Lokale Pfeil-Punkte → Mercator-Ring: gedreht um Kurs th (vorwärts = (sinθ, cosθ)),
    // skaliert (s = Mercator je Pixel), zentriert bei (cx, cy).
    private static Coordinate[] PfeilGeometrie((double x, double y)[] lokal, double cx, double cy, double th, double s)
    {
        double cos = Math.Cos(th), sin = Math.Sin(th);
        var pts = new Coordinate[lokal.Length + 1];
        for (int i = 0; i < lokal.Length; i++)
        {
            double lx = lokal[i].x, ly = lokal[i].y;   // lx = rechts, ly = vorwärts (oben)
            pts[i] = new Coordinate(cx + (lx * cos + ly * sin) * s, cy + (-lx * sin + ly * cos) * s);
        }
        pts[^1] = pts[0];   // Ring schließen
        return pts;
    }

    // Standort-Beam (Google-Stil) als EINMALIG gerenderte Grafik: glatter radialer Alpha-Verlauf
    // (innen voll grün → außen transparent, OHNE Ringe/Linien/Kontur) + Standortpunkt. Wird als
    // bildschirm-konstantes, drehbares Symbol verwendet → kein Zoom-Redraw, kein Banding.
    private int _beamBitmapId = -1;

    private int BeamBitmapId()
    {
        if (_beamBitmapId >= 0) return _beamBitmapId;
        const int g = 240;                                   // Bitmapgröße (px); per SymbolScale verkleinert
        const float c = g / 2f;                              // Mitte = Standortpunkt = Drehzentrum
        const float radius = 104f;                           // Beam-Länge
        const float halb = 39f * (float)(Math.PI / 180);     // halber Öffnungswinkel (50 % breiter als zuvor 26°)
        var dunkel = new SkiaSharp.SKColor(22, 101, 52);     // Dunkelgrün (#166534): Punkt UND Trichter-Basis
        using var bitmap = new SkiaSharp.SKBitmap(g, g);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            // Trichter: Sektor nach oben, beginnt DIREKT an der Position (Verlauf ab 0, kein Loch),
            // dunkelgrün an der Position → nach außen transparent. Kein weißer Ring.
            using var shader = SkiaSharp.SKShader.CreateRadialGradient(
                new SkiaSharp.SKPoint(c, c), radius,
                new[] { dunkel.WithAlpha(210), dunkel.WithAlpha(0) },
                new[] { 0f, 1f }, SkiaSharp.SKShaderTileMode.Clamp);
            using var beam = new SkiaSharp.SKPaint { Shader = shader, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
            using var pfad = new SkiaSharp.SKPath();
            pfad.MoveTo(c, c);
            for (int i = 0; i <= 28; i++)
            {
                float ang = -halb + 2 * halb * i / 28f;       // um die Aufwärtsachse (−y)
                pfad.LineTo(c + radius * (float)Math.Sin(ang), c - radius * (float)Math.Cos(ang));
            }
            pfad.Close();
            canvas.DrawPath(pfad, beam);
            // Standortpunkt: nur dunkelgrün, KEIN weißer Ring.
            using var fuell = new SkiaSharp.SKPaint { Color = dunkel, IsAntialias = true };
            canvas.DrawCircle(c, c, 15f, fuell);
        }
        using var img = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        _beamBitmapId = Mapsui.Styles.BitmapRegistry.Instance.Register(new System.IO.MemoryStream(png.ToArray()), "positions_beam");
        return _beamBitmapId;
    }

    // Standortanzeige im Google-Maps-Stil: Standortpunkt + glatter Blickrichtungs-Beam (gerenderte
    // Bitmap mit Alpha-Verlauf). Bildschirm-konstant (kein Zoom-Redraw), dreht mit Kompass/GPS-Kurs.
    private void PositionZeichnen()
    {
        if (_letztePos == null || _letzteGeo == null)
        {
            _positionLayer.Features = new List<IFeature>();
            _positionLayer.DataHasChanged();
            return;
        }
        double kursGrad = _kompassHatWert ? _heading : _gpsKurs;   // Blickrichtung in Grad
        var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(_letztePos.X, _letztePos.Y) };
        f.Styles.Add(new SymbolStyle
        {
            BitmapId = BeamBitmapId(),
            SymbolScale = 0.5,
            SymbolRotation = kursGrad,    // dreht in Blickrichtung …
            RotateWithMap = true,         // … und mit der Karte (Norden-/Fahrtrichtungs-Ansicht korrekt)
            Fill = null, Outline = null,  // kein Standard-Ellipsen-Symbol (heller Ring) hinter der Bitmap
        });
        _positionLayer.Features = new List<IFeature> { f };
        _positionLayer.DataHasChanged();
    }

    // Breadcrumb-Spur („Brotkrumen") als dezente grau-blaue, dünne, halbtransparente Linie zeichnen
    // (Mercator-Geometrie wie die Route, aber unauffälliger und UNTER der aktiven Route).
    private void BreadcrumbZeichnen()
    {
        List<(double lat, double lon)> pts;
        lock (_breadcrumbLock) pts = new List<(double lat, double lon)>(_breadcrumb);
        if (pts.Count < 2)
        {
            if (_breadcrumbLayer.Features.Any())
            { _breadcrumbLayer.Features = new List<IFeature>(); _breadcrumbLayer.DataHasChanged(); }
            return;
        }
        var coords = new Coordinate[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        { var (x, y) = ZuMercator(pts[i].lat, pts[i].lon); coords[i] = new Coordinate(x, y); }
        var f = new GeometryFeature { Geometry = new LineString(coords) };
        // dezent: grau-blau (#64748b), dünn, halbtransparent (Alpha 150) – liegt unter der kräftigen Route.
        f.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromArgb(150, 100, 116, 139), 4) });
        _breadcrumbLayer.Features = new List<IFeature> { f };
        _breadcrumbLayer.DataHasChanged();
    }

    private void AltAnzeigen()
    {
        // Alternativrouten-Hinweis („Alternative: …") auf Wunsch des Nutzers NICHT anzeigen.
        AltChip.IsVisible = false;
    }

    private void OnAltWaehlen(object? sender, EventArgs e)
    {
        if (_alternativen.Count == 0 || _navPunkte == null) return;
        var neu = _alternativen[0];
        var rest = new List<RouteErgebnis>(_alternativen.Skip(1));
        rest.Add(new RouteErgebnis(_navPunkte, _navGesamt / 1000.0, _navMinuten, _navManoever));   // bisherige Haupt → Alternative
        _alternativen = rest;
        _navZiel = neu.Punkte[^1]; _navMinuten = neu.Minuten;
        var ank = DateTime.Now.AddMinutes(neu.Minuten).ToString("HH:mm");
        NavStart(neu.Punkte, neu.Manoever, $"{FmtKmVon(neu.Km)} · {neu.Minuten:0} min", ank, fitKamera: false);
    }

    // ---- Steuerknöpfe ------------------------------------------------------

    // Zentrieren/Kompass-Toggle: idle → Norden+folgen → Fahrtrichtung → Norden …
    private void OnZentrieren(object? sender, EventArgs e)
    {
        bool warFolgen = _folgen;
        _folgen = true;
        Einst.Folgen = true;
        // Aus „nicht folgen" → in Blickrichtung ausrichten; weitere Tipps schalten Norden ↔ Fahrtrichtung um.
        _fahrtrichtung = warFolgen ? !_fahrtrichtung : true;
        try { _map.Navigator.RotationLock = false; } catch (Exception ex) { Debug.WriteLine(ex); }   // für die programmatische Drehung entsperren
        if (_letztePos != null)
        {
            _map.Navigator.CenterOnAndZoomTo(_letztePos, Aufloesung(ZentrierZoom));
            _map.Navigator.RotateTo(_fahrtrichtung ? -_heading : 0);
        }
        else
        {
            // Noch kein GPS-Standort: sobald der erste Fix kommt, automatisch dorthin zentrieren.
            _zentrierenNaechsterFix = true;
            Status(L.T("st_warte_gps"), autoAus: true);
        }
        RotationssperreAktualisieren();   // endgültige Sperre setzen (abhängig von Fahrtrichtung/Einstellung)
        KompassIconAktualisieren();
    }

    // Manuelle Karten-Drehung (Zwei-Finger-Geste) sperren, wenn der Nutzer sie ausgeschaltet
    // hat – außer die Fahrtrichtungs-Ansicht ist aktiv, dann dreht die App programmatisch mit.
    private void RotationssperreAktualisieren()
    {
        try { _map.Navigator.RotationLock = !Einst.ManuelleDrehung && !_fahrtrichtung; }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // Karten-/Routing-Einstellungen, die auf der Einstellungen-Seite geändert worden sein
    // können, auf die Live-Karte übernehmen (Kartenmodus, Fortbewegungsprofil, Overlay).
    private void KartenEinstellungenAnwenden()
    {
        // Effektiven Basiskarten-Modus aus Farbmodus + Kartenwahl bestimmen und nur tauschen, wenn er
        // sich gegenüber der angewandten Ebene geändert hat (Kartenwahl, Farbmodus oder – bei „auto" –
        // das System-Theme). „auto" wird hier bei jeder Rückkehr auf die Karte neu ausgewertet.
        var effektiv = EffektiverKartenmodus();
        if (effektiv != _modusJetzt) BasiskarteSetzen(effektiv);
        if (Einst.Profil != _profilJetzt) { WanderLayerSetzen(); TabsMarkieren(); }
        if (_wanderLayer.Enabled != Einst.Wanderwege) { _wanderLayer.Enabled = Einst.Wanderwege; _map.RefreshGraphics(); }
    }

    private void KompassIconAktualisieren()
    {
        bool nord = _folgen && !_fahrtrichtung;
        if (NordIcon != null) NordIcon.IsVisible = nord;
        if (LocateIcon != null) LocateIcon.IsVisible = !nord;
        if (ZentrierenBorder != null) ZentrierenBorder.BackgroundColor = nord ? Weiss : Rot;
    }

    private void OnTon(object? sender, EventArgs e)
    {
        Einst.Ton = !Einst.Ton;
        TonIconAktualisieren();
        if (Einst.Ton) Sprich(NaviText("ansage_sprachansagen_an"));
    }

    private void TonIconAktualisieren()
    {
        bool an = Einst.Ton;
        if (TonBorder != null) TonBorder.BackgroundColor = an ? Rot : Weiss;
        var farbe = an ? Weiss : Dunkel;
        if (TonKegel != null) TonKegel.Fill = new SolidColorBrush(farbe);
        if (TonWellen != null) { TonWellen.IsVisible = an; TonWellen.Stroke = new SolidColorBrush(farbe); }
        if (TonAus != null) { TonAus.IsVisible = !an; TonAus.Stroke = new SolidColorBrush(farbe); }
    }

    private void OnSuche(object? sender, EventArgs e) => _ = PoiSuche();

    private async Task PoiSuche()
    {
        // Letzte Ziele zum Schnell-Wiederholen anbieten (aus dem entfernten Karten-Sheet hierher).
        var ziele = LetzteZiele();
        if (ziele.Count > 0)
        {
            string suchen = L.T("poi_nach_name");
            var optionen = ziele.Select(z => "📍 " + z.name).Append(suchen).ToArray();
            string wahl = await DisplayActionSheet(L.T("poi_wohin"), L.T("abbrechen"), null, optionen);
            if (string.IsNullOrEmpty(wahl) || wahl == L.T("abbrechen")) return;
            if (wahl != suchen)
            {
                var z = ziele.FirstOrDefault(x => "📍 " + x.name == wahl);
                if (z.name != null) { await RouteZu(z.lat, z.lon, z.name); return; }
            }
        }
        string q = await DisplayPromptAsync(L.T("poi_orte_suchen_titel"), L.T("poi_orte_suchen_msg"), L.T("poi_suchen_btn"), L.T("abbrechen"), L.T("poi_placeholder"));
        if (string.IsNullOrWhiteSpace(q)) return;
        var vp = _map.Navigator.Viewport;
        double halbB = vp.Width / 2.0 * vp.Resolution, halbH = vp.Height / 2.0 * vp.Resolution;
        var (w, s) = ZuGeo(vp.CenterX - halbB, vp.CenterY - halbH);
        var (o, n) = ZuGeo(vp.CenterX + halbB, vp.CenterY + halbH);
        Status(L.T("st_suche"));
        try
        {
            var treffer = await PoiService.SucheAsync(w, s, o, n, q);
            if (treffer.Count == 0) { Status(L.T("st_nichts_gefunden"), autoAus: true); return; }
            Status(null);
            var namen = treffer.Take(10).Select(t => t.Name).ToArray();
            string wahl = await DisplayActionSheet(L.T("poi_treffer", treffer.Count), L.T("abbrechen"), null, namen);
            var p = treffer.FirstOrDefault(t => t.Name == wahl);
            if (p == null) return;
            var (px, py) = ZuMercator(p.Lat, p.Lon);
            _folgen = false;
            _map.Navigator.CenterOnAndZoomTo(new MPoint(px, py), Aufloesung(ZentrierZoom));
            KompassIconAktualisieren();
            if (await DisplayAlert(p.Name, L.T("poi_dorthin_navigieren"), L.T("poi_navigieren_btn"), L.T("schliessen")))
                await RouteZu(p.Lat, p.Lon, p.Name);
        }
        catch (PaywallException)
        {
            await Paywall(L.T("paywall_suche_text"));
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("st_suche_fehlgeschlagen"), autoAus: true); }
    }

    private void OnVollbild(object? sender, EventArgs e)
    {
        _vollbild = !_vollbild;
        VollAuf.IsVisible = !_vollbild;
        VollZu.IsVisible = _vollbild;
        VollBorder.BackgroundColor = _vollbild ? Teal : Weiss;
        // Vollbild blendet die Kopfleiste (Shell-NavBar) + Overlays aus, damit mehr Karte
        // sichtbar ist. Die rechte Steuerleiste bleibt, damit man Vollbild wieder verlassen kann.
        Shell.SetNavBarIsVisible(this, !_vollbild);
        bool routeDa = _navPunkte != null;             // Route vorhanden (Vorschau ODER Lauf)
        NaviPanel.IsVisible = !_vollbild && routeDa;
        // Die Abbiege-Box gehört nur in die AKTIVE Navigation, nicht in die Vorschau.
        AnweisungBox.IsVisible = !_vollbild && _navAktiv;
        if (!_vollbild && _navAktiv && _letzteGeo != null)   // Abbiege-Box korrekt nachführen
            AktualisiereNav(_letzteGeo.Value.lat, _letzteGeo.Value.lon);
    }

    private async Task OfflineLaden()
    {
        // Offline-Kontingent durchsetzen: P8 unbegrenzt, P5 = 3 inklusive, sonst nur gekaufte Karten.
        int budget = Auth.AlleFunktionen ? int.MaxValue
                   : (Auth.Premium ? AppKontoOfflineP5 : 0) + Auth.OfflineGekauft;
        if (Einst.OfflineAnzahl >= budget)
        {
            await Paywall(L.T("offline_paywall_text"));
            return;
        }

        var vp = _map.Navigator.Viewport;
        double halbB = vp.Width / 2.0 * vp.Resolution, halbH = vp.Height / 2.0 * vp.Resolution;
        var bereich = new MRect(vp.CenterX - halbB, vp.CenterY - halbH, vp.CenterX + halbB, vp.CenterY + halbH);
        int z = (int)Math.Round(Math.Log2(MercatorAufloesungZoom0 / vp.Resolution));
        var prog = new Progress<(int done, int total)>(p => Status(L.T("offline_fortschritt", p.done, p.total)));
        try
        {
            int n = await Task.Run(() => OfflineKarte.DownloadAsync(
                _aktiveQuelle, bereich, Math.Max(1, z), Math.Min(z + OfflineExtraZoom, MaxOsmZoom), OfflineMaxKacheln, prog));
            if (n > 0) Einst.OfflineAnzahl++;        // erfolgreich geladenen Bereich aufs Kontingent anrechnen
            Status(L.T("st_offline_gespeichert", n), autoAus: true);
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("st_offline_fehler"), autoAus: true); }
    }

    // ---- Karten-/Routing-Anwendung (Quellen-Wechsel; Steuerung jetzt in der Einstellungen-Seite) ----
    // Effektiver Kartenmodus = Nutzer-Kartenwahl (Einst.Karte), bei Farbmodus „nacht" bzw. „auto"+System
    // dunkel auf Dunkel umgebogen. „auto" wertet das System-Theme bei jedem Aufruf neu aus.
    private Kartenmodus EffektiverKartenmodus()
    {
        bool systemDunkel = Application.Current?.RequestedTheme == AppTheme.Dark;
        return MapQuellen.EffektiverModus(Einst.Farbmodus, Einst.Karte, systemDunkel);
    }

    // Tauscht die Basiskarten-Ebene auf den gegebenen (effektiven) Modus, OHNE die Nutzer-Kartenwahl
    // (Einst.Karte) zu überschreiben – sonst würde der Nachtmodus den gewählten Tag-Modus dauerhaft
    // ersetzen. Die Kartenwahl wird ausschließlich auf der Einstellungen-Seite gesetzt.
    private void BasiskarteSetzen(Kartenmodus m)
    {
        _aktiveQuelle = MapQuellen.Quelle(m);
        var neu = new TileLayer(_aktiveQuelle) { Name = "Basis" };
        var alt = _basisLayer;
        _map.Layers.Remove(alt);
        _map.Layers.Insert(0, neu);
        _basisLayer = neu;
        _modusJetzt = m;
        (alt as IDisposable)?.Dispose();   // alten TileLayer freigeben (kein Leak)
        _map.RefreshGraphics();
    }

    private void WanderLayerSetzen()
    {
        var neu = new TileLayer(MapQuellen.Wanderwege(Einst.Profil == "bicycle"))
        { Name = "Wanderwege", Enabled = Einst.Wanderwege };
        var alt = _wanderLayer;
        int idx = _map.Layers.ToList().IndexOf(alt);
        if (idx < 0) idx = 1;
        _map.Layers.Remove(alt);
        _map.Layers.Insert(idx, neu);
        _wanderLayer = neu;
        _profilJetzt = Einst.Profil;
        (alt as IDisposable)?.Dispose();   // alten Wander-TileLayer freigeben (kein Leak)
        _map.RefreshGraphics();
    }

    private async void OnUmkehr(object? sender, EventArgs e)
    {
        if (_navPunkte == null) return;
        var umgekehrt = new List<(double lat, double lon)>(_navPunkte);
        umgekehrt.Reverse();
        await NavStartGetracet(umgekehrt, L.T("mp_route_umgekehrt"), _ankunftText, fit: true);   // korrekte Manöver rückwärts
        Status(L.T("st_route_umgekehrt"), autoAus: true);
    }

    // Tour-Anfahrt: zum Startpunkt der Tour …
    private async void OnZumStart(object? sender, EventArgs e)
    {
        var tour = _tourOriginal ?? _navPunkte;
        if (tour == null || tour.Count == 0) return;
        await AnfahrtUndTour(tour[0].lat, tour[0].lon, new List<(double lat, double lon)>(tour));
    }

    // ---- Routenplan (mehrere Wegpunkte) ------------------------------------
    private void PlanAnzeigen()
    {
        if (_plan.Count > 0) { PlanChip.IsVisible = true; PlanLos.Text = L.T("plan_los", _plan.Count); }
        else PlanChip.IsVisible = false;
    }

    private void OnPlanLeeren(object? sender, EventArgs e) { _plan.Clear(); PlanAnzeigen(); }

    private async void OnPlanNavigieren(object? sender, EventArgs e)
    {
        if (_letzteGeo == null || _plan.Count == 0) return;
        Status(L.T("st_plan_berechnet"));
        try
        {
            // Von der Position über alle Wegpunkte routen (segmentweise verkettet).
            var stationen = new List<(double lat, double lon)> { _letzteGeo.Value };
            stationen.AddRange(_plan);
            var opt = RouteService.CostingOptionen(Einst.Profil, Einst.Wegtyp,
                Einst.VermeideAutobahn, Einst.VermeideUnbefestigt, Einst.VermeideSchlechteOberflaeche);
            var alle = new List<(double lat, double lon)>();
            var manAll = new List<Manoever>();
            double kmSum = 0, minSum = 0;
            for (int i = 0; i < stationen.Count - 1; i++)
            {
                var r = await RouteService.RouteAsync(stationen[i].lat, stationen[i].lon,
                    stationen[i + 1].lat, stationen[i + 1].lon, Einst.Profil, opt, Einst.NaviLocale, folge: i > 0);
                if (r == null || r.Punkte.Count < 2) continue;
                int off = alle.Count;
                foreach (var m in r.Manoever)
                    manAll.Add(m with { BeginIndex = m.BeginIndex + off });
                alle.AddRange(r.Punkte);
                kmSum += r.Km; minSum += r.Minuten;
            }
            if (alle.Count < 2) { Status(L.T("st_kein_plan"), autoAus: true); return; }
            _istTour = false; _tourOriginal = null; _navZiel = alle[^1]; _alternativen.Clear(); _navMinuten = minSum;
            var ank = DateTime.Now.AddMinutes(minSum).ToString("HH:mm");
            NavStart(alle, manAll, L.T("route_zusammenfassung", FmtKmVon(kmSum), minSum), ank);
            _plan.Clear(); PlanAnzeigen();
            Status(null);
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("st_plan_nicht_erreichbar"), autoAus: true); }
    }

    // ---- Track-Aufnahme (lokal, GPX-Export per Teilen) ---------------------
    // Aufnahme automatisch beim App-Start beginnen (je nach Einstellung).
    private void AufnahmeStart()
    {
        lock (_trackLock) { _track.Clear(); if (_letzteGeo is { } g) _track.Add((g.lat, g.lon)); }
        _aufnahme = true;
        _aufnahmeStart = DateTime.Now;
    }

    // Laufende Auto-Aufnahme abschließen: lokal speichern (Upload erfolgt beim App-Ende/-Start).
    private void AufnahmeFinalisieren()
    {
        if (!_aufnahme) return;
        _aufnahme = false;
        _autoAufnahmeProbiert = false;   // beim nächsten Erscheinen neues Segment aufzeichnen
        List<(double lat, double lon)> kopie;
        lock (_trackLock) kopie = new List<(double, double)>(_track);
        if (kopie.Count < 2) return;
        try
        {
            int dauer = (int)(DateTime.Now - _aufnahmeStart).TotalSeconds;
            AufnahmeService.SpeichereLokal(kopie, L.T("aufnahme_name", DateTime.Now.ToString("dd.MM. HH:mm")), dauer);
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    /// <summary>Statischer Einsprung fürs App-Lebenszyklus: laufende Aufnahme sichern (vor Upload).</summary>
    public static void AktiveAufnahmeSichern() => _aktuell?.AufnahmeFinalisieren();

    // ---- Letzte Ziele (lokal, ohne Login) ----------------------------------
    // Ziel merken; ist kein Name bekannt (z. B. Karten-Tipp), best-effort per Reverse-Geocoding.
    private async Task ZielMerkenMitName(double lat, double lon, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            try { name = await GeocodeService.ReverseAsync(lat, lon); } catch (Exception ex) { Debug.WriteLine(ex); }
        }
        ZielMerken(lat, lon, string.IsNullOrEmpty(name) ? L.T("ziel") : name);
    }

    private void ZielMerken(double lat, double lon, string name)
    {
        try
        {
            var liste = LetzteZiele();
            liste.RemoveAll(z => Math.Abs(z.lat - lat) < 1e-5 && Math.Abs(z.lon - lon) < 1e-5);
            liste.Insert(0, (lat, lon, name));
            if (liste.Count > 5) liste = liste.GetRange(0, 5);
            var arr = liste.Select(z => new { z.lat, z.lon, z.name });
            Preferences.Set("ziele", System.Text.Json.JsonSerializer.Serialize(arr));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private List<(double lat, double lon, string name)> LetzteZiele()
    {
        var liste = new List<(double, double, string)>();
        try
        {
            var roh = Preferences.Get("ziele", "");
            if (string.IsNullOrEmpty(roh)) return liste;
            using var doc = System.Text.Json.JsonDocument.Parse(roh);
            foreach (var e in doc.RootElement.EnumerateArray())
                liste.Add((e.GetProperty("lat").GetDouble(), e.GetProperty("lon").GetDouble(),
                    e.TryGetProperty("name", out var n) ? n.GetString() ?? L.T("ziel") : L.T("ziel")));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        return liste;
    }

    // … bzw. zum nächstgelegenen noch ausstehenden Routenpunkt.
    private async void OnZumNaechsten(object? sender, EventArgs e)
    {
        if (_navPunkte == null || _navKum == null || _letzteGeo == null) return;
        var (idx, _, _) = NavGeo.Projektion(_letzteGeo.Value.lat, _letzteGeo.Value.lon, _navPunkte, _navKum);
        int ein = Math.Min(idx + 1, _navPunkte.Count - 1);
        var rest = _navPunkte.GetRange(ein, _navPunkte.Count - ein);
        await AnfahrtUndTour(_navPunkte[ein].lat, _navPunkte[ein].lon, rest);
    }

    // Routet GPS → Zielpunkt und hängt den Tour-Rest an (eine durchgehende Route).
    private async Task AnfahrtUndTour(double zlat, double zlon, List<(double lat, double lon)> rest)
    {
        if (_letzteGeo == null) { Status(L.T("st_noch_kein_gps"), autoAus: true); return; }
        Status(L.T("st_anfahrt_berechnet"));
        try
        {
            var opt = RouteService.CostingOptionen(Einst.Profil, Einst.Wegtyp,
                Einst.VermeideAutobahn, Einst.VermeideUnbefestigt, Einst.VermeideSchlechteOberflaeche);
            var r = await RouteService.RouteAsync(_letzteGeo.Value.lat, _letzteGeo.Value.lon, zlat, zlon,
                Einst.Profil, opt, Einst.NaviLocale, folge: true);
            if (r == null || r.Punkte.Count < 2) { Status(L.T("st_keine_anfahrt"), autoAus: true); return; }
            var komb = new List<(double lat, double lon)>(r.Punkte);
            komb.AddRange(rest);
            var ank = DateTime.Now.AddMinutes(r.Minuten).ToString("HH:mm");
            await NavStartGetracet(komb, L.T("mp_anfahrt_tour"), ank, fit: true);   // echte Manöver der ganzen Strecke
            Status(null);
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("st_anfahrt_nicht_erreichbar"), autoAus: true); }
    }

    // ---- Gruppen-Position (Live-Standort teilen) ---------------------------
    private static string GruppenAnzeigename()
    {
        if (!string.IsNullOrWhiteSpace(Einst.GruppenName)) return Einst.GruppenName.Trim();
        if (!string.IsNullOrWhiteSpace(Auth.Name)) return Auth.Name.Trim();
        return L.T("gruppe_default_name");
    }

    private void GruppeIconAktualisieren()
    {
        bool aktiv = _gruppeCode.Length > 0;
        if (GruppeBorder != null) GruppeBorder.BackgroundColor = aktiv ? Microsoft.Maui.Graphics.Color.FromArgb("#16a34a") : Weiss;
        if (GruppeBadge != null) GruppeBadge.IsVisible = aktiv;
        if (aktiv && GruppeBadgeText != null && string.IsNullOrEmpty(GruppeBadgeText.Text)) GruppeBadgeText.Text = "0";
    }

    private void GruppeStart()
    {
        if (_gruppeTimer == null)
        {
            _gruppeTimer = Dispatcher.CreateTimer();
            _gruppeTimer.Interval = TimeSpan.FromSeconds(5);   // Mitglieder-Positionen alle 5 s nachladen
            _gruppeTimer.IsRepeating = true;
            _gruppeTimer.Tick += (_, __) => _ = GruppeAktualisieren();
        }
        _gruppeTimer.Stop();
        _gruppeTimer.Start();
        _ = GruppeAktualisieren();   // sofort einmal laden
    }

    private void GruppeStop() => _gruppeTimer?.Stop();   // Code bleibt erhalten (Fortsetzung bei OnAppearing)

    private async Task GruppeAktualisieren()
    {
        if (_gruppeCode.Length == 0) return;
        var mitglieder = await GruppeService.HoleAsync(_gruppeCode);
        if (_gruppeCode.Length == 0) return;   // zwischenzeitlich verlassen
        GruppeZeichnen(mitglieder);
    }

    // Mitglieder als beschriftete Marker zeichnen (frisch = orange, veraltet = grau); mich selbst
    // (eigener Beam) auslassen. Badge zeigt die Zahl der sichtbaren Mitreisenden.
    private void GruppeZeichnen(List<GruppenMitglied> mitglieder)
    {
        string ich = GruppenAnzeigename();
        var feats = new List<IFeature>();
        int andere = 0;
        foreach (var m in mitglieder)
        {
            if (string.Equals(m.Name, ich, StringComparison.OrdinalIgnoreCase)) continue;   // mich nicht doppelt
            andere++;
            var (x, y) = ZuMercator(m.Lat, m.Lng);
            string farbe = m.AlterS < 90 ? "#f59e0b" : "#94a3b8";   // frisch orange, veraltet grau
            var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(x, y) };
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse, SymbolScale = 0.7,
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString(farbe)),
                Outline = new Pen(Mapsui.Styles.Color.White, 2),
            });
            f.Styles.Add(new LabelStyle
            {
                Text = m.Name, Offset = new Offset(0, 20), Font = new Mapsui.Styles.Font { Size = 12, Bold = true },
                ForeColor = Mapsui.Styles.Color.FromString("#0f172a"),
                BackColor = new Mapsui.Styles.Brush(Mapsui.Styles.Color.White),
                Halo = new Pen(Mapsui.Styles.Color.White, 2),
            });
            feats.Add(f);
        }
        _gruppeLayer.Features = feats;
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
        if (GruppeBadgeText != null) GruppeBadgeText.Text = andere.ToString();
        if (GruppeBadge != null) GruppeBadge.IsVisible = _gruppeCode.Length > 0;
    }

    private async void OnGruppe(object? sender, EventArgs e)
    {
        if (_gruppeCode.Length == 0) { await GruppeBeitretenDialog(); return; }
        string verlassen = L.T("gruppe_verlassen"), nameAendern = L.T("gruppe_name_aendern");
        string wahl = await DisplayActionSheet(L.T("gruppe_aktiv_titel", _gruppeCode), L.T("abbrechen"), verlassen, nameAendern);
        if (wahl == verlassen) GruppeVerlassen();
        else if (wahl == nameAendern) await GruppeNameDialog();
    }

    private async Task GruppeBeitretenDialog()
    {
        string code = await DisplayPromptAsync(L.T("gruppe_titel"), L.T("gruppe_code_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), L.T("gruppe_code_placeholder"), maxLength: 32);
        code = GruppeService.CodeSaeubern(code);
        if (code.Length == 0) return;   // abgebrochen oder leer
        string vorgabe = GruppenAnzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (name == null) return;   // abgebrochen
        name = string.IsNullOrWhiteSpace(name) ? vorgabe : name.Trim();
        Einst.GruppenName = name;
        _gruppeCode = code; Einst.GruppenCode = code;
        GruppeIconAktualisieren();
        GruppeStart();
        if (_letzteGeo is { } g)   // sofort die eigene Position teilen
        {
            _letztGruppeSendeMs = Environment.TickCount64;
            _ = GruppeService.SendePositionAsync(_gruppeCode, name, g.lat, g.lon);
        }
        Status(L.T("gruppe_beigetreten", code), autoAus: true);
    }

    private void GruppeVerlassen()
    {
        _gruppeCode = ""; Einst.GruppenCode = "";
        GruppeStop();
        _gruppeLayer.Features = new List<IFeature>();
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
        GruppeIconAktualisieren();
        Status(L.T("gruppe_verlassen_ok"), autoAus: true);
    }

    private async Task GruppeNameDialog()
    {
        string vorgabe = GruppenAnzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("ok"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (string.IsNullOrWhiteSpace(name)) return;
        Einst.GruppenName = name.Trim();
        _ = GruppeAktualisieren();   // Selbst-Filter/Marker neu auswerten
    }

#if ANDROID
    // Mausrad (Emulator/Desktop): ACTION_SCROLL → Karte zoomen (statt blättern).
    // Mapsui (SkiaSharp/MAUI) verarbeitet ACTION_SCROLL nicht selbst, daher hängen
    // wir uns an die native Skia-Oberfläche (und alle Unteransichten) an.
    private readonly HashSet<Android.Views.View> _motionViews = new();

    private void WheelZoomEinrichten()
    {
        MapCtrl.Loaded += (_, _) => MotionAnhaengen();
        MapCtrl.HandlerChanged += (_, _) => MotionAnhaengen();
    }

    private void MotionAnhaengen()
    {
        if (MapCtrl.Handler?.PlatformView is Android.Views.View v)
        {
            Debug.WriteLine($"[Wheel] PlatformView = {v.GetType().FullName}");
            MotionRekursiv(v);
        }
    }

    private void MotionRekursiv(Android.Views.View v)
    {
        if (_motionViews.Add(v)) v.GenericMotion += AufGenericMotion;   // Mausrad-Zoom (Langdruck läuft über PointerGestureRecognizer)
        if (v is Android.Views.ViewGroup vg)
            for (int i = 0; i < vg.ChildCount; i++)
            {
                var c = vg.GetChildAt(i);
                if (c != null) MotionRekursiv(c);
            }
    }

    private void AufGenericMotion(object? sender, Android.Views.View.GenericMotionEventArgs e)
    {
        var ev = e.Event;
        if (ev == null || ev.Action != Android.Views.MotionEventActions.Scroll) return;
        float vs = ev.GetAxisValue(Android.Views.Axis.Vscroll);
        if (vs == 0) return;
        // Zeiger-Position in Mapsui-Screen-Koordinaten (geräteunabhängige Pixel).
        double dichte = DeviceDisplay.Current.MainDisplayInfo.Density;
        if (dichte <= 0) dichte = 1;
        var zentrum = new MPoint(ev.GetX() / dichte, ev.GetY() / dichte);
        // SOFORT zoomen (duration 0, keine Animation) – MouseWheelZoom animiert jeden Tick,
        // was am Emulator träge wirkt. Ein voller Zoom-Schritt je Rad-Tick, direkt.
        var nav = _map.Navigator;
        double neu = nav.Viewport.Resolution * (vs > 0 ? 0.5 : 2.0);   // rein = kleinere Auflösung
        nav.ZoomTo(neu, zentrum, 0L, null);
        e.Handled = true;
    }
#endif
}
