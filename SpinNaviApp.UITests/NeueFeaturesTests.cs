using NUnit.Framework;

namespace SpinNaviApp.UITests;

/// <summary>Neue Navigations-Funktionen: Gruppen-Knopf + Sprachansage-Toggle (Voice-Pfad).
/// EIN Neustart je Fixture im OneTimeSetUp, dann nur Prüfungen.</summary>
[TestFixture]
public class NaviNeuTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); GehZu("Navigation"); }

    // Gruppen-Position: neuer 👥-Knopf in der rechten Steuerleiste.
    [Test]
    public void Leiste_zeigt_Gruppen_Knopf()
        => Assert.That(Da(ResId("navi_gruppe")), Is.True, "Gruppen-Knopf 'navi_gruppe' fehlt");

    // Sprachansage: der Ton-Knopf löst die TTS-Ausgabe aus (Sprich(...)). Hier als Rauch-Test –
    // Audio lässt sich nicht prüfen, aber das Ein-/Ausschalten darf die App nicht abstürzen lassen.
    [Test]
    public void Ton_Knopf_reagiert_ohne_Absturz()
    {
        Assert.That(Da(ResId("navi_ton")), Is.True, "Ton-Knopf 'navi_ton' fehlt");
        Tap(ResId("navi_ton"));    // schaltet Ansagen EIN → Sprich(...) → TTS
        Warte(900);
        Tap(ResId("navi_ton"));    // wieder AUS
        Warte(500);
        Assert.That(Da(ResId("navi_ton")), Is.True, "Leiste nach Ton-Toggle weg → Absturz?");
    }
}

/// <summary>Offline-Karten: neuer „Region offline speichern"-Knopf + Pakete-Liste im Tab „Karte".</summary>
[TestFixture]
public class EinstOfflineRegionTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); EinstTab("Karte"); }

    [TestCase("Region offline speichern")]
    [TestCase("Meine Offline-Karten")]
    public void Karte_zeigt_Offline_Region(string label)
        => Assert.That(DaText(label), Is.True, $"'{label}' fehlt im Tab Karte");
}
