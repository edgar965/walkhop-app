namespace WalkHop;

/// <summary>Zentrale, NUTZERSICHTBARE Fehlermeldung – bewusst MAUI-FREI gehalten, damit Services,
/// die hier aufrufen, weiter im Unit-Test-Projekt (ohne MAUI) kompilieren. Statt Fehler still per
/// Debug.WriteLine zu verschlucken, ruft jeder relevante catch-Block <see cref="Fehler"/>.
/// Die eigentliche Anzeige (Dialog mit „Diesen Fehler ignorieren"-Knopf, Throttle, Persistenz)
/// liefert die MAUI-Schicht <c>MeldungAnzeige</c> über die Delegaten unten; sie registriert sich
/// per ModuleInitializer selbst. Ohne MAUI (Tests) bleiben die Delegaten null → nur Log.</summary>
public static class Meldung
{
    /// <summary>Von der MAUI-Schicht gesetzt: zeigt (Kontext, Exception) als Dialog.</summary>
    public static Action<string, Exception?>? Anzeiger;
    /// <summary>Von der MAUI-Schicht gesetzt: leert die Liste dauerhaft ignorierter Fehler.</summary>
    public static Action? ResetIgnoriert;
    /// <summary>Von der MAUI-Schicht gesetzt: Anzahl dauerhaft ignorierter Fehlerarten.</summary>
    public static Func<int>? AnzahlIgnoriert;
    /// <summary>Von der App-Schicht (<c>Protokoll</c>) gesetzt: schreibt (Kontext, Exception) persistent
    /// in die Log-Datei. So landet JEDER gemeldete Fehler auch im hochladbaren Protokoll.</summary>
    public static Action<string, Exception?>? Protokollierer;
    /// <summary>Von der App-Schicht gesetzt: schreibt eine reine Log-Zeile (Kategorie, Text) – ohne Dialog.
    /// Für Ablauf-/Ereignis-Protokollierung (z. B. Navigations-Lebenszyklus), nicht für Fehler.</summary>
    public static Action<string, string>? Notierer;

    /// <summary>Meldet einen Fehler: loggt ihn (Debug + Protokoll-Datei) UND zeigt ihn (sofern MAUI-Anzeiger
    /// registriert) als Dialog. Nie werfend.</summary>
    /// <param name="kontext">Kurzbeschreibung WAS schiefging (z. B. „Touren laden").</param>
    /// <param name="ex">Optionale Exception – Typ + Message werden mit angezeigt.</param>
    public static void Fehler(string kontext, Exception? ex = null)
    {
        System.Diagnostics.Debug.WriteLine($"[Fehler] {kontext}: {ex}");
        try { Protokollierer?.Invoke(kontext, ex); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
        try { Anzeiger?.Invoke(kontext, ex); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    /// <summary>Protokolliert ein Ereignis (Kategorie + Text) NUR in die Log-Datei – kein Dialog.
    /// Für Ablauf-Diagnose (z. B. „Navigation gestartet/beendet"). Nie werfend.</summary>
    public static void Notiz(string kategorie, string text)
    {
        System.Diagnostics.Debug.WriteLine($"[{kategorie}] {text}");
        try { Notierer?.Invoke(kategorie, text); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); }
    }

    /// <summary>Leert die Ignorier-Liste (Einstellungen → „Ignorierte Fehler zurücksetzen").</summary>
    public static void IgnorierteZuruecksetzen() => ResetIgnoriert?.Invoke();

    /// <summary>Anzahl dauerhaft ignorierter Fehlerarten (für die Anzeige in den Einstellungen).</summary>
    public static int IgnorierteAnzahl => AnzahlIgnoriert?.Invoke() ?? 0;
}
