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

public partial class MainPage
{
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

    // ---- Tour-Routen-Overlay (antippbar zum „Abwandern") ----
    private async void TourenLaden()
    {
        if (_tourenGezeichnet) return;
        _tourenGezeichnet = true;
        try { _touren = await TourService.LadeTourenAsync(); }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Touren laden", ex); _tourenGezeichnet = false; return; }
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
            f.Styles.Add(new VectorStyle { Line = new Pen(KarteHelfer.Farbe(t.Farbe), 3) { PenStyle = PenStyle.Solid } });
            features.Add(f);
        }
        return features;
    }

    // Nächstgelegene angezeigte Tour-Route zum Punkt (null, wenn keine in ~10 px Reichweite).
    // Trefferlogik gemeinsam mit der Übersichtskarte in KarteHelfer.NaechsteRoute.
    private TourInfo? NaechsteTourRoute(double lat, double lon)
    {
        if (_touren.Count == 0) return null;
        return KarteHelfer.NaechsteRoute(_touren, lat, lon, _map.Navigator.Viewport.Resolution, 10);
    }

    // ---- Karten-Tipp → Kontextmenü (wie Web: „Hierhin navigieren") ----------
    private async void AufKarteTipp(object? sender, MapInfoEventArgs e)
    {
        var wp = e.MapInfo?.WorldPosition;
        if (wp == null) return;
        if (Environment.TickCount64 - _letztLangdruckMs < 700) return;   // Langdruck hat das Menü gerade gezeigt
        var (lon, lat) = ZuGeo(wp.X, wp.Y);
        // In der Vorschau mit mehreren Routenvorschlägen: Tipp auf eine Linie wählt diesen Vorschlag
        // (statt das Kontextmenü zu öffnen) – „durch Klick auf einen Vorschlag kann ich ihn navigieren".
        if (!_navAktiv && _vorschlaege.Count > 1)
        {
            int idx = NaechsterVorschlag(lat, lon);
            if (idx >= 0) { if (idx != _vorschlagWahl) VorschlagWaehlen(idx, fit: false); return; }
        }
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
        for (int i = 0; i < pts.Count; i++)
        {
            var (x, y) = ZuMercator(pts[i].lat, pts[i].lon);
            coords[i] = new Coordinate(x, y);
        }
        // maps.me-Stil: weiße Kontur (Casing) + kräftige blaue Route darüber.
        var casing = new GeometryFeature { Geometry = new LineString(coords) };
        casing.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#ffffff"), 11) });
        features.Add(casing);
        var feature = new GeometryFeature { Geometry = new LineString(coords) };
        feature.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString(RouteFarbeHex), 7) });
        features.Add(feature);
        _routeLayer.Features = features;
        _routeLayer.DataHasChanged();
        if (fitKamera && pts.Count > 1) KameraAufPunkte(pts);   // gemeinsame Kamera-Fit-Logik (auch für Vorschläge)
    }

    // ---- Richtungspfeil (Schaft + Spitze in Routenfarbe) – Port aus navi_route.js/navi.js ----
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

    // Richtungspfeil mitführen: Schaft in Routenfarbe (Linie + weißes Casing) entlang der Route
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
        // Schaft: weißes Casing + Linie in Routenfarbe (blau, wie die GPS-Route)
        var schaftCasing = new GeometryFeature { Geometry = new LineString(coords) };
        schaftCasing.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString("#ffffff"), 11) });
        feats.Add(schaftCasing);
        var schaft = new GeometryFeature { Geometry = new LineString(coords) };
        schaft.Styles.Add(new VectorStyle { Line = new Pen(Mapsui.Styles.Color.FromString(RouteFarbeHex), 7) });
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
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString(RouteFarbeHex)),
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
        // Nur bei echtem ZOOM (Auflösungsänderung) eingreifen – Pan/Zentrieren (GPS-Folgen) ignorieren.
        if (!KarteHelfer.ZoomWesentlich(res, _letzteZoomRes)) return;
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
        BreadcrumbZeichnen();   // Anzeige/Farbe der gewanderten Route (Einstellungen) neu anwenden
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
