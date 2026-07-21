using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace WalkHop;

/// <summary>App-WEITER Dauer-GPS-Empfang: startet mit der App und läuft, solange die App an ist –
/// unabhängig von der aktuell gezeigten Seite. Jeder Fix wird persistent über <see cref="GpsSpeicher"/>
/// gespeichert.
///
/// iOS: nativer <see cref="HintergrundStandort"/> (CLLocationManager, läuft auch im Hintergrund).
/// Android/Windows: fortlaufender <c>Geolocation</c>-Loop (solange der Prozess lebt; echtes
/// Android-Hintergrund-Tracking bei geschlossener App bräuchte einen Foreground-Service – separat).</summary>
internal static class DauerGps
{
    private static bool _laeuft;

    internal static void Starten()
    {
        if (_laeuft) return;
        _laeuft = true;
#if IOS
        HintergrundStandort.Starten();
#else
        _ = Schleife();
#endif
    }

#if !IOS
    private static async Task Schleife()
    {
        try
        {
            var s = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (s != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        while (_laeuft)
        {
            try
            {
                var loc = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
                if (loc != null) GpsSpeicher.Speichere(loc.Latitude, loc.Longitude, loc.Accuracy ?? 0, loc.Timestamp);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            await Task.Delay(1000);
        }
    }
#endif
}
