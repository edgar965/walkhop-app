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
        _navManoeverBegin = manoever.Select(m => m.BeginIndex).ToArray();   // 1× vorberechnet für NaviLogik.NaechstesManoever
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
    private long _navStartMs;   // Start-/Reroute-Zeitpunkt → kurze Reroute-Schonfrist (s. AktualisiereNav)

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
        _navStartMs = Environment.TickCount64;   // Schonfrist: direkt nach Start/Reroute nicht sofort wieder umrouten
        _zentrierenNaechsterFix = false;   // beim Start die per fitKamera gefittete GANZE Route NICHT durch
                                           // Auto-Zoom auf die Position überschreiben (sonst nur ein Bruchteil sichtbar);
                                           // der Nutzer kann mit „Zentrieren" jederzeit auf seine Position springen.
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
        double rest = NaviLogik.Reststrecke(_navGesamt, entlang);
        RichtungPfeilAktualisieren(entlang);   // lila Richtungspfeil voraus mitführen

        // Auto-Reroute bei Abweichung von der Route (>50 m), gedrosselt. Schonfrist nach Start/Reroute
        // (10 s): verhindert den spurious Sofort-Reroute am (Rundtour-)Start, der die volle Tour durch
        // ein Fragment ersetzt + zurück in die „Start"-Vorschau springt, bevor der Nutzer auf der Route ist.
        if (NaviLogik.IstAbseitsRoute(abstand, RerouteSchwelleMeter) && !_reroutLaeuft
            && Environment.TickCount64 - _letztRerouteMs > 6000 && Environment.TickCount64 - _navStartMs > 10000)
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

        // nächstes Manöver = erstes, das DISTANZMÄSSIG noch vor uns liegt (Logik in NaviLogik):
        // Distanz statt reinem Index-Vergleich ist robust gegen GPS-Sprünge, die idx genau auf
        // einen Manöver-Stützpunkt setzen, und überspringt das Start-Manöver (Distanz 0).
        int next = NaviLogik.NaechstesManoever(_navManoeverBegin, _navKum, idx, entlang);

        if (next >= 0)
        {
            double distNext = NaviLogik.DistanzBisManoever(_navKum, _navManoever[next].BeginIndex, entlang);
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
                int ein = NaviLogik.NaechsterIndex(idx, _tourOriginal.Count);
                var rest = _tourOriginal.GetRange(ein, _tourOriginal.Count - ein);
                var r = await RouteService.RouteAsync(lat, lon, _tourOriginal[ein].lat, _tourOriginal[ein].lon,
                    Einst.Profil, opt, Einst.NaviLocale, folge: true);
                if (_seiteLebt && _navPunkte != null && r != null && r.Punkte.Count >= 2)   // Seite + Sitzung noch aktiv?
                {
                    var komb = new List<(double lat, double lon)>(r.Punkte);
                    komb.AddRange(rest);
                    await NavStartGetracet(komb, "", _ankunftText, fit: false);   // echte Manöver, ohne Kamera-Sprung
                    if (_seiteLebt) NaviAktivieren();   // Reroute läuft WÄHREND aktiver Navigation → aktiv bleiben (nicht zurück in die „Start"-Vorschau)
                    Status(L.T("st_route_neu"), autoAus: true);
                    if (Einst.Benachrichtigungstoene) NaviNotif.Signalton();   // Hinweis-Ton bei „Route neu"
                }
            }
            else if (_navZiel is { } z)
            {
                var (r, alt) = await RouteService.RouteVollAsync(lat, lon, z.lat, z.lon, Einst.Profil, opt, Einst.NaviLocale, 2, folge: true);
                if (_seiteLebt && _navPunkte != null && r != null && r.Punkte.Count >= 2)   // Seite + Sitzung noch aktiv?
                {
                    _alternativen = alt; _navMinuten = r.Minuten;
                    var ank = DateTime.Now.AddMinutes(r.Minuten).ToString("HH:mm");
                    NavStart(r.Punkte, r.Manoever, L.T("route_zusammenfassung", FmtKmVon(r.Km), r.Minuten), ank, fitKamera: false);
                    if (_seiteLebt) NaviAktivieren();   // Reroute läuft WÄHREND aktiver Navigation → aktiv bleiben (nicht zurück in die „Start"-Vorschau)
                    Status(L.T("st_route_neu"), autoAus: true);
                    if (Einst.Benachrichtigungstoene) NaviNotif.Signalton();   // Hinweis-Ton bei „Route neu"
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Route neu berechnen", ex); }
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
            if (!_seiteLebt || _navPunkte == null) return;   // Seite verlassen / Navigation zwischenzeitlich beendet
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
                HoeheInfo.Text = L.T("mp_hoehenprofil_werte", auf, ab);
                HoeheBlock.IsVisible = true;
            }
            else HoeheBlock.IsVisible = false;   // keine Höhendaten → Block ausblenden
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Höhenprofil laden", ex); }
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
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Sprachansage", ex); }
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
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Zur Startseite wechseln", ex); }
    }

    // „Zurück"-Knopf (nur in der Vorschau): zurück zur Start-/Übersichtsseite.
    private async void OnZurueck(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("//start"); }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Zur Startseite wechseln", ex); }
    }

    private void NavigationBeenden()
    {
        _navPunkte = null; _navKum = null; _navManoever = new(); _navManoeverBegin = Array.Empty<int>();
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
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Letztes Ziel speichern", ex); }
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
        int ein = NaviLogik.NaechsterIndex(idx, _navPunkte.Count);
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
}
