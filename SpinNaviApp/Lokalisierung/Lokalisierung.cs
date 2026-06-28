using System.ComponentModel;

namespace SpinNaviApp;

/// <summary>Laufzeit-umschaltbare App-Sprache (Singleton). XAML bindet über die Markup-Extension
/// <see cref="TranslateExtension"/> auf den Indexer <c>this[key]</c>; beim Sprachwechsel meldet der
/// Indexer „Item[]" → alle Bindings aktualisieren sich sofort. Code-Behind nutzt <see cref="L"/>.
///
/// Quelle der Wahrheit für die Sprache ist <see cref="Einst.Sprache"/> (steuert zugleich den
/// Routing-/Ansage-Locale de→de-DE, en→en-US). Default = Englisch; als Admin (e@edgarm.de bzw.
/// Auth.IstAdmin) = Deutsch – solange der Nutzer NICHT selbst gewählt hat.</summary>
public class Lokalisierung : INotifyPropertyChanged
{
    public static Lokalisierung Instanz { get; } = new();

    private string _sprache;

    private Lokalisierung()
    {
        _sprache = Einst.Sprache;   // berücksichtigt die Admin-Default-Regel, falls noch nicht gesetzt
        // Auth-Status ist beim Start evtl. noch nicht geladen (Login async). Wenn er sich ändert und
        // der Nutzer noch keine explizite Sprachwahl getroffen hat, Admin-Default-Regel anwenden.
        Auth.StatusGeaendert += AufAuthGeaendert;
    }

    /// <summary>Aktuelle App-Sprache ("de"/"en").</summary>
    public string Sprache => _sprache;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Wird nach jedem Sprachwechsel ausgelöst – für Code-Behind, das Texte imperativ setzt
    /// (z. B. dynamisch erzeugte Labels, Chips, Konto-Status).</summary>
    public event Action? Geaendert;

    /// <summary>Übersetzung für den Schlüssel in der aktuellen Sprache (XAML-Indexer-Binding).</summary>
    public string this[string key] => Texte.Hole(key, _sprache);

    /// <summary>Sprache durch den Nutzer wählen: persistiert (markiert als explizit gewählt),
    /// steuert den Routing-Locale mit und aktualisiert alle Bindings.</summary>
    public void Wechsle(string sprache)
    {
        if (sprache != "de" && sprache != "en") sprache = "en";
        Einst.Sprache = sprache;   // markiert SpracheGesetzt = true + setzt Einst.Locale
        Setze(sprache);
    }

    private void AufAuthGeaendert()
    {
        if (Einst.SpracheGesetzt) return;          // Nutzer hat selbst gewählt → Admin-Regel nicht überschreiben
        Setze(Einst.StandardSprache);              // Admin → de, sonst → en
    }

    private void Setze(string sprache)
    {
        if (sprache == _sprache) return;
        _sprache = sprache;
        // „Item[]" aktualisiert ALLE Indexer-Bindings (alle {loc:Translate …}); Sprache für direkte Bindings.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sprache)));
        try { Geaendert?.Invoke(); } catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}

/// <summary>Kurzform für Code-Behind: <c>L.T("key")</c> bzw. <c>L.T("key", args)</c> (mit String.Format).</summary>
public static class L
{
    public static string T(string key) => Lokalisierung.Instanz[key];

    public static string T(string key, params object[] args)
    {
        var muster = Lokalisierung.Instanz[key];
        try { return string.Format(muster, args); }
        catch (System.FormatException) { return muster; }
    }

    /// <summary>Aktuelle Sprache ("de"/"en").</summary>
    public static string Sprache => Lokalisierung.Instanz.Sprache;

    /// <summary>Sprachwechsel-Event (für Seiten, die imperativ gesetzte Texte neu rendern).</summary>
    public static event Action? Geaendert
    {
        add => Lokalisierung.Instanz.Geaendert += value;
        remove => Lokalisierung.Instanz.Geaendert -= value;
    }
}
