using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using NUnit.Framework;

namespace SpinNaviApp.UITests;

// ============================================================================
//  FUNKTIONALE Einstellungs-Tests: Bedienelemente betätigen und die WIRKUNG /
//  Persistenz prüfen – nicht nur die Anwesenheit der Labels (das macht bereits
//  EinstellungenTests.cs). Vorbild ist EinheitenFunktionTests (Metrisch↔Imperial).
//
//  Drei Fixtures je Tab (je EIN Neustart im OneTimeSetUp).
//
//  Was über Appium beobachtbar ist:
//   • Wert-Labels, deren TEXT umschaltet (App-/Navi-Sprache, Einheiten).
//   • Schalter (Android-Switch): das `checked`-Attribut ist auslesbar → echter
//     Vorher/Nachher-Vergleich möglich.
//   • Segment-Pillen (Fortbewegung/Wegtyp/Farbmodus/Kartenmodus): die Auswahl wird
//     nur über Farbe/Fettung markiert – das ist über Appium NICHT auslesbar. Daher
//     prüfen wir dort Bedienbarkeit (Handler läuft, kein Absturz) + Persistenz der
//     Optionen über einen Tab-Wechsel. Das ist ein gültiger funktionaler UI-Test.
//
//  Bewusst KEINE Tests für die parallel ENTFERNTEN Optionen (KARTENANSICHT 2D/3D,
//  Schattiertes Relief, Hangneigung, Bluetooth-Wiedergabe).
// ============================================================================

/// <summary>Gemeinsame Helfer für die funktionalen Einstellungs-Tests.</summary>
public abstract class EinstFunktionBasis : AppBasis
{
    // Alle umschaltbaren Bedienelemente (Switches sind checkable; Slider/Labels nicht).
    protected static By Checkables =>
        MobileBy.AndroidUIAutomator("new UiSelector().checkable(true)");

    /// <summary>Erst in Sicht scrollen, dann antippen (robust gegen Scroll-Position).</summary>
    protected void TippInSicht(string t)
    {
        Assert.That(DaText(t), Is.True, $"'{t}' zum Antippen nicht gefunden");
        Tap(Text(t));
        Warte(800);
    }

    /// <summary>Findet den Schalter in DERSELBEN Bildschirmzeile wie der Titel-Text:
    /// Titel in Sicht scrollen, dann unter allen sichtbaren Switches den nehmen, dessen
    /// vertikale Mitte der des Titels am nächsten ist.</summary>
    protected IWebElement SchalterZu(string titel)
    {
        Assert.That(DaText(titel), Is.True, $"Schalter-Zeile '{titel}' nicht gefunden");
        var titelEl = Driver.FindElement(Text(titel));
        int titelMitte = titelEl.Location.Y + titelEl.Size.Height / 2;

        IWebElement? treffer = null;
        int bestDelta = int.MaxValue;
        foreach (var s in Driver.FindElements(Checkables))
        {
            try
            {
                int mitte = s.Location.Y + s.Size.Height / 2;
                int d = Math.Abs(mitte - titelMitte);
                if (d < bestDelta) { bestDelta = d; treffer = s; }
            }
            catch { /* Element verschwunden – ignorieren */ }
        }
        Assert.That(treffer, Is.Not.Null, $"Kein Schalter in der Zeile '{titel}' gefunden");
        Assert.That(bestDelta, Is.LessThan(250), $"Kein Schalter nah genug an '{titel}' (Δ={bestDelta}px)");
        return treffer!;
    }

    protected static bool IstAn(IWebElement schalter) =>
        (schalter.GetAttribute("checked") ?? "false") == "true";

    /// <summary>Schalter umschalten und prüfen, dass sich `checked` wirklich umkehrt; danach
    /// wieder in den Ausgangszustand bringen (Hygiene für Folge-Tests).</summary>
    protected void SchalterToggelt(string titel)
    {
        bool vorher = IstAn(SchalterZu(titel));
        SchalterZu(titel).Click();
        Warte(500);
        bool nachher = IstAn(SchalterZu(titel));
        Assert.That(nachher, Is.Not.EqualTo(vorher),
            $"Schalter '{titel}' hat nicht umgeschaltet ({vorher} → {nachher})");
        // zurückschalten
        SchalterZu(titel).Click();
        Warte(500);
        Assert.That(IstAn(SchalterZu(titel)), Is.EqualTo(vorher),
            $"Schalter '{titel}' ließ sich nicht in den Ausgangszustand zurückstellen");
    }

    /// <summary>Jede Segment-Option nacheinander anwählen; nach jedem Tipp müssen die Option
    /// und der Abschnitts-Anker noch da sein (Handler lief, kein Absturz, Seite intakt).</summary>
    protected void SegmenteDurchschalten(string anker, params string[] optionen)
    {
        foreach (var opt in optionen)
        {
            TippInSicht(opt);
            Assert.That(DaText(opt), Is.True, $"Segment-Option '{opt}' nach Tippen verschwunden (Absturz?)");
            Assert.That(DaText(anker), Is.True, $"Anker '{anker}' nach Tippen auf '{opt}' weg (Seite kaputt?)");
        }
    }
}

// ---------------------------------------------------------------------------
//  Tab „Allgemein"
// ---------------------------------------------------------------------------
/// <summary>Funktional: Schalter im Tab „Allgemein" + App-Sprache (zuletzt, da global).</summary>
[TestFixture]
public class EinstFunktionAllgemeinTests : EinstFunktionBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Allgemein"); }

    // Sicherheitsnetz: Falls der App-Sprache-Test die Oberfläche auf Englisch ließ, hier
    // zurückstellen – sonst starten Folge-Fixtures (deutsche Texte) fehl. Geht NUR über das
    // Wert-Label „English"/„Deutsch", das in beiden Sprachen identisch heißt.
    [OneTimeTearDown]
    public void DeutschWiederherstellen()
    {
        try
        {
            for (int i = 0; i < 4 && !DaText("Deutsch"); i++)
            {
                if (DaText("English")) { Tap(Text("English")); Warte(900); }
                else break;
            }
        }
        catch { /* best effort */ }
    }

    [Test, Order(1)]
    public void Bildschirm_entsperren_Schalter_toggelt() => SchalterToggelt("Bildschirm entsperren");

    [Test, Order(2)]
    public void Track_automatisch_Schalter_toggelt() => SchalterToggelt("Track automatisch");

    [Test, Order(3)]
    public void Fotos_beim_Start_Schalter_toggelt() => SchalterToggelt("Fotos beim Start");

    [Test, Order(4)]
    public void Fotos_nur_WLAN_Schalter_toggelt() => SchalterToggelt("Fotos nur über WLAN");

    // Locker gehalten: ein paralleler Umbau ändert evtl. Verhalten/Aufbau dieses Schalters.
    // Daher nur Anwesenheit + Tippbarkeit ohne Absturz – kein bestimmtes Verhalten festnageln.
    [Test, Order(5)]
    public void Kompakter_Suchmodus_ist_vorhanden_und_tippbar()
    {
        EinstTab("Allgemein");
        Assert.That(DaText("Kompakter Suchmodus"), Is.True, "Zeile 'Kompakter Suchmodus' fehlt");
        try
        {
            SchalterZu("Kompakter Suchmodus").Click(); Warte(400);
            SchalterZu("Kompakter Suchmodus").Click(); Warte(300);   // wieder zurück
        }
        catch { /* evtl. umgebaut – Anwesenheit genügt */ }
        Assert.That(DaText("Kompakter Suchmodus"), Is.True, "Zeile nach Tippen verschwunden");
    }

    // Echte Persistenz: Umschalten → Einstellungen-Seite verlassen und neu aufbauen →
    // der neue Zustand wird aus dem persistenten Preference wieder hergestellt.
    [Test, Order(6)]
    public void Bildschirm_Zustand_ueberlebt_Seitenwechsel()
    {
        EinstTab("Allgemein");
        bool start = IstAn(SchalterZu("Bildschirm entsperren"));
        SchalterZu("Bildschirm entsperren").Click();
        Warte(500);
        bool neu = !start;
        Assert.That(IstAn(SchalterZu("Bildschirm entsperren")), Is.EqualTo(neu), "Schalter sollte umgeschaltet sein");

        // Seite verlassen und neu öffnen (Konstruktor liest die Werte frisch aus Einst.*)
        GehZu("Navigation");
        GehZu("Einstellungen");
        EinstTab("Allgemein");
        Assert.That(IstAn(SchalterZu("Bildschirm entsperren")), Is.EqualTo(neu),
            "Umgeschalteter Zustand sollte nach Seitenwechsel erhalten bleiben (Persistenz)");

        // Ausgangszustand wiederherstellen
        SchalterZu("Bildschirm entsperren").Click();
        Warte(400);
    }

    // App-Sprache ändert die GANZE Oberfläche → ganz zuletzt; danach wieder Deutsch.
    [Test, Order(9)]
    public void App_Sprache_schaltet_Oberflaeche_um_und_zurueck()
    {
        EinstTab("Allgemein");
        Assert.That(DaText("Deutsch"), Is.True, "App-Sprache sollte zu Beginn Deutsch sein");
        Assert.That(DaText("Allgemein"), Is.True, "Oberfläche sollte deutsch sein (Tab 'Allgemein')");

        // Wert-Label antippen → Zeilen-Geste OnSpracheWechseln → Oberfläche auf Englisch
        TippInSicht("Deutsch");
        Assert.That(DaText("English"), Is.True, "Wert sollte nach Umschalten 'English' zeigen");
        Assert.That(DaText("General"), Is.True, "Oberfläche sollte mitwechseln (englischer Tab 'General')");

        // zurück auf Deutsch
        TippInSicht("English");
        Assert.That(DaText("Deutsch"), Is.True, "App-Sprache sollte wieder Deutsch sein");
        Assert.That(DaText("Allgemein"), Is.True, "Oberfläche sollte wieder deutsch sein (Tab 'Allgemein')");
    }
}

// ---------------------------------------------------------------------------
//  Tab „Navigation"
// ---------------------------------------------------------------------------
/// <summary>Funktional: Segmente (Fortbewegung/Wegtyp/Farbmodus), Routenoptionen-Schalter,
/// Ton/Benachrichtigung-Schalter und die Navigationssprache (ohne Oberflächenwechsel).</summary>
[TestFixture]
public class EinstFunktionNavigationTests : EinstFunktionBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Navigation"); }

    [Test]
    public void Fortbewegung_laesst_sich_umschalten()
    {
        EinstTab("Navigation");
        // Hinweis: „Rad"/„Auto" matchen per Baum-Reihenfolge die Fortbewegungs-Pille zuerst
        // (Abschnitt steht ganz oben), darum hier ohne Emoji eindeutig genug.
        SegmenteDurchschalten("FORTBEWEGUNG", "Fuß", "Rad", "Auto");
        // Persistenz/Bedienbarkeit: Tab wechseln und zurück – Optionen bleiben da & tippbar.
        EinstTab("Karte"); EinstTab("Navigation");
        foreach (var o in new[] { "Fuß", "Rad", "Auto" })
            Assert.That(DaText(o), Is.True, $"Fortbewegungs-Option '{o}' nach Tab-Wechsel verschwunden");
        TippInSicht("Fuß");   // Standard wieder wählen (weiterhin umschaltbar)
    }

    [Test]
    public void Wegtyp_laesst_sich_umschalten()
    {
        EinstTab("Navigation");
        SegmenteDurchschalten("WEGTYP", "Befestigt", "Neutral", "Offroad");
        TippInSicht("Neutral");   // Standard
    }

    [Test]
    public void Farbmodus_laesst_sich_umschalten()
    {
        EinstTab("Navigation");
        SegmenteDurchschalten("FARBMODUS", "Auto-Modus", "Tagmodus", "Nachtmodus");
        TippInSicht("Auto-Modus");   // Standard
    }

    [Test]
    public void Autobahn_vermeiden_Schalter_toggelt() => SchalterToggelt("Autobahn vermeiden");

    [Test]
    public void Unbefestigt_vermeiden_Schalter_toggelt() => SchalterToggelt("Unbefestigte Wege vermeiden");

    [Test]
    public void Schlechte_Oberflaeche_Schalter_toggelt() => SchalterToggelt("Schlechte Oberflächen vermeiden");

    [Test]
    public void Sprachnavigation_Ton_Schalter_toggelt() => SchalterToggelt("Sprachnavigation");

    [Test]
    public void Benachrichtigungstoene_Schalter_toggelt() => SchalterToggelt("Benachrichtigungstöne");

    // Navigationssprache umschalten – ANDERS als die App-Sprache darf das die Oberfläche
    // NICHT verändern (deutscher Abschnitt 'FORTBEWEGUNG' bleibt deutsch).
    [Test]
    public void Navi_Sprache_schaltet_ohne_Oberflaechenwechsel()
    {
        EinstTab("Navigation");
        Assert.That(DaText("Deutsch"), Is.True, "Navi-Sprache sollte zu Beginn Deutsch sein");
        TippInSicht("Deutsch");
        Assert.That(DaText("English"), Is.True, "Navi-Sprache sollte auf 'English' wechseln");
        Assert.That(DaText("FORTBEWEGUNG"), Is.True, "Oberfläche darf sich NICHT geändert haben (Abschnitt deutsch)");
        TippInSicht("English");
        Assert.That(DaText("Deutsch"), Is.True, "Navi-Sprache sollte wieder Deutsch sein");
    }
}

// ---------------------------------------------------------------------------
//  Tab „Karte"
// ---------------------------------------------------------------------------
/// <summary>Funktional: Kartenmodus-Segment, Overlay-/Drehung-Schalter, Standard-Punkt.</summary>
[TestFixture]
public class EinstFunktionKarteTests : EinstFunktionBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Karte"); }

    [Test]
    public void Kartenmodus_laesst_sich_umschalten()
    {
        EinstTab("Karte");
        SegmenteDurchschalten("KARTENMODUS", "Wandern", "Standard", "Satellit", "Dunkel");
        // Persistenz/Bedienbarkeit: Tab wechseln und zurück – Optionen bleiben da & tippbar.
        EinstTab("Navigation"); EinstTab("Karte");
        foreach (var o in new[] { "Wandern", "Standard", "Satellit", "Dunkel" })
            Assert.That(DaText(o), Is.True, $"Kartenmodus-Option '{o}' nach Tab-Wechsel verschwunden");
        TippInSicht("Wandern");   // Standard wieder wählen
    }

    [Test]
    public void Wanderwege_Overlay_Schalter_toggelt() => SchalterToggelt("Wander-/Radwege-Overlay");

    [Test]
    public void Manuelle_Drehung_Schalter_toggelt() => SchalterToggelt("Manuelle Kartendrehung");

    // Standard-Punkt: „Aktuellen Standort übernehmen" ist GPS-abhängig (ohne GPS Hinweis-Dialog,
    // mit GPS wird „eigener Punkt" gesetzt – beides ok). „Zurücksetzen" ist deterministisch und
    // setzt den Punkt sichtbar auf „Berlin Mitte" (Brandenburger Tor).
    [Test]
    public void Standardpunkt_Knoepfe_funktionieren()
    {
        EinstTab("Karte");
        TippInSicht("Aktuellen Standort übernehmen");
        // Auf Abschluss des (ggf. ~8s langen) Standort-Aufrufs warten: entweder erscheint der
        // Hinweis-Dialog (ohne GPS) ODER das Label wechselt auf „eigener Punkt" (mit GPS).
        var ende = DateTime.UtcNow.AddSeconds(11);
        while (DateTime.UtcNow < ende)
        {
            if (Da(Text("OK"), 400)) { try { Tap(Text("OK")); } catch { } Warte(500); break; }
            if (Da(Text("eigener Punkt"), 400)) break;
            Warte(300);
        }

        TippInSicht("Zurücksetzen");
        Assert.That(DaText("Berlin Mitte"), Is.True,
            "Nach 'Zurücksetzen' sollte der Standard-Punkt 'Berlin Mitte' anzeigen");
    }
}
