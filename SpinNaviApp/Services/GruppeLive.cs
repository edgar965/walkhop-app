using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace SpinNaviApp;

/// <summary>Gemeinsame Live-Gruppen-Komponente (eine Quelle der Wahrheit für BEIDE Karten-Seiten):
/// hält den aktiven Gruppen-Code/Namen, pollt die Mitglieder-Positionen und sendet die eigene
/// Position gedrosselt. Die Seiten abonnieren <see cref="Mitglieder"/> (Marker zeichnen) und
/// <see cref="Geaendert"/> (Knopf-Zustand) und rufen <see cref="Sende"/> aus ihrer GPS-Schleife.
/// So bleibt die Gruppen-Logik an EINER Stelle (kein Kopieren zwischen MainPage/UebersichtPage).</summary>
public static class GruppeLive
{
    private const int PollSekunden = 5;     // Mitglieder-Positionen alle 5 s laden
    private const long SendeDrosselMs = 6000;   // eigene Position höchstens alle 6 s senden
    // Verwechslungsarme Basis (ohne 0/o/1/l) für gut teilbare, schwer ratbare Codes.
    private const string CodeAbc = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>Mitglieder-Positionen frisch gepollt (auf dem UI-Thread).</summary>
    public static event Action<List<GruppenMitglied>>? Mitglieder;
    /// <summary>Beitritt/Verlassen – für Knopf-/Badge-Aktualisierung.</summary>
    public static event Action? Geaendert;

    private static IDispatcherTimer? _timer;
    private static long _letztSendeMs = -100000;

    public static string Code => Einst.GruppenCode;
    public static bool Aktiv => Code.Length > 0;

    /// <summary>Anzeigename in der Gruppe (gewählter Name, sonst Konto-Name, sonst „Wanderer").</summary>
    public static string Anzeigename()
    {
        if (!string.IsNullOrWhiteSpace(Einst.GruppenName)) return Einst.GruppenName.Trim();
        if (!string.IsNullOrWhiteSpace(Auth.Name)) return Auth.Name.Trim();
        return L.T("gruppe_default_name");
    }

    /// <summary>Neuer zufälliger, schwer ratbarer Gruppen-Code (8 Zeichen).</summary>
    public static string NeuerCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(8);
        foreach (var b in bytes) sb.Append(CodeAbc[b % CodeAbc.Length]);
        return sb.ToString();
    }

    /// <summary>Teil-Link für WhatsApp & Co. – öffnet die App (Deep-Link) bzw. die Web-Gruppenseite.</summary>
    public static string TeilenLink(string code) => "https://walkhop.com/g/" + GruppeService.CodeSaeubern(code);

    /// <summary>Einer Gruppe beitreten (Code säubern, Name merken) und Polling starten.</summary>
    public static void Beitreten(string code, string name)
    {
        var sauber = GruppeService.CodeSaeubern(code);
        if (sauber.Length == 0) return;
        if (!string.IsNullOrWhiteSpace(name)) Einst.GruppenName = name.Trim();
        Einst.GruppenCode = sauber;
        Geaendert?.Invoke();
        Starten();
    }

    /// <summary>Gruppe verlassen (Polling stoppen, Code löschen).</summary>
    public static void Verlassen()
    {
        _timer?.Stop();
        Einst.GruppenCode = "";
        Geaendert?.Invoke();
    }

    /// <summary>Polling (wieder) starten – z. B. wenn eine Karten-Seite erscheint und eine Gruppe aktiv ist.</summary>
    public static void Starten()
    {
        if (!Aktiv) return;
        if (_timer == null)
        {
            _timer = Application.Current?.Dispatcher.CreateTimer();
            if (_timer == null) return;
            _timer.Interval = TimeSpan.FromSeconds(PollSekunden);
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => _ = PollAsync();
        }
        _timer.Stop();
        _timer.Start();
        _ = PollAsync();   // sofort einmal
    }

    /// <summary>Polling pausieren (Code bleibt erhalten) – z. B. wenn die Karten-Seite verschwindet.</summary>
    public static void Pausieren() => _timer?.Stop();

    /// <summary>Eigene Position in die Gruppe schreiben (gedrosselt, fire-and-forget).</summary>
    public static void Sende(double lat, double lng)
    {
        if (!Aktiv) return;
        if (Environment.TickCount64 - _letztSendeMs < SendeDrosselMs) return;
        _letztSendeMs = Environment.TickCount64;
        _ = GruppeService.SendePositionAsync(Code, Anzeigename(), lat, lng);
    }

    private static async Task PollAsync()
    {
        if (!Aktiv) return;
        var liste = await GruppeService.HoleAsync(Code);
        if (!Aktiv) return;   // zwischenzeitlich verlassen
        Mitglieder?.Invoke(liste);
    }
}
