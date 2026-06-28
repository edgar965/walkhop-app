using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace WalkHop;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// Custom-Scheme Deep-Link: walkhop://g/<code> öffnet die App (MAUI routet Custom-Schemes NICHT über
// OnAppLinkRequestReceived → wir lesen das Intent unten selbst aus). Browsable+Default, damit der
// Link aus dem Browser/WhatsApp die App startet.
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "walkhop", DataHost = "g")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Cold-Start: Die App wurde evtl. durch den Deep-Link gestartet → Start-Intent auswerten.
        DeepLinkVerarbeiten(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        // Warm-Start (SingleTop): App lief schon, ein neues Link-Intent kommt herein.
        Intent = intent;   // damit ein späteres getIntent() das aktuelle Intent liefert
        DeepLinkVerarbeiten(intent);
    }

    // Liest die Deep-Link-URL aus dem VIEW-Intent und reicht sie an den gemeinsamen Handler weiter.
    private static void DeepLinkVerarbeiten(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView) return;
        var daten = intent.DataString;
        if (!string.IsNullOrEmpty(daten)) DeepLink.Behandeln(daten);
    }
}
