using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace WalkHop;

/// <summary>Nutzungs-Heartbeat: misst die aktive Vordergrund-Zeit der App und meldet
/// sie periodisch an den Server (<see cref="Auth.PingAsync"/>). Das erfüllt zweierlei:
/// (1) Durchsetzung des Tages-Zeit-Budgets der Stufe (Demo z. B. 20 Min/Tag) und
/// (2) Server-Tracking der tatsächlichen Nutzung.
///
/// Läuft NUR im Vordergrund: <see cref="Starten"/> beim App-Start/Wiederkehren,
/// <see cref="Stoppen"/> beim Wechsel in den Hintergrund (App.xaml.cs-Lifecycle).</summary>
public static class Nutzung
{
    private const int IntervallS = 30;     // alle 30 s ein Heartbeat
    private static IDispatcherTimer? _timer;
    private static DateTime _letzte;
    private static bool _laeuft;

    public static void Starten()
    {
        if (_laeuft) return;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        _laeuft = true;
        _letzte = DateTime.UtcNow;
        _timer = disp.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(IntervallS);
        _timer.IsRepeating = true;
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    public static void Stoppen()
    {
        if (!_laeuft) return;
        _laeuft = false;
        try { _timer?.Stop(); } catch (Exception ex) { Debug.WriteLine(ex); }
        _timer = null;
        _ = TickAsync();   // die noch nicht gemeldete Restzeit vor dem Hintergrund senden
    }

    // Vergangene aktive Sekunden seit dem letzten Tick an den Server melden.
    private static async Task TickAsync()
    {
        var jetzt = DateTime.UtcNow;
        int sek = (int)Math.Round((jetzt - _letzte).TotalSeconds);
        _letzte = jetzt;
        if (sek <= 0) return;
        try { await Auth.PingAsync(sek); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }
}
