using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace WalkHop;

/// <summary>MAUI-Anzeige für <see cref="Meldung"/>: zeigt Fehler als Dialog auf der aktuellen Seite,
/// mit „Diesen Fehler ignorieren"-Knopf (dauerhaft je Kontext+Exception-Typ, in Preferences gespeichert)
/// und Schutz gegen Dialog-Flut (gleiche Fehlerart max. alle 30 s, keine gestapelten Dialoge).
/// Registriert sich per <see cref="ModuleInitializer"/> selbst – kein Eingriff in App/MauiProgram nötig.
/// Diese Datei ist NICHT im Unit-Test-Projekt eingebunden, daher bleibt <see cref="Meldung"/> MAUI-frei.</summary>
internal static class MeldungAnzeige
{
    private const long ThrottleMs = 30000;   // gleiche Fehlerart höchstens alle 30 s anzeigen
    private static readonly object _sperre = new();
    private static readonly Dictionary<string, long> _zuletzt = new();   // Schlüssel → letzte Anzeige (TickCount64)
    private static bool _zeigtGerade;        // verhindert gestapelte Dialoge
    private static HashSet<string>? _ignoriert;

    private static HashSet<string> Ignoriert => _ignoriert ??= new HashSet<string>(
        (Preferences.Get("fehler_ignoriert", "") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries));

    [ModuleInitializer]
    internal static void Registrieren()
    {
        Meldung.Anzeiger = Zeige;
        Meldung.ResetIgnoriert = Reset;
        Meldung.AnzahlIgnoriert = () => Ignoriert.Count;
    }

    private static void Reset()
    {
        _ignoriert = new HashSet<string>();
        Preferences.Remove("fehler_ignoriert");
    }

    private static void DauerhaftIgnorieren(string schluessel)
    {
        var set = Ignoriert;
        set.Add(schluessel);
        Preferences.Set("fehler_ignoriert", string.Join("\n", set));
    }

    private static void Zeige(string kontext, Exception? ex)
    {
        try
        {
            string typ = ex?.GetType().Name ?? "";
            string schluessel = kontext + "|" + typ;     // Identität: Kontext + Exception-Typ
            if (Ignoriert.Contains(schluessel)) return;  // dauerhaft ignoriert

            long now = Environment.TickCount64;
            lock (_sperre)
            {
                if (_zuletzt.TryGetValue(schluessel, out var t) && now - t < ThrottleMs) return;   // Throttle
                _zuletzt[schluessel] = now;
            }

            var app = Application.Current;
            if (app?.Dispatcher == null) return;
            app.Dispatcher.Dispatch(async () =>
            {
                if (_zeigtGerade) return;
                _zeigtGerade = true;
                try
                {
                    Page? seite = Shell.Current?.CurrentPage
                                  ?? (app.Windows.Count > 0 ? app.Windows[0].Page : null);
                    if (seite is Shell sh) seite = sh.CurrentPage ?? seite;
                    if (seite == null) return;
                    string detail = ex == null ? "" : $"\n\n{typ}: {ex.Message}";
                    bool ignorieren = await seite.DisplayAlert(
                        L.T("fehler_titel"), kontext + detail, L.T("fehler_ignorieren"), L.T("ok"));
                    if (ignorieren) DauerhaftIgnorieren(schluessel);
                }
                finally { _zeigtGerade = false; }
            });
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }
}
