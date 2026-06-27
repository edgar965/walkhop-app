using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using NUnit.Framework;

namespace SpinNaviApp.UITests;

/// <summary>Eine einzige Appium-Sitzung für die GESAMTE Suite (App nur einmal
/// installieren/starten – sonst dauert jeder Test ewig). Die App ist zustandslos
/// genug, dass Vorhandenseins-Prüfungen sich eine Sitzung teilen können.</summary>
[SetUpFixture]
public class Sitzung
{
    public const string Paket = "com.companyname.spinnaviapp";
    public static AndroidDriver Driver = null!;

    private static string ApkPfad =>
        Environment.GetEnvironmentVariable("SPIN_APK")
        ?? @"A:\spin1more\App\SpinNaviApp\bin\Release\net10.0-android\com.companyname.spinnaviapp-Signed.apk";

    [OneTimeSetUp]
    public void Start()
    {
        var o = new AppiumOptions();
        o.PlatformName = "Android";
        o.AutomationName = "UiAutomator2";
        o.App = ApkPfad;
        o.AddAdditionalAppiumOption("udid", "emulator-5554");
        o.AddAdditionalAppiumOption("newCommandTimeout", 300);
        o.AddAdditionalAppiumOption("autoGrantPermissions", true);
        o.AddAdditionalAppiumOption("noReset", true);
        o.AddAdditionalAppiumOption("fullReset", false);
        // Hinweis: Die FRISCHE APK wird vor dem Lauf per `adb install -r` eingespielt
        // (siehe README) – Appium startet dann nur die installierte App.
        var url = new Uri(Environment.GetEnvironmentVariable("APPIUM_URL") ?? "http://127.0.0.1:4723/");
        Driver = new AndroidDriver(url, o, TimeSpan.FromSeconds(300));
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(3);
        // Warm-up: warten, bis die Shell-UI bereit ist (Hamburger da), sonst rasen die ersten Tests los.
        var ende = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < ende)
        {
            try { if (Driver.FindElements(MobileBy.AccessibilityId("Open navigation drawer")).Count > 0) break; }
            catch { }
            Thread.Sleep(700);
        }
        Thread.Sleep(1500);
    }

    [OneTimeTearDown]
    public void Stop() { try { Driver?.Quit(); } catch { } }
}
