using NUnit.Framework;

namespace WalkHop.UITests;

/// <summary>UI-Tests für das GRUPPEN-Feature. Seit dem Umbau sitzt die Bedienung in
/// <b>Einstellungen → Tab „Gruppe"</b> (nicht mehr als 👥-Knopf auf der Karte). Buttons tragen
/// AutomationIds: <c>gruppe_erstellen</c>, <c>gruppe_beitreten</c>, <c>gruppe_teilen</c>,
/// <c>gruppe_name</c>, <c>gruppe_verlassen</c>. Geprüft werden: Tab erreichbar + „Erstellen"-Knopf
/// da, Gruppe erstellen → aktiv (Verlassen-Knopf sichtbar), Gruppe verlassen → wieder inaktiv.
///
/// Robust gegen Sprache: der native Namens-Dialog + das Share-Sheet sind Text OHNE AutomationId –
/// dort werden BEIDE Varianten (Deutsch aus Texte.cs + Englisch) akzeptiert. Die App-Buttons werden
/// per AutomationId angesprochen (sprachunabhängig).
///
/// Neustart-Disziplin: GENAU EIN App-Neustart im [OneTimeSetUp]; danach nur noch tippen/prüfen.
/// Reihenfolge-abhängig via [Order]: erst erstellen (Gruppe bleibt aktiv), dann verlassen (Cleanup).</summary>
[TestFixture]
public class GruppeTests : AppBasis
{
    // ---- Sprach-Varianten der nativen Dialog-/Sheet-Texte (de aus Texte.cs, en parallel) ----
    private static readonly string[] Erstellen_Knopf = { "Erstellen", "Create" };
    private static readonly string[] Nein            = { "Nein", "No" };

    [OneTimeSetUp]
    public void Auf()
    {
        Neustart();                 // einziger App-Neustart dieser Fixture
        StartSeiteBereitMachen();   // evtl. Erststart-Dialoge wegklicken
        StelleSicherKeineGruppe();  // Reste aus früheren Läufen entfernen → sauberer Ausgangszustand
    }

    [OneTimeTearDown]
    public void Ab()
    {
        try { StelleSicherKeineGruppe(); } catch { /* best effort */ }
    }

    // 1) Der Gruppe-Tab in den Einstellungen ist erreichbar und zeigt den „Gruppe erstellen"-Knopf.
    [Test, Order(1)]
    public void Gruppe_Tab_zeigt_Erstellen_Knopf()
    {
        GruppeTabOeffnen();
        Assert.That(Da(ResId("gruppe_erstellen"), 3000), Is.True,
            "Knopf 'gruppe_erstellen' fehlt im Einstellungen-Tab 'Gruppe'");
    }

    // 2) „Gruppe erstellen" → im Namens-Dialog „Erstellen" → danach ist die Gruppe AKTIV
    //    (Verlassen-Knopf sichtbar). Lässt die Gruppe aktiv.
    [Test, Order(2)]
    public void Gruppe_erstellen_macht_Gruppe_aktiv()
    {
        GruppeTabOeffnen();
        Assert.That(Da(ResId("gruppe_erstellen"), 3000), Is.True, "Knopf 'gruppe_erstellen' nicht bereit");
        Tap(ResId("gruppe_erstellen"));
        Warte(1300);   // nativer Namens-Dialog erscheint
        Assert.That(TippEines(Erstellen_Knopf), Is.True, "Knopf 'Erstellen'/'Create' im Namens-Dialog fehlt");
        Warte(1800);   // Beitritt aktiv → App teilt automatisch (natives Share-Sheet)
        ShareSheetSchliessen();

        Assert.That(GruppeIstAktiv(), Is.True,
            "Nach dem Erstellen ist die Gruppe nicht aktiv (Verlassen-Knopf nicht sichtbar)");
    }

    // 3) Cleanup: „Gruppe verlassen" → danach wieder INAKTIV (Erstellen-Knopf sichtbar, kein Verlassen mehr).
    [Test, Order(3)]
    public void Gruppe_verlassen_macht_Gruppe_inaktiv()
    {
        GruppeTabOeffnen();
        if (Da(ResId("gruppe_verlassen"), 1500))
        {
            Tap(ResId("gruppe_verlassen"));
            Warte(1000);
        }
        Assert.That(Da(ResId("gruppe_erstellen"), 2500), Is.True,
            "Nach dem Verlassen ist der 'Erstellen'-Knopf nicht wieder sichtbar");
        Assert.That(Da(ResId("gruppe_verlassen"), 800), Is.False,
            "Nach dem Verlassen ist der 'Verlassen'-Knopf noch sichtbar → Gruppe nicht verlassen");
    }

    // ====================================================================================
    //  Helfer
    // ====================================================================================

    private void GruppeTabOeffnen()
    {
        EinstTab("Gruppe");
        Warte(600);
    }

    /// <summary>Tippt die erste sichtbare Text-Variante an; gibt zurück, ob etwas getroffen wurde.</summary>
    private bool TippEines(params string[] texte)
    {
        foreach (var t in texte)
        {
            if (Da(Text(t), 900))
            {
                try { Tap(Text(t)); Warte(800); return true; } catch { /* nächste Variante */ }
            }
        }
        return false;
    }

    /// <summary>Geräte-Zurück (schließt native Dialoge/Sheets ohne App-Neustart).</summary>
    private void Zurueck()
    {
        try { Driver.Navigate().Back(); } catch { }
        Warte(800);
    }

    /// <summary>Nach dem Erstellen teilt die App automatisch (natives Share-Sheet). Nur schließen, wenn
    /// der Gruppe-Tab wirklich verdeckt ist (gruppe_verlassen nicht sichtbar) – sonst KEIN Zurück, um die
    /// App nicht in den Hintergrund zu schicken (Share kann z. B. ohne Ziele gar nicht erscheinen).</summary>
    private void ShareSheetSchliessen()
    {
        if (Da(ResId("gruppe_verlassen"), 1200)) return;   // kein Sheet offen – nichts zu tun
        Zurueck();
        if (Da(ResId("gruppe_verlassen"), 1500)) return;
        Zurueck();
        if (!Da(ResId("gruppe_verlassen"), 1500))          // Notnagel: versehentlich in den Hintergrund → App wieder holen
        {
            try { Driver.ActivateApp(Paket); } catch { }
            Warte(1200);
        }
    }

    /// <summary>Gruppe aktiv? Der Verlassen-Knopf ist nur bei aktiver Gruppe sichtbar. Lässt den Zustand unverändert.</summary>
    private bool GruppeIstAktiv() => Da(ResId("gruppe_verlassen"), 1500);

    /// <summary>Macht die App bedienbar: Beim Erststart erscheinen – teils erst NACH dem Initial-
    /// Netzwerkaufruf (Auth) und damit verzögert – zwei DisplayAlerts („Sprachansagen…?"/„Abbiege-Töne…?",
    /// Knöpfe Ja/Nein bzw. Yes/No), die die Karte verdecken. Daher aktiv pollen: solange das stabile
    /// Start-Element <c>osm_suchfeld</c> nicht da ist, einen evtl. offenen Dialog mit „Nein"/„No" schließen.</summary>
    private void StartSeiteBereitMachen()
    {
        var ende = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < ende)
        {
            if (Da(ResId("osm_suchfeld"), 800)) return;   // Übersichtskarte ist bedienbar
            TippEines(Nein);                              // evtl. Erststart-Dialog mit „Nein"/„No" schließen (sonst no-op)
            Warte(400);
        }
    }

    /// <summary>Sauberer Ausgangszustand: falls (aus früherem Lauf) eine Gruppe aktiv ist, verlassen.</summary>
    private void StelleSicherKeineGruppe()
    {
        GruppeTabOeffnen();
        if (Da(ResId("gruppe_verlassen"), 1200))
        {
            Tap(ResId("gruppe_verlassen"));
            Warte(1000);
        }
    }
}
