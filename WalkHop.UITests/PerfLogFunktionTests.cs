using OpenQA.Selenium.Appium;
using NUnit.Framework;

namespace WalkHop.UITests;

// ============================================================================
//  Funktionale UI-Tests für die neuen Funktionen:
//   • Gewanderte Route: Anzeige-Schalter (an/aus) + Farb-Auswahl-Abschnitt
//   • Offroad: Umwege-in-%-Slider (Abschnitt, Default-Wert, SeekBar)
//   • Diagnose-Protokoll: „Logs löschen" (Bestätigungsdialog) + „Logs an Server"
//     (Ergebnis-Dialog, kein Absturz – robust gegen Deploy-/Netz-Zustand)
//  Ein Neustart je Fixture (OneTimeSetUp), dann Prüfungen.
// ============================================================================

/// <summary>Gewanderte Route + Offroad im Tab „Navigation".</summary>
[TestFixture]
public class GewanderteUndOffroadTests : EinstFunktionBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Navigation"); }

    // Anzeige-Schalter der gewanderten Route (Default AN) lässt sich umschalten + wieder zurück.
    [Test, Order(1)]
    public void Gewanderte_Route_Schalter_toggelt()
        => SchalterToggelt("Gewanderte Route auf der Karte");

    // Persistenz: umschalten → Seite verlassen + neu → Zustand aus Einst.* wiederhergestellt.
    [Test, Order(2)]
    public void Gewanderte_Route_Zustand_ueberlebt_Seitenwechsel()
    {
        EinstTab("Navigation");
        bool start = IstAn(SchalterZu("Gewanderte Route auf der Karte"));
        SchalterZu("Gewanderte Route auf der Karte").Click();
        Warte(500);
        bool neu = !start;
        GehZu("Navigation");           // Kartenseite …
        GehZu("Einstellungen");        // … und zurück (Konstruktor liest Einst.* frisch)
        EinstTab("Navigation");
        Assert.That(IstAn(SchalterZu("Gewanderte Route auf der Karte")), Is.EqualTo(neu),
            "Gewanderte-Route-Schalter sollte nach Seitenwechsel erhalten bleiben (Persistenz)");
        SchalterZu("Gewanderte Route auf der Karte").Click();   // Ausgangszustand wiederherstellen
        Warte(400);
    }

    // Farb-Auswahl der gewanderten Route ist vorhanden (Abschnittstitel).
    [Test, Order(3)]
    public void Gewanderte_Route_Farbauswahl_vorhanden()
        => Assert.That(DaText("Farbe der gewanderten Route"), Is.True, "Farb-Auswahl fehlt");

    // Offroad-Abschnitt: Titel + Default-Prozentwert + ein Slider (SeekBar).
    [Test, Order(4)]
    public void Offroad_Abschnitt_mit_Slider_und_Default()
    {
        Assert.That(DaText("OFFROAD"), Is.True, "OFFROAD-Abschnitt fehlt");
        Assert.That(DaText("Umwege in Kauf nehmen"), Is.True, "Offroad-Titel fehlt");
        Assert.That(DaText("30 %"), Is.True, "Offroad-Default 30 % fehlt");
        Assert.That(Da(MobileBy.AndroidUIAutomator("new UiSelector().className(\"android.widget.SeekBar\")")),
            Is.True, "Kein Slider (SeekBar) im Navigations-Tab (Offroad/Lautstärke) gefunden");
    }
}

/// <summary>Diagnose-Protokoll (Logging-Knöpfe) im Tab „Allgemein".</summary>
[TestFixture]
public class ProtokollFunktionTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Allgemein"); }

    // „Logs löschen" öffnet einen Bestätigungsdialog; wir brechen ab (nichts löschen).
    [Test, Order(1)]
    public void Logs_loeschen_zeigt_Bestaetigung()
    {
        Assert.That(DaText("Logs löschen"), Is.True, "'Logs löschen' fehlt");
        TapText("Logs löschen");
        Assert.That(DaText("wirklich löschen"), Is.True, "Lösch-Bestätigung erscheint nicht");
        if (DaText("Abbrechen")) TapText("Abbrechen");   // nicht löschen
    }

    // „Logs an Server": tippbar, zeigt einen Ergebnis-Dialog (leer/gesendet/fehlgeschlagen) mit OK –
    // Rauch-Test (kein Absturz), robust gegen Deploy-/Netz-Zustand des Servers.
    [Test, Order(2)]
    public void Logs_an_Server_reagiert_ohne_Absturz()
    {
        EinstTab("Allgemein");
        Assert.That(DaText("Logs an Server"), Is.True, "'Logs an Server' fehlt");
        TapText("Logs an Server");
        Warte(3000);   // Upload/Netz bzw. „leer"
        Assert.That(DaText("OK"), Is.True, "Kein Ergebnis-Dialog nach 'Logs an Server'");
        if (DaText("OK")) TapText("OK");
        // Seite lebt weiter (kein Absturz).
        Assert.That(DaText("DIAGNOSE-PROTOKOLL"), Is.True, "Allgemein-Tab nach 'Logs an Server' kaputt?");
    }
}
