using NUnit.Framework;

namespace WalkHop.UITests;

/// <summary>UI-Tests für das GRUPPEN-Feature der Übersichtskarte („Start"): der 👥-Knopf
/// (<c>osm_gruppe</c>) öffnet ein natives ActionSheet zum Erstellen/Beitreten bzw. – bei aktiver
/// Gruppe – zum Teilen/Verlassen/Umbenennen. Geprüft werden: Knopf vorhanden, Menü öffnet,
/// Gruppe erstellen → aktiv, Gruppe verlassen → wieder inaktiv (Cleanup).
///
/// Robust gegen Sprache: ActionSheet-/Dialog-Einträge sind nativer Text OHNE AutomationId –
/// daher werden je Eintrag BEIDE Varianten (Deutsch aus Texte.cs + Englisch) akzeptiert.
/// AutomationIds (<c>osm_gruppe</c>) werden genutzt, wo es sie gibt.
///
/// Neustart-Disziplin: GENAU EIN App-Neustart im [OneTimeSetUp]; danach wird nur noch über
/// das ActionSheet getippt/geprüft (kein weiterer Neustart). Reihenfolge-abhängig via [Order]:
/// erst erstellen (Gruppe bleibt aktiv), dann verlassen (Cleanup).</summary>
[TestFixture]
public class GruppeTests : AppBasis
{
    // ---- Sprach-Varianten der Menü-/Dialog-Texte (de aus Texte.cs, en parallel) ----------
    private static readonly string[] Erstellen_Eintrag = { "Gruppe erstellen", "Create group" };
    private static readonly string[] Erstellen_Knopf   = { "Erstellen", "Create" };
    private static readonly string[] Verlassen_Eintrag = { "Gruppe verlassen", "Leave group" };
    private static readonly string[] Abbrechen         = { "Abbrechen", "Cancel" };
    private static readonly string[] Nein              = { "Nein", "No" };

    [OneTimeSetUp]
    public void Auf()
    {
        Neustart();              // einziger App-Neustart dieser Fixture
        StartSeiteBereitMachen();  // evtl. Erststart-Dialoge wegklicken, bis die Übersichtskarte (osm_gruppe) bereit ist
        StelleSicherKeineGruppe(); // Reste aus früheren Läufen entfernen → sauberer Ausgangszustand
    }

    [OneTimeTearDown]
    public void Ab()
    {
        // Aufräumen: eine evtl. noch aktive Gruppe verlassen, damit Folge-Fixtures sauber starten.
        try { StelleSicherKeineGruppe(); } catch { /* best effort */ }
    }

    // 1) Der Gruppen-Knopf ist auf der Start-/Übersichtskarte vorhanden und anklickbar.
    [Test, Order(1)]
    public void Gruppen_Knopf_vorhanden()
        => Assert.That(Da(ResId("osm_gruppe")), Is.True, "Gruppen-Knopf 'osm_gruppe' fehlt auf der Übersichtskarte");

    // 2) Tippen auf den Knopf öffnet das native ActionSheet mit dem Eintrag „Gruppe erstellen"/„Create".
    [Test, Order(2)]
    public void Menue_oeffnet_mit_Erstellen_Eintrag()
    {
        GruppeMenueOeffnen();
        bool da = DaEines(2000, Erstellen_Eintrag);
        MenueSchliessen();
        Assert.That(da, Is.True, "ActionSheet zeigt keinen 'Gruppe erstellen'/'Create group'-Eintrag");
    }

    // 3) „Gruppe erstellen" → im Eingabe-Dialog „Erstellen" → danach ist die Gruppe AKTIV
    //    (Badge sichtbar ODER das Menü zeigt jetzt „Gruppe verlassen"/„Leave"). Lässt die Gruppe aktiv.
    [Test, Order(3)]
    public void Gruppe_erstellen_macht_Gruppe_aktiv()
    {
        GruppeMenueOeffnen();
        Assert.That(TippEines(Erstellen_Eintrag), Is.True, "Eintrag 'Gruppe erstellen' nicht antippbar");
        Warte(1300);   // Eingabe-Dialog (Name) erscheint
        Assert.That(TippEines(Erstellen_Knopf), Is.True, "Knopf 'Erstellen'/'Create' im Eingabe-Dialog fehlt");
        Warte(1800);   // Beitritt aktiv → App teilt automatisch (natives Share-Sheet)
        ShareSheetSchliessen();

        Assert.That(GruppeIstAktiv(), Is.True,
            "Nach dem Erstellen ist die Gruppe nicht aktiv (weder Badge noch Menü-Eintrag 'Gruppe verlassen')");
    }

    // 4) Cleanup: bei aktiver Gruppe „Gruppe verlassen"/„Leave" → danach wieder INAKTIV
    //    (Menü zeigt wieder „Gruppe erstellen", kein „Gruppe verlassen" mehr → Badge weg).
    [Test, Order(4)]
    public void Gruppe_verlassen_macht_Gruppe_inaktiv()
    {
        GruppeMenueOeffnen();
        if (DaEines(1500, Verlassen_Eintrag))
        {
            Assert.That(TippEines(Verlassen_Eintrag), Is.True, "Eintrag 'Gruppe verlassen' nicht antippbar");
            Warte(1200);
        }
        else
        {
            // War (unerwartet) nicht aktiv – Menü schließen und trotzdem den Endzustand prüfen.
            MenueSchliessen();
        }

        // Verifizieren: Gruppe ist jetzt inaktiv → Menü zeigt wieder „erstellen", nicht „verlassen".
        GruppeMenueOeffnen();
        bool wiederErstellen = DaEines(2000, Erstellen_Eintrag);
        bool keinVerlassen = !DaEines(800, Verlassen_Eintrag);
        MenueSchliessen();
        Assert.That(wiederErstellen && keinVerlassen, Is.True,
            "Gruppe wurde nicht verlassen – Menü zeigt nicht wieder 'Gruppe erstellen' (bzw. noch 'verlassen')");
    }

    // ====================================================================================
    //  Helfer
    // ====================================================================================

    /// <summary>Prüft, ob EINE der Text-Varianten sichtbar ist (Sprach-robust).</summary>
    private bool DaEines(int timeoutMs, params string[] texte)
    {
        foreach (var t in texte)
            if (Da(Text(t), timeoutMs)) return true;
        return false;
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

    private void GruppeMenueOeffnen()
    {
        Assert.That(Da(ResId("osm_gruppe"), 8000), Is.True, "Gruppen-Knopf 'osm_gruppe' nicht bereit (Karte verdeckt?)");
        Tap(ResId("osm_gruppe"));
        Warte(1200);   // natives ActionSheet aufbauen lassen
    }

    /// <summary>Offenes ActionSheet schließen: bevorzugt „Abbrechen"/„Cancel", sonst Zurück.</summary>
    private void MenueSchliessen()
    {
        if (!TippEines(Abbrechen)) Zurueck();
        Warte(400);
    }

    /// <summary>Nach dem Erstellen teilt die App automatisch (natives Share-Sheet). Nur schließen,
    /// wenn die Karte wirklich verdeckt ist (osm_gruppe nicht sichtbar) – sonst KEIN Zurück, um die
    /// App nicht in den Hintergrund zu schicken (Share kann z. B. ohne Ziele gar nicht erscheinen).</summary>
    private void ShareSheetSchliessen()
    {
        if (Da(ResId("osm_gruppe"), 1200)) return;   // kein Sheet offen – nichts zu tun
        Zurueck();
        if (Da(ResId("osm_gruppe"), 1500)) return;
        Zurueck();
        if (!Da(ResId("osm_gruppe"), 1500))           // Notnagel: versehentlich in den Hintergrund → App wieder holen
        {
            try { Driver.ActivateApp(Paket); } catch { }
            Warte(1200);
        }
    }

    /// <summary>Gruppe aktiv? Erst Badge (hat evtl. keine AutomationId → meist nicht auffindbar),
    /// sonst zeigt das ActionSheet den Eintrag „Gruppe verlassen"/„Leave". Lässt den Zustand unverändert.</summary>
    private bool GruppeIstAktiv()
    {
        if (Da(ResId("GruppeBadge"), 800)) return true;   // Badge sichtbar (falls auffindbar)
        GruppeMenueOeffnen();
        bool verlassen = DaEines(1500, Verlassen_Eintrag);
        MenueSchliessen();
        return verlassen;
    }

    /// <summary>Macht die Übersichtskarte (Start) bedienbar: Beim Erststart erscheinen – teils erst
    /// NACH dem Initial-Netzwerkaufruf (Auth) und damit verzögert – zwei DisplayAlerts
    /// („Sprachansagen…?"/„Abbiege-Töne…?", Knöpfe Ja/Nein bzw. Yes/No), die die Karte verdecken.
    /// Daher aktiv pollen: solange <c>osm_gruppe</c> nicht da ist, einen evtl. offenen Dialog mit
    /// „Nein"/„No" schließen – bis die Karte sichtbar ist (oder Zeitbudget abgelaufen).</summary>
    private void StartSeiteBereitMachen()
    {
        var ende = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < ende)
        {
            if (Da(ResId("osm_gruppe"), 800)) return;   // Start-Seite ist bedienbar
            TippEines(Nein);                            // evtl. Erststart-Dialog mit „Nein"/„No" schließen (sonst no-op)
            Warte(400);
        }
    }

    /// <summary>Sauberer Ausgangszustand: falls (aus einem früheren Lauf) eine Gruppe aktiv ist,
    /// verlassen. Sonst nur das Menü wieder schließen.</summary>
    private void StelleSicherKeineGruppe()
    {
        GruppeMenueOeffnen();
        if (DaEines(1200, Verlassen_Eintrag))
        {
            TippEines(Verlassen_Eintrag);
            Warte(1000);
        }
        else MenueSchliessen();
    }
}
