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
    // Zoom-Glättung: nur bei echter Auflösungs-(Zoom-)Änderung die schweren Vektor-Layer ausblenden;
    // Pan/Zentrieren (GPS-Folgen ändert die Auflösung NICHT) bleibt unberührt.
    private void BeiViewportAenderung()
    {
        double res = _map.Navigator.Viewport.Resolution;
        if (!KarteHelfer.ZoomWesentlich(res, _letzteZoomRes)) return;
        _letzteZoomRes = res;
        if (!_vektorenVerborgen)
        {
            _vektorenVerborgen = true;
            _tourLayer.Enabled = false;   // der laufende Zoom zeichnet ohnehin neu – kein extra RefreshGraphics nötig
            _fotoLayer.Enabled = false;
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
        _tourLayer.Enabled = true;
        _fotoLayer.Enabled = _fotoAn;
        _map.RefreshGraphics();
    }

    // ---- Karten-Tipp → nächste Tour öffnen -----------------------------------
    // Karten-Tipp → Kontextmenü: Navigation zu / GPS-Position / Marker setzen.
    private async void OnKarteTipp(object? sender, MapInfoEventArgs e)
    {
        // Welt-Position des Tipps holen; falls Mapsui (z. B. direkt nach Layer-/Zoom-Änderungen)
        // ausnahmsweise keine WorldPosition liefert, ersatzweise aus der Screen-Position rechnen,
        // damit ein Tipp auf die freie Karte zuverlässig das Kontextmenü auslöst.
        var wp = e.MapInfo?.WorldPosition;
        if (wp == null && e.MapInfo?.ScreenPosition is { } sp)
            wp = _map.Navigator.Viewport.ScreenToWorld(sp.X, sp.Y);
        if (wp == null) return;
        if (Environment.TickCount64 - _letztLangdruckMs < 700) return;   // Langdruck hat das Menü gerade gezeigt
        var (lon, lat) = SphericalMercator.ToLonLat(wp.X, wp.Y);
        // Tipp genau auf einen Foto-Marker → das Foto zeigen.
        if (NaechstesFoto(lat, lon) is { } foto) { await FotoBetrachten(foto); return; }
        // Tipp genau auf eine SELBST ERRECHNETE Rundwanderung (liegt obenauf) → DEREN Detail-Fenster
        // (vor den Standard-Touren prüfen, sonst öffnete sich fälschlich eine andere, nicht sichtbare Route).
        if (NaechsteGenWanderung(lat, lon) is { } gw) { DialogZeigen(GenZuTour(gw)); return; }
        // Tipp genau auf eine GPS-Route → Tour-Detail-Fenster (Karte, Daten, Sehenswürdigkeiten).
        if (NaechsteTourRoute(lat, lon) is { } tour) { DialogZeigen(tour); return; }
        await KontextmenueZeigen(lat, lon);
    }

    // Kontextmenü (Punkt-Aktionen) – per kurzem Tipp UND Langdruck. Ein Tipp genau AUF eine
    // GPS-Route öffnet hingegen das Tour-Detail-Fenster (siehe OnKarteTipp/LongTap), nicht dieses Menü.
    private async Task KontextmenueZeigen(double lat, double lon)
    {
        string navZu = L.T("ktx_navigation_zu"), neueW = L.T("ktx_neue_wanderung"), markerOpt = L.T("ktx_marker_setzen");
        var optionen = new List<string> { navZu, neueW, markerOpt };
        optionen.Add(Standort.EntfernungZeile(lat, lon, _letzteGeo));   // Info-Zeile vor „Abbrechen"
        string wahl = await DisplayActionSheet(null, L.T("abbrechen"), null, optionen.ToArray());
        if (wahl == navZu)
        {
            MainPage.GeplantesZiel = (lat, lon);
            await Shell.Current.GoToAsync("//navigation");
        }
        else if (wahl == neueW) await NeueWanderung(lat, lon);
        else if (wahl == markerOpt)
        {
            string name = await DisplayPromptAsync(L.T("marker_titel"), L.T("marker_msg"),
                                                   L.T("marker_setzen_btn"), L.T("abbrechen"), L.T("marker_placeholder"));
            if (name == null) return;   // abgebrochen
            MarkerSetzen(lat, lon, string.IsNullOrWhiteSpace(name) ? L.T("marker_default") : name.Trim());
        }
    }

    // Nächstgelegene angezeigte Tour-Route zum Tipp-Punkt (null, wenn keine in Reichweite).
    // Abstand zum LINIENSEGMENT (nicht nur zu Stützpunkten) und Toleranz abhängig vom Zoom
    // (~22 px Tap-Radius), damit man die Linie auch herausgezoomt zuverlässig trifft.
    // Trefferradius REIN pixelbasiert (~10 px) – nur direkt AUF der Route, nicht „in der Nähe".
    // Bei jedem Zoom gleich; weit entfernte Routen lösen nicht aus. Logik gemeinsam mit der
    // Navigations-Karte in KarteHelfer.NaechsteRoute (durchsucht hier die gefilterten Touren).
    private TourInfo? NaechsteTourRoute(double lat, double lon)
        => KarteHelfer.NaechsteRoute(_gefiltert, lat, lon, _map.Navigator.Viewport.Resolution, 10);

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
        StatusKurz(L.T("marker_gesetzt", name), 4);   // blendet sich nach 4 s aus
    }

    private async void OnStandort(object? sender, EventArgs e)
    {
        try
        {
            var st = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (st != PermissionStatus.Granted) st = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            var loc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));
            if (loc == null) { Status(L.T("ue_st_kein_standort")); return; }
            var (x, y) = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), Aufloesung(12));
            _zentrum = (loc.Latitude, loc.Longitude);
            if (_umkreis) Anwenden();
        }
        catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("ue_st_standort_nicht_verfuegbar")); }
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
                    // Medium (Fused-/Netzwerk-fähig) statt Best (GPS-only) → auch drinnen schnell ein Fix.
                    var loc = await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
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
        GruppeLive.Sende(loc.Latitude, loc.Longitude);   // eigene Position in die Gruppe (gedrosselt, wenn aktiv)
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
            else if (_folgen && KameraFrei) _map.Navigator.CenterOn(_letztePos);
            // Kompass-Modus OHNE Kompass-Hardware: Karte in GPS-Fahrtrichtung drehen (greift nur bei Bewegung).
            if (_fahrtrichtung && !_kompassHatWert && KameraFrei) _map.Navigator.RotateTo(-_gpsKurs);
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
            if (KarteHelfer.Winkeldifferenz(_heading, _gezeichnetHeading) > 3)
            { _gezeichnetHeading = _heading; PositionZeichnen(); }
            // RotateTo NUR bei spürbarer Kurs-Änderung (>1,5°) – sonst dreht jeder Sensor-Tick (~16 Hz)
            // die Karte und erzwingt einen Voll-Redraw (Akku/Jitter). Nicht während Touch.
            if (_fahrtrichtung && KameraFrei
                && KarteHelfer.Winkeldifferenz(_heading, _gedrehtHeading) > 1.5)
            { _gedrehtHeading = _heading; _map.Navigator.RotateTo(-_heading); }
        });
    }

    // Standort-Beam + Zeichenlogik gemeinsam mit der Navigations-Karte in KarteHelfer.
    private void PositionZeichnen()
    {
        var pos = (_letztePos != null && _letzteGeo != null) ? _letztePos : null;
        double kursGrad = _kompassHatWert ? _heading : _gpsKurs;
        KarteHelfer.PositionBeamZeichnen(_posLayer, pos, kursGrad);
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
            Status(L.T("st_warte_gps"));
        }
        // Norden-Modus: Karten-Drehung sperren (dreht sich nicht). Kompass-Modus: entsperrt (App dreht).
        try { _map.Navigator.RotationLock = !_fahrtrichtung; } catch (Exception ex) { Debug.WriteLine(ex); }
        KompassIconAktualisieren();
        // Ohne Kompass-Hardware (z. B. Doogee N55) kann die Karte im Stand nicht zur Blickrichtung drehen.
        if (_fahrtrichtung)
        {
            bool kompass = false;
            try { kompass = Compass.Default.IsSupported; } catch (Exception ex) { Debug.WriteLine(ex); }
            if (!kompass) StatusKurz(L.T("ue_kein_kompass"), 6);
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
}
