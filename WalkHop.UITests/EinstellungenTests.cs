using NUnit.Framework;

namespace WalkHop.UITests;

/// <summary>Tab „Allgemein". EIN Neustart + Navigation im OneTimeSetUp, dann nur Prüfungen.</summary>
[TestFixture]
public class EinstAllgemeinTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Allgemein"); }

    [TestCase("App-Sprache")]
    [TestCase("Einstellung der Einheiten")]
    [TestCase("Bildschirm entsperren")]
    [TestCase("Kompakter Suchmodus")]
    [TestCase("Track automatisch aufzeichnen")]   // neue Auto-Aufnahme (statt Tracker-Knopf)
    [TestCase("Fotos nur über WLAN")]
    public void Allgemein_zeigt(string label)
        => Assert.That(DaText(label), Is.True, $"'{label}' fehlt im Tab Allgemein");
}

/// <summary>Tab „Navigation" – inkl. der aus dem Karten-Sheet verschobenen Steuerungen.</summary>
[TestFixture]
public class EinstNavigationTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Navigation"); }

    [TestCase("FORTBEWEGUNG")]
    [TestCase("Fuß")]
    [TestCase("Rad")]
    [TestCase("WEGTYP")]
    [TestCase("Befestigt")]
    [TestCase("Neutral")]
    [TestCase("Offroad")]
    [TestCase("ROUTENOPTIONEN")]
    [TestCase("Autobahn vermeiden")]
    [TestCase("Unbefestigte Wege vermeiden")]
    [TestCase("Schlechte Oberflächen vermeiden")]
    [TestCase("FARBMODUS")]
    [TestCase("Auto-Modus")]
    [TestCase("Tagmodus")]
    [TestCase("Nachtmodus")]
    [TestCase("Sprachnavigation")]
    [TestCase("Lautstärke der Anweisungen")]
    [TestCase("Benachrichtigungstöne")]
    [TestCase("Sprache der Navigation")]
    // „Bluetooth-Wiedergabe" + „KARTENANSICHT/Geneigte Ansicht/2D-Ansicht" wurden entfernt
    // (nicht unterstützt: 3D in Mapsui nicht möglich, BT-Audio-Routing nicht umgesetzt).
    public void Navigation_zeigt(string label)
        => Assert.That(DaText(label), Is.True, $"'{label}' fehlt im Tab Navigation");
}

/// <summary>Tab „Karte" – inkl. Kartenmodus/Overlay (aus dem Karten-Sheet verschoben).</summary>
[TestFixture]
public class EinstKarteTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Karte"); }

    [TestCase("KARTENMODUS")]
    [TestCase("Wandern")]
    [TestCase("Standard")]
    [TestCase("Satellit")]
    [TestCase("Dunkel")]
    [TestCase("Wander-/Radwege-Overlay")]
    // „Schattiertes Relief" + „Hangneigung" entfernt (keine zuverlässige freie Tile-Quelle).
    [TestCase("Manuelle Kartendrehung")]
    [TestCase("OFFLINE-KARTEN")]
    [TestCase("Zwischenspeicher")]
    [TestCase("Offline-Cache leeren")]
    public void Karte_zeigt(string label)
        => Assert.That(DaText(label), Is.True, $"'{label}' fehlt im Tab Karte");
}

/// <summary>Tab „Anmeldung/Premium".</summary>
[TestFixture]
public class EinstAnmeldungTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Anmeldung"); }

    [TestCase("Konto anlegen oder anmelden")]
    [TestCase("Anmelden")]
    [TestCase("Registrieren")]
    [TestCase("Premium freischalten")]
    [TestCase("Über")]
    [TestCase("Version")]
    [TestCase("Server")]
    public void Anmeldung_zeigt(string label)
        => Assert.That(DaText(label), Is.True, $"'{label}' fehlt im Tab Anmeldung");
}

/// <summary>Funktional: Einheiten umschalten (metrisch ↔ imperial) wirkt auf die Anzeige.</summary>
[TestFixture]
public class EinheitenFunktionTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Allgemein"); }

    [Test]
    public void Einheiten_lassen_sich_umschalten()
    {
        Assert.That(DaText("Metrisch"), Is.True, "Start sollte metrisch sein");
        TapText("Einstellung der Einheiten");
        Warte(600);
        Assert.That(DaText("Imperial"), Is.True, "nach Umschalten sollte Imperial stehen");
        TapText("Einstellung der Einheiten");   // zurück auf metrisch (Standard)
        Warte(600);
        Assert.That(DaText("Metrisch"), Is.True, "sollte wieder metrisch sein");
    }
}
