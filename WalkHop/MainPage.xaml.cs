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
    private const double RerouteSchwelleMeter = 50;  // Abstand zur Route, ab dem neu geroutet wird (off-route)
    private const int OfflineExtraZoom = 2;     // beim Offline-Laden so viele Stufen tiefer
    private const int OfflineMaxKacheln = 300;  // Obergrenze pro Offline-Download
    private const int AppKontoOfflineP5 = 3;    // Abo 5 €: 3 Offline-Karten inklusive (vgl. AppKonto.OFFLINE_INKLUSIVE_P5)
    // Richtungspfeil (maps.me-/Web-Konzept): Schaft in Routenfarbe ab der Position nach vorne auf
    // der Route. Grund-Länge; steht eine Abbiegung in Reichweite, wächst er bis dahin.
    private const double VorausMin = 90;        // Grund-Länge des Richtungspfeils (m)
    private const double VorausAbbieg = 150;    // Abbiegung <= so nah → Pfeil bis dahin zeigen
    private const double VorausUeber = 20;      // …und ein Stück darüber hinaus
    // Farbe der GPS-Route (blau). Der Richtungspfeil (Schaft + Spitze) nutzt EXAKT dieselbe Farbe,
    // damit der Abbiege-Pfeil wie die Route wirkt (früher lila – auf Wunsch angeglichen).
    private const string RouteFarbeHex = "#1d6fe0";

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
    private MemoryLayer _richtungLayer = null!;   // Richtungspfeil (Schaft + Spitze, Routenfarbe) voraus auf der Route
    private MemoryLayer _breadcrumbLayer = null!; // dezente „Brotkrumen"-Spur des zurückgelegten Weges (unter der Route)
    private readonly List<(double lat, double lon)> _breadcrumb = new();   // gesammelte GPS-Spur (nur HEUTE)
    private DateTime _breadcrumbTag = DateTime.MinValue;   // Tag der aktuellen Spur → bei Tageswechsel zurücksetzen
    private readonly object _breadcrumbLock = new();
    // Brotkrumen-Spur deckeln: bei ~1 Fix/s reichen ein paar Tausend Punkte locker; verhindert
    // unbegrenztes Wachstum (Speicher) und teures Kopieren/Zeichnen der gesamten Liste.
    private const int MaxBreadcrumb = 5000;
    // Breadcrumb-Neuaufbau ist O(n) (ganze Linie neu). Bei schnellem Punkt-Zufluss (Rad/Auto) höchstens
    // alle 2,5 s neu zeichnen – die verzögerte Spur HINTER dem Nutzer ist visuell nicht wahrnehmbar.
    private long _letztBreadcrumbMs = -100000;
    private const long BreadcrumbRedrawMs = 2500;
    private IDispatcherTimer? _breadcrumbTimer;   // zeichnet ein gedrosselt übersprungenes Segment verzögert nach
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
    private (double lat, double lon)? _navStartGeo;            // Position, aus der die Vorschau-Route berechnet wurde (nur bei GPS-Start)
    private long _letztVorschauNeuMs = -100000;               // Drosselung der Vorschau-Neuberechnung bei Standort-Korrektur
    private double _heading;
    private bool _kompassHatWert;     // erst nach der ersten Kompass-Lesung true (sonst _heading == 0)
    private double _gpsKurs;          // Fahrtrichtung aus GPS (Fallback ohne Kompass)
    private (double lat, double lon)? _letzteKursGeo;   // letzte Position für Kursberechnung aus Bewegung
    private double _letztEntlang;     // zuletzt projizierte Distanz entlang der Route (für Zoom-Redraw)
    private double _pfeilEntlangGezeichnet = -1;   // entlang-Wert des zuletzt GEZEICHNETEN Richtungspfeils (Redraw-Drosselung)
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
    private bool _kompassMoeglich = true;   // false, sobald das Gerät nachweislich keinen Kompass-Sensor hat (kein weiteres Start-Versuchen)
    private bool _positionsSchleifeLaeuft;   // genau eine Live-Positions-Schleife (umgeht den 50-m-Distanzfilter)
    private long _letztGpsLogMs = -100000;   // Drosselung der GPS-Diagnose ins Protokoll
    private volatile bool _seiteLebt;        // Seite sichtbar (OnAppearing…OnDisappearing): Schutz für fire-and-forget-UI-Zugriffe

    // Live-Navigation
    private List<(double lat, double lon)>? _navPunkte;
    private double[]? _navKum;
    private List<Manoever> _navManoever = new();
    private int[] _navManoeverBegin = Array.Empty<int>();   // BeginIndex je Manöver (1× vorberechnet, kein LINQ je GPS-Takt)
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
        // Richtungspfeil (Routenfarbe): liegt ÜBER der (blauen) Route, aber UNTER dem Positionsmarker.
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
        _seiteLebt = true;
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
            catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Standortberechtigung prüfen", ex); }
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_navAktiv) Meldung.Notiz("NAV", "Navigationsseite verlassen (OnDisappearing) trotz aktiver Navigation");
        _seiteLebt = false;  // ab hier dürfen fire-and-forget-Fortsetzungen die UI nicht mehr berühren
        SensorenStoppen();   // Lifecycle-Gegenstück zu OnAppearing: Akku/Leaks vermeiden
        GruppeStop();        // Gruppen-Polling pausieren (Code bleibt erhalten)
    }

    /// <summary>Beendet eine laufende Navigation + stoppt Sensoren (für Logout).</summary>
    public static void AktiveSitzungBeenden()
    {
        if (_aktuell == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _aktuell.NavigationBeenden("Sitzung beendet (Logout)");
            _aktuell.SensorenStoppen();
        });
    }
}
