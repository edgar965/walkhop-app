using CoreLocation;

namespace WalkHop;

/// <summary>App-WEITER Dauer-Standort auf iOS: ein <see cref="CLLocationManager"/>, der ab App-Start bis
/// App-Ende läuft – auch im HINTERGRUND (Bildschirm gesperrt / Handy in der Tasche). Jeder vom System
/// gelieferte Fix wird sofort persistent über <see cref="GpsSpeicher"/> gespeichert.
///
/// Hintergrund: MAUI Essentials' <c>Geolocation</c> setzt auf iOS <c>AllowsBackgroundLocationUpdates</c>
/// NICHT. Ohne das (und ohne <c>UIBackgroundModes=location</c> in der Info.plist) suspendiert iOS die App
/// im Hintergrund → das GPS steht (im Log: lange Fix-Lücken, „alter" steigt) und iOS beendet die App später
/// → laufende Navigation „bricht ab". Dieser eigene Manager verhindert das und liefert die geforderten
/// GPS-Daten „so lange die App an ist". „When In Use" reicht, sofern in-foreground gestartet (blaue
/// Statusleiste = Pflicht/Transparenz).</summary>
internal static class HintergrundStandort
{
    private static CLLocationManager? _mgr;
    private static bool _laeuft;

    internal static void Starten()
    {
        try
        {
            if (_mgr == null)
            {
                _mgr = new CLLocationManager
                {
                    DesiredAccuracy = CLLocation.AccuracyBest,
                    ActivityType = CLActivityType.Fitness,            // Wandern/Fitness → passendes iOS-Energieprofil
                    PausesLocationUpdatesAutomatically = false,       // iOS soll die Updates NICHT selbst pausieren
                    AllowsBackgroundLocationUpdates = true,           // der eigentliche Schalter fürs Weiterlaufen im Hintergrund
                };
                try { _mgr.ShowsBackgroundLocationIndicator = true; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                // Jeder gelieferte Fix → sofort persistent speichern (läuft im Vorder- UND Hintergrund).
                _mgr.LocationsUpdated += (_, e) =>
                {
                    foreach (var l in e.Locations)
                        GpsSpeicher.Speichere(l.Coordinate.Latitude, l.Coordinate.Longitude,
                            l.HorizontalAccuracy, DateTimeOffset.UtcNow);
                };
            }
            _mgr.RequestWhenInUseAuthorization();   // no-op wenn bereits erteilt (MAUI fragt in OnAppearing schon)
            _mgr.StartUpdatingLocation();
            if (!_laeuft) { _laeuft = true; Meldung.Notiz("HGRUND", "Dauer-GPS aktiv (app-weit, Hintergrund erlaubt)"); }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Meldung.Fehler("Dauer-Standort starten", ex); }
    }

    internal static void Stoppen()
    {
        if (!_laeuft) return;
        try { _mgr?.StopUpdatingLocation(); _laeuft = false; Meldung.Notiz("HGRUND", "Dauer-GPS gestoppt"); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
