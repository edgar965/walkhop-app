using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using NUnit.Framework;

namespace WalkHop.UITests;

/// <summary>Helfer für die nativen UI-Tests: Locator (resource-id/Text), Navigation
/// im Shell-Flyout und in der Einstellungen-TabView – mit Caching, damit aufeinander
/// folgende Prüfungen derselben Seite nicht jedes Mal neu navigieren.</summary>
public abstract class AppBasis
{
    protected const string Paket = Sitzung.Paket;
    protected static AndroidDriver Driver => Sitzung.Driver;

    private static string _seite = "?";    // zuletzt angesteuerte Flyout-Seite
    private static string _tab = "?";       // zuletzt aktiver Einstellungen-Tab

    // App SAUBER neu starten (Start-Seite, kein offenes Flyout). Wird NUR einmal je Fixture
    // im [OneTimeSetUp] aufgerufen → max. so viele Neustarts wie Fixtures (≤ 10), nicht pro Test.
    protected void Neustart()
    {
        try { Driver.TerminateApp(Paket); } catch { }
        try { Driver.ActivateApp(Paket); } catch { }
        Warte(2500);
        _seite = "Start";
        _tab = "?";
    }

    // ---- Locator -----------------------------------------------------------
    // MAUI bildet AutomationId auf Android als resource-id ab (paket:id/<AutomationId>).
    protected static By ResId(string id) => MobileBy.Id($"{Paket}:id/{id}");
    protected static By Text(string t) =>
        MobileBy.AndroidUIAutomator($"new UiSelector().textContains(\"{t}\")");

    protected static void Warte(int ms) => Thread.Sleep(ms);

    protected bool Da(By by, int timeoutMs = 3500)
    {
        var ende = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            try { if (Driver.FindElements(by).Count > 0) return true; } catch { }
            Warte(200);
        } while (DateTime.UtcNow < ende);
        return false;
    }

    protected bool Fehlt(By by) => !Da(by, 1200);
    protected void Tap(By by) => Driver.FindElement(by).Click();

    /// <summary>Text vorhanden – scrollt bei Bedarf in der Liste/ScrollView dorthin.</summary>
    protected bool DaText(string t)
    {
        if (Da(Text(t), 900)) return true;
        try
        {
            Driver.FindElement(MobileBy.AndroidUIAutomator(
                "new UiScrollable(new UiSelector().scrollable(true).instance(0))"
                + $".scrollIntoView(new UiSelector().textContains(\"{t}\"))"));
            return true;
        }
        catch { return false; }
    }

    protected void TapText(string t)
    {
        Assert.That(Da(Text(t)), $"Text '{t}' nicht gefunden");
        Tap(Text(t));
        Warte(900);
    }

    // ---- Navigation --------------------------------------------------------
    protected void OeffneFlyout()
    {
        // Hamburger der Shell-NavBar (content-desc), Fallback: erster ImageButton.
        // Mit Retry – direkt nach App-Start ist die NavBar manchmal noch nicht bereit.
        for (int versuch = 0; versuch < 3; versuch++)
        {
            try { Driver.FindElement(MobileBy.AccessibilityId("Open navigation drawer")).Click(); }
            catch
            {
                var b = Driver.FindElements(
                    MobileBy.AndroidUIAutomator("new UiSelector().className(\"android.widget.ImageButton\")"));
                if (b.Count > 0) b[0].Click();
            }
            Warte(900);
            // „Beenden" gibt es NUR im Flyout → verlässlicher Beleg, dass es offen ist.
            if (Da(Text("Beenden"), 1500)) return;
        }
    }

    /// <summary>Zu einer Flyout-Seite navigieren (idempotent: überspringt, wenn schon dort).</summary>
    protected void GehZu(string seite)
    {
        if (_seite == seite) return;
        OeffneFlyout();
        TapText(seite);
        Warte(1200);
        _seite = seite;
        _tab = "?";
    }

    /// <summary>In der Einstellungen-Seite einen Tab aktivieren (idempotent).</summary>
    protected void EinstTab(string tab)
    {
        GehZu("Einstellungen");
        if (_tab == tab) return;
        TapText(tab);
        Warte(700);
        _tab = tab;
    }

    /// <summary>Zustand „vergessen" (nach zustandsändernden Aktionen aufrufen).</summary>
    protected static void NaviZuruecksetzen() { _seite = "?"; _tab = "?"; }
}
