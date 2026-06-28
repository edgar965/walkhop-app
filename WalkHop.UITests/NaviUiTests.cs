using NUnit.Framework;

namespace WalkHop.UITests;

/// <summary>Flyout-Menü. EIN Neustart je Fixture (im OneTimeSetUp), dann nur Prüfungen.</summary>
[TestFixture]
public class FlyoutTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); OeffneFlyout(); }

    [TestCase("Start")]
    [TestCase("Navigation")]
    [TestCase("Konto")]
    [TestCase("Einstellungen")]
    [TestCase("Beenden")]            // umbenannt von „Logout"
    public void Flyout_zeigt_Eintrag(string eintrag)
        => Assert.That(Da(Text(eintrag)), Is.True, $"Flyout-Eintrag '{eintrag}' fehlt");

    [Test]
    public void Flyout_hat_kein_Logout_mehr()
        => Assert.That(Da(Text("Logout"), 1200), Is.False, "Alter 'Logout'-Eintrag sollte 'Beenden' heißen");
}

/// <summary>Navigationsseite: Icon-Leiste (resource-ids) + kein Zahnrad.</summary>
[TestFixture]
public class NaviSeiteTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); GehZu("Navigation"); }

    [TestCase("navi_zentrieren")]
    [TestCase("navi_ton")]
    [TestCase("navi_suche")]
    [TestCase("navi_vollbild")]
    public void Leiste_zeigt_Icon(string id)
        => Assert.That(Da(ResId(id)), Is.True, $"Leisten-Icon '{id}' fehlt");

    [Test]
    public void Leiste_hat_kein_Zahnrad()
        => Assert.That(Fehlt(ResId("navi_einstellungen")), Is.True, "Einstellungs-Zahnrad sollte entfernt sein");
}

/// <summary>Navigationsseite: Suche öffnet „Wohin?"-/Suchdialog.</summary>
[TestFixture]
public class NaviSucheTests : AppBasis
{
    [OneTimeSetUp]
    public void Auf() { Neustart(); GehZu("Navigation"); }

    [Test]
    public void Suche_oeffnet_Dialog()
    {
        Tap(ResId("navi_suche"));
        Warte(1500);
        bool da = Da(Text("Nach Name suchen")) || Da(Text("Orte suchen")) || Da(Text("Wohin"));
        if (Da(Text("Abbrechen"), 1000)) { try { Tap(Text("Abbrechen")); } catch { } }
        Assert.That(da, Is.True, "Such-Dialog ließ sich nicht öffnen");
    }
}
