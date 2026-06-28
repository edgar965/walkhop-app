using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace WalkHop;

/// <summary>Gemeinsamer Handler für Custom-Scheme-Deep-Links <c>walkhop://g/&lt;code&gt;</c>.
/// Die Web-/WhatsApp-Seite verlinkt auf <c>walkhop://g/{code}</c> („In der WalkHop-App öffnen").
/// Beide Plattformen (Android <c>MainActivity</c>, iOS <c>AppDelegate</c>) reichen die rohe URL an
/// <see cref="Behandeln"/> weiter. Der Code wird geparst, gesäubert, der Gruppe wird beigetreten
/// und zur Übersichtskarte (<c>//start</c>) navigiert.
///
/// Cold-Start (App durch den Link gestartet): die Shell ist evtl. noch nicht bereit. Dann wird der
/// Code in <see cref="_ausstehenderCode"/> zwischengespeichert; <see cref="App"/> ruft beim ersten
/// Erscheinen/Resume <see cref="AusstehendAnwenden"/> nach. Warm-Start (App läuft schon): die Shell
/// ist bereit, der Beitritt erfolgt sofort.</summary>
public static class DeepLink
{
    // Zwischengespeicherter Code, falls die Shell beim Cold-Start noch nicht bereit ist.
    private static string? _ausstehenderCode;

    /// <summary>Einstiegspunkt aus dem Plattform-Code (Android-Intent / iOS-OpenUrl). Parst die URL,
    /// merkt den Code vor und wendet ihn auf dem Main-Thread an. Ungültige/leere URLs werden ignoriert.</summary>
    public static void Behandeln(string? uri)
    {
        var code = CodeAusUri(uri);
        if (string.IsNullOrEmpty(code)) return;   // leerer/ungültiger Code → nichts tun
        _ausstehenderCode = code;
        MainThread.BeginInvokeOnMainThread(AusstehendAnwenden);
    }

    /// <summary>Wendet einen zwischengespeicherten Beitritt an: tritt der Gruppe bei (persistiert den
    /// Code + startet Polling) und navigiert zur Übersichtskarte. Ist die Shell noch nicht bereit,
    /// bleibt der Code ausstehend und <see cref="App"/> ruft die Methode später erneut. Idempotent –
    /// gefahrlos mehrfach aufrufbar (auch ohne ausstehenden Code).</summary>
    public static void AusstehendAnwenden()
    {
        var code = _ausstehenderCode;
        if (string.IsNullOrEmpty(code)) return;

        // Beitritt persistieren (überlebt auch, falls die Navigation unten noch warten muss):
        GruppeLive.Beitreten(code, GruppeLive.StandardName());

        var shell = Shell.Current;
        if (shell == null) return;   // Shell noch nicht bereit → bleibt ausstehend (App holt es nach)

        _ausstehenderCode = null;
        // Zur Übersichtskarte (zeigt die Gruppen-Marker); harmlos, falls schon dort.
        _ = shell.GoToAsync("//start");
    }

    /// <summary>Extrahiert den Gruppen-Code aus einer Deep-Link-URL. Robust gegenüber den Formen
    /// <c>walkhop://g/&lt;code&gt;</c>, <c>walkhop:///g/&lt;code&gt;</c> und Varianten mit/ohne Slash
    /// sowie angehängtem <c>?query</c>/<c>#fragment</c>. Liefert den gesäuberten Code
    /// (<see cref="GruppeService.CodeSaeubern"/>) oder <c>null</c>, wenn keiner enthalten ist.</summary>
    public static string? CodeAusUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        var s = uri.Trim();

        // Schema „walkhop:" (case-insensitiv) abtrennen.
        const string schema = "walkhop:";
        if (s.StartsWith(schema, StringComparison.OrdinalIgnoreCase))
            s = s.Substring(schema.Length);

        // In Pfad-Segmente zerlegen (führende/mehrfache Slashes ignorieren).
        var segmente = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segmente.Length == 0) return null;

        // Optionalen Host „g" überspringen (bei walkhop://g/<code> ist das erste Segment „g").
        int i = string.Equals(segmente[0], "g", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (i >= segmente.Length) return null;

        // Evtl. ?query / #fragment am Code-Segment abschneiden, dann säubern.
        var roh = segmente[i];
        int schnitt = roh.IndexOfAny(new[] { '?', '#' });
        if (schnitt >= 0) roh = roh.Substring(0, schnitt);

        var code = GruppeService.CodeSaeubern(roh);
        return code.Length > 0 ? code : null;
    }
}
