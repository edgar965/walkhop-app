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

public partial class UebersichtPage : ContentPage
{
    private const double WELT0 = 156543.03392804097;
    private const int MaxRouten = 250;   // Obergrenze gezeichneter Routen (Performance)
    private const int ZentrierZoom = 14; // Zoomstufe beim Zentrieren auf den Live-Standort

    // Facetten-Chips: key = Filter-Facette, txtKey = Übersetzungs-Schlüssel (siehe Texte.cs).
    private static readonly (string key, string txtKey)[] FacettenChips =
    {
        ("", "fac_alle"), ("stadt", "fac_stadt"), ("see", "fac_see"),
        ("wandern_kurz", "fac_wandern_kurz"), ("wandern_mittel", "fac_wandern_mittel"), ("wandern_lang", "fac_wandern_lang"),
        ("wandern", "fac_wandern"), ("radtour", "fac_radtour"), ("rundtour", "fac_rundtour"),
        ("bahnanreise", "fac_bahnanreise"), ("qualitytour", "fac_qualitytour"), ("edgars", "fac_edgars"),
    };
    private static readonly int[] RadiusPresets = { 5, 15, 30, 60 };

    private Mapsui.Map _map = null!;
    private TileLayer _basisLayer = null!;
    private MemoryLayer _tourLayer = null!;
    private MemoryLayer _fotoLayer = null!;
    private MemoryLayer _kreisLayer = null!;
    private MemoryLayer _markerLayer = null!;
    private MemoryLayer _gruppeLayer = null!;        // Live-Marker der Gruppenmitglieder
    private MemoryLayer _genLayer = null!;          // generierte Rundwanderungen
    private List<GenWanderung> _genWanderungen = new();
    private string _genCosting = "pedestrian";       // Modus der zuletzt generierten Rundwanderungen (für Dialog/Navigation)
    private readonly Dictionary<string, Button> _chips = new();
    private readonly Dictionary<int, Button> _radiusBtns = new();

    // ---- Detail-Dialog: Mini-Karte mit der Tour-Route (wie im Web-Vorbild) ----
    private Mapsui.Map? _dlgMap;
    private MemoryLayer _dlgRouteLayer = null!;
    private MRect? _dlgBox;          // letzte Routen-Bounding-Box (für verzögertes Nachzoomen)

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
    private double _gedrehtHeading = -999;   // zuletzt gedrehte Kartenrichtung (RotateTo-Drosselung)
    private (double lat, double lon)? _letzteKursGeo;   // letzte Position für die Kursberechnung aus Bewegung
    private bool _kompassHatWert;
    private bool _folgen, _fahrtrichtung;
    private bool _zentrierenNaechsterFix = true;   // erster GPS-Fix → auf Position zentrieren (Norden oben)
    private bool _gpsLaeuft, _kompassLaeuft, _positionsSchleifeLaeuft;
    private bool _kompassMoeglich = true;   // false, sobald das Gerät nachweislich keinen Kompass-Sensor hat
    private volatile bool _userBeruehrt;   // Finger berührt gerade die Karte → Kamera nicht bewegen
    private long _letzteBeruehrungMs;
    /// <summary>Kamera darf programmatisch bewegt werden (kein aktiver Touch + kurze Nachlauf-Sperre;
    /// Auto-Reset nach 4 s, falls ein TouchEnded verloren ging, damit das Folgen nicht hängen bleibt).</summary>
    private bool KameraFrei =>
        !_userBeruehrt && Environment.TickCount64 - _letzteBeruehrungMs > 350
        || Environment.TickCount64 - _letzteBeruehrungMs > 4000;
    private int _fotoPinBitmapId = -1;
    // Zoom-Glättung: schwere Tour-/Foto-Vektoren während der Zoom-Geste ausblenden (nur Kacheln
    // bleiben sichtbar → flüssig), nach kurzer Ruhe wieder einblenden. Verhindert das Ruckeln.
    private double _letzteZoomRes = -1;
    private bool _vektorenVerborgen;
    private IDispatcherTimer? _zoomTimer;
    private long _letztLangdruckMs;   // entkoppelt Langdruck-Menü vom evtl. folgenden Kurz-Tipp

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
        _genLayer = new MemoryLayer("GenWanderungen");   // generierte Rundwanderungen (über den Touren)
        _map.Layers.Add(_genLayer);
        _gruppeLayer = new MemoryLayer("GruppeLive") { Style = null };   // Live-Marker der Gruppenmitglieder
        _map.Layers.Add(_gruppeLayer);
        // Style = null: KEIN Layer-Default-Symbol (Mapsui zeichnet sonst einen grau/weißen Kreis
        // hinter jedes Punkt-Feature). Die Position bekommt ihren Style allein über das Feature.
        _posLayer = new MemoryLayer("Position") { Style = null };   // Live-Standort-Beam ganz oben
        _map.Layers.Add(_posLayer);
        var (bx, by) = SphericalMercator.FromLonLat(13.405, 52.52);
        _map.Home = n => n.CenterOnAndZoomTo(new MPoint(bx, by), Aufloesung(9));
        UeMap.Map = _map;
        // maps.me-Stil: beim 2-Finger-Zoom nicht mitdrehen (erst ab bewusster 30°-Drehung).
        UeMap.UnSnapRotationDegrees = 30;
        UeMap.ReSnapRotationDegrees = 8;
        // Mapsuis 200-ms-„Warte-auf-Doppeltipp" abschalten → Tipps reagieren sofort; Zoom per Pinch.
        UeMap.UseDoubleTap = false;
        // Solange der Finger die Karte berührt, KEINE programmatische Kamera-Bewegung (Folgen-
        // Zentrieren/Kompass-Drehen). Sonst kämpft die Live-Schleife gegen die Pinch-Geste → Zittern.
        UeMap.TouchStarted += (s, e) => { _userBeruehrt = true; _letzteBeruehrungMs = Environment.TickCount64; };
        UeMap.TouchEnded += (s, e) => { _userBeruehrt = false; _letzteBeruehrungMs = Environment.TickCount64; };
        _map.Info += OnKarteTipp;
        _map.Navigator.ViewportChanged += (s, e) => BeiViewportAenderung();
        // Langdruck → Kontextmenü sofort (Mapsuis eingebautes LongTap; e.ScreenPosition ist Mapsui-Screen).
        UeMap.LongTap += (s, e) =>
        {
            _letztLangdruckMs = Environment.TickCount64;
            var welt = _map.Navigator.Viewport.ScreenToWorld(e.ScreenPosition.X, e.ScreenPosition.Y);
            var (lon, lat) = SphericalMercator.ToLonLat(welt.X, welt.Y);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (NaechsteGenWanderung(lat, lon) is { } gw) { DialogZeigen(GenZuTour(gw)); return; }
                if (NaechsteTourRoute(lat, lon) is { } tour) { DialogZeigen(tour); return; }
                _ = KontextmenueZeigen(lat, lon);
            });
        };

        _fotoAn = Einst.FotosBeimStart;   // Foto-Ebene beim Start nur, wenn in Einstellungen → Allgemein aktiviert (Standard: aus)
        FotoKnopfAnzeigen();

        ChipsBauen();
        RadiusBauen();

        // Bei Laufzeit-Sprachwechsel die im Code erzeugten Chip-Beschriftungen neu setzen
        // (alle statischen XAML-Texte aktualisieren sich über {loc:Translate} selbst).
        L.Geaendert += () => MainThread.BeginInvokeOnMainThread(ChipsTexteSetzen);

#if IOS
        // iOS rendert das innere Suchfeld (UISearchTextField) im Dunkelmodus schwarz. Explizit hell
        // setzen (Minimal-Stil entfernt die dunkle Standard-Chrome), damit es wie auf Android weiß ist.
        Suchfeld.HandlerChanged += (_, _) =>
        {
            if (Suchfeld.Handler?.PlatformView is UIKit.UISearchBar sb)
            {
                sb.SearchBarStyle = UIKit.UISearchBarStyle.Minimal;
                sb.BackgroundColor = UIKit.UIColor.White;
                if (sb.SearchTextField is { } feld)
                {
                    feld.BackgroundColor = UIKit.UIColor.White;
                    feld.TextColor = UIKit.UIColor.Black;
                }
            }
        };
#endif
    }

    private static double Aufloesung(int zoom) => WELT0 / Math.Pow(2, zoom);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = SensorenStarten();   // Live-Standort-Beam + Kompass (jedes Mal, da OnDisappearing stoppt)
        KompassIconAktualisieren();
        KompakteSucheAnwenden();   // kompakten Suchmodus aus den Einstellungen übernehmen (beim Erscheinen)
        // Gruppen-Live: Marker/Knopf an die gemeinsame Komponente koppeln und Polling fortsetzen.
        GruppeLive.Mitglieder += GruppeMarkerZeichnen;
        GruppeLive.Geaendert += GruppeIconAktualisieren;
        GruppeIconAktualisieren();
        GruppeLive.Starten();
        if (_geladen) return;
        _geladen = true;
        Status(L.T("ue_st_touren_laden"));
        // Erst den ersten Frame (Karte + Bedienelemente) rendern lassen, dann im Hintergrund
        // laden. So blockieren Fetch/Parse/Filter nie den UI-Thread (kein Einfrieren/ANR).
        Dispatcher.Dispatch(() => _ = ErstLadenAsync());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SensorenStoppen();   // Akku/Leaks: Standort-Schleife + Kompass beim Verlassen stoppen
        GruppeLive.Mitglieder -= GruppeMarkerZeichnen;
        GruppeLive.Geaendert -= GruppeIconAktualisieren;
        GruppeLive.Pausieren();
        _zoomTimer?.Stop();   // Zoom-Glättungs-Timer beim Verlassen stoppen (Leak/Akku)
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
