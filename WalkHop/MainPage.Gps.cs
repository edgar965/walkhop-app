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
            // Medium statt Best: Best erzwingt faktisch GPS-only (drinnen lange kein Fix). Medium nutzt den
            // Fused-/Netzwerk-Standort mit und liefert auch in Gebäuden zügig eine Position.
            await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(1)));
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
            Meldung.Notiz("GPS", letzte == null ? "LastKnown = NULL"
                : $"LastKnown {letzte.Latitude:F5},{letzte.Longitude:F5} acc={letzte.Accuracy:0}m alter={(DateTimeOffset.UtcNow - letzte.Timestamp).TotalMinutes:0.0}min");
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
        const int BackoffBasisMs = 1000;   // erste Wartezeit nach einem Fehlversuch
        const int BackoffMaxMs = 15000;    // gedeckelte Obergrenze (kein endloser Eng-Takt bei GPS-Ausfall)
        int fehler = 0;                    // Fehlerzähler fürs Backoff
        bool fehlerGemeldet = false;       // Nutzer-Feedback nur EINMAL je Fehlerserie (kein stiller Dauerfehler)
        try
        {
            while (_gpsLaeuft)
            {
                long start = Environment.TickCount64;
                bool ok = false;
                try
                {
                    // Medium (Fused-/Netzwerk-fähig) statt Best (GPS-only) → auch drinnen schnell ein Fix.
                    var loc = await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
                    GpsLog($"Live-Fix = {(loc == null ? "NULL (Timeout)" : $"{loc.Latitude:F5},{loc.Longitude:F5}")}");
                    if (loc != null) { VerarbeitePosition(loc); ok = true; }
                }
                catch (Exception ex) { GpsLog("Live-Fehler: " + ex.Message); }

                if (ok)
                {
                    fehler = 0; fehlerGemeldet = false;   // Erfolg → Backoff + Feedback zurücksetzen
                    // Schutz gegen Leerlauf-Spin, falls GetLocationAsync sofort (ohne echten Fix) zurückkäme:
                    long dauer = Environment.TickCount64 - start;
                    if (dauer < 200) await Task.Delay(200 - (int)dauer);
                }
                else
                {
                    // Fehler / kein Fix (Timeout): Intervall gedeckelt exponentiell vergrößern, damit wir bei
                    // dauerhaftem GPS-Ausfall nicht im engen Takt weiterpollen (Akku/Log-Spam). Bei Erfolg → reset.
                    fehler++;
                    if (!fehlerGemeldet)
                    {
                        fehlerGemeldet = true;   // dezenter, einmaliger Hinweis statt stillem Dauerfehler
                        Meldung.Notiz("GPS", "Live-Fix Timeout/Fehler – kein Standort erhalten (drinnen? Ortung aus?)");
                        MainThread.BeginInvokeOnMainThread(() =>
                        { if (_seiteLebt) Status(L.T("st_gps_nicht_verfuegbar"), autoAus: true); });
                    }
                    int warte = Math.Min(BackoffMaxMs, BackoffBasisMs * (1 << Math.Min(fehler - 1, 4)));
                    await Task.Delay(warte);
                }
            }
        }
        finally { _positionsSchleifeLaeuft = false; }
    }

    private void SensorenStoppen()
    {
        _gpsLaeuft = false;   // beendet die Live-Positions-Schleife
        _breadcrumbTimer?.Stop();   // ausstehende verzögerte Breadcrumb-Zeichnung abbrechen
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
        // VERALTETEN Fix verwerfen: ein alter Cache-Standort (z. B. GetLastKnownLocation von gestern/
        // einem anderen Ort) darf NICHT als aktuelle Position / Routenstart dienen – sonst startet die
        // Route „ganz woanders" und die gewanderte Route zieht eine lange Linie dorthin. Nur verwerfen,
        // wenn der Zeitstempel PLAUSIBEL alt ist (10 min…2 Tage); absurde/fehlende Zeitstempel (Epoch/
        // Zukunft) NICHT verwerfen, sonst würgt ein Gerät mit schlechtem Zeitstempel das GPS ab.
        // 10 min (statt 2): ein wenige Minuten alter, ortsrichtiger LastKnown-Fix soll die Position weiter
        // seeden (drinnen/schwacher Empfang) – nur klar veraltete Fixes (anderer Ort) werden verworfen.
        var alter = DateTimeOffset.UtcNow - loc.Timestamp;
        bool verwerfen = alter > TimeSpan.FromMinutes(10) && alter < TimeSpan.FromDays(2);
        if (Environment.TickCount64 - _letztGpsLogMs > 3000)   // Diagnose ins Protokoll (gedrosselt)
        {
            _letztGpsLogMs = Environment.TickCount64;
            Meldung.Notiz("GPS", $"Fix {loc.Latitude:F5},{loc.Longitude:F5} acc={loc.Accuracy:0}m alter={alter.TotalMinutes:0.0}min → {(verwerfen ? "VERWORFEN" : "ok")}");
        }
        if (verwerfen) { GpsLog($"Fix verworfen (veraltet {alter.TotalMinutes:0} min)"); return; }
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
            // „Gewanderte Route" nur AB HEUTE: bei Tageswechsel die alte Spur verwerfen.
            if (_breadcrumbTag != DateTime.Today) { _breadcrumb.Clear(); _breadcrumbTag = DateTime.Today; }
            double sprung = _breadcrumb.Count == 0 ? 0
                : NavGeo.Haversine(_breadcrumb[^1].lat, _breadcrumb[^1].lon, loc.Latitude, loc.Longitude);
            // Riesensprung (> 2 km) zwischen zwei Fixes = Lücke/Glitch (App pausiert, woanders fortgesetzt):
            // NICHT als Linie verbinden – die Spur ab hier frisch beginnen (keine Linie quer über die Karte).
            if (sprung > 2000) _breadcrumb.Clear();
            if (_breadcrumb.Count == 0 || sprung > 8)
            {
                _breadcrumb.Add((loc.Latitude, loc.Longitude));
                // Deckel: bei Überlauf den ältesten Block in EINEM Rutsch verwerfen (amortisiert O(1)
                // statt teurem Einzel-Shift je Punkt) – auf ~90 % der Obergrenze zurückstutzen.
                if (_breadcrumb.Count > MaxBreadcrumb)
                    _breadcrumb.RemoveRange(0, _breadcrumb.Count - MaxBreadcrumb * 9 / 10);
                breadcrumbNeu = true;
            }
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_seiteLebt) return;   // Seite verlassen → keine Karten-/UI-Zugriffe mehr (fire-and-forget-Schutz)
            PositionZeichnen();
            // Breadcrumb-Neuaufbau ist O(n) → nur gedrosselt (≤ alle 2,5 s) neu zeichnen; die Punkte
            // selbst werden weiter sofort gesammelt (oben unter _breadcrumbLock), nur das Zeichnen wartet.
            if (breadcrumbNeu)
            {
                if (Environment.TickCount64 - _letztBreadcrumbMs > BreadcrumbRedrawMs)
                { _letztBreadcrumbMs = Environment.TickCount64; _breadcrumbTimer?.Stop(); BreadcrumbZeichnen(); }
                else BreadcrumbNachziehen();   // gedrosselt übersprungen → verzögert nachzeichnen (letztes Segment nicht verlieren)
            }
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
            if (KarteHelfer.Winkeldifferenz(_heading, _gezeichnetHeading) > 3)
            { _gezeichnetHeading = _heading; PositionZeichnen(); }
            // RotateTo NUR bei spürbarer Kurs-Änderung (>1,5°): sonst dreht jeder Sensor-Tick (~16 Hz) die
            // Karte und löst einen Voll-Redraw aus (Akku/Jitter). Nicht während Touch / nicht in der Vorschau.
            if (_fahrtrichtung && KameraFrei && !(_navPunkte != null && !_navAktiv)
                && KarteHelfer.Winkeldifferenz(_heading, _gedrehtHeading) > 1.5)
            { _gedrehtHeading = _heading; _map.Navigator.RotateTo(-_heading); }
        });
    }

    // Standortanzeige im Google-Maps-Stil: Standortpunkt + glatter Blickrichtungs-Beam (gerenderte
    // Bitmap mit Alpha-Verlauf). Bildschirm-konstant (kein Zoom-Redraw), dreht mit Kompass/GPS-Kurs.
    // Beam-Grafik + Zeichenlogik liegen gemeinsam mit der Übersichtskarte in KarteHelfer.
    private void PositionZeichnen()
    {
        var pos = (_letztePos != null && _letzteGeo != null) ? _letztePos : null;
        double kursGrad = _kompassHatWert ? _heading : _gpsKurs;   // Blickrichtung in Grad
        KarteHelfer.PositionBeamZeichnen(_positionLayer, pos, kursGrad);
    }

    // „Gewanderte Route": die gesammelte GPS-Spur des zurückgelegten Weges als Linie zeichnen
    // (Mercator-Geometrie wie die Route, UNTER der aktiven Route). Anzeige + Farbe sind in den
    // Einstellungen (Navigation) steuerbar – aus → Ebene leeren.
    private void BreadcrumbZeichnen()
    {
        if (!Einst.GewanderteAnzeigen)   // Anzeige der gewanderten Route ausgeschaltet
        { BreadcrumbLeeren(); return; }
        List<(double lat, double lon)> pts;
        lock (_breadcrumbLock) pts = new List<(double lat, double lon)>(_breadcrumb);
        if (pts.Count < 2) { BreadcrumbLeeren(); return; }
        var coords = new Coordinate[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        { var (x, y) = ZuMercator(pts[i].lat, pts[i].lon); coords[i] = new Coordinate(x, y); }
        var f = new GeometryFeature { Geometry = new LineString(coords) };
        // Farbe aus den Einstellungen (Default Grau), kräftig sichtbar – liegt unter der aktiven Route.
        f.Styles.Add(new VectorStyle { Line = new Pen(KarteHelfer.Farbe(Einst.GewanderteFarbe), 5) });
        _breadcrumbLayer.Features = new List<IFeature> { f };
        _breadcrumbLayer.DataHasChanged();
    }

    private void BreadcrumbLeeren()
    {
        if (_breadcrumbLayer.Features.Any())
        { _breadcrumbLayer.Features = new List<IFeature>(); _breadcrumbLayer.DataHasChanged(); }
    }

    // Ein gedrosselt übersprungenes Segment einmal verzögert nachzeichnen – so erscheint das letzte Stück
    // der gewanderten Route auch, wenn der Nutzer stehen bleibt oder die Seite kurz nach dem Punkt verlässt.
    private void BreadcrumbNachziehen()
    {
        if (_breadcrumbTimer == null)
        {
            _breadcrumbTimer = Dispatcher.CreateTimer();
            _breadcrumbTimer.Interval = TimeSpan.FromMilliseconds(BreadcrumbRedrawMs + 200);
            _breadcrumbTimer.IsRepeating = false;
            _breadcrumbTimer.Tick += (_, _) =>
            { if (_seiteLebt) { _letztBreadcrumbMs = Environment.TickCount64; BreadcrumbZeichnen(); } };
        }
        _breadcrumbTimer.Stop();
        _breadcrumbTimer.Start();
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
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Aufnahme speichern", ex); }
    }

    /// <summary>Statischer Einsprung fürs App-Lebenszyklus: laufende Aufnahme sichern (vor Upload).</summary>
    public static void AktiveAufnahmeSichern() => _aktuell?.AufnahmeFinalisieren();
}
