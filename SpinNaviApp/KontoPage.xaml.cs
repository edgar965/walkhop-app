using Microsoft.Maui.ApplicationModel;

namespace SpinNaviApp;

public partial class KontoPage : ContentPage
{
    public KontoPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        L.Geaendert += Anzeigen;   // bei Sprachwechsel die imperativ gesetzten Status-Texte neu rendern
        await Auth.AktualisiereAsync();
        Anzeigen();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        L.Geaendert -= Anzeigen;
    }

    private void Anzeigen()
    {
        bool angemeldet = !Auth.Anonym && !string.IsNullOrEmpty(Auth.Email);
        StatusTitel.Text = Auth.Premium
            ? (Auth.AlleFunktionen ? L.T("konto_premium_alle") : L.T("konto_premium"))
            : (angemeldet ? L.T("konto_titel") : L.T("konto_testkonto"));
        StatusZeile.Text = angemeldet ? L.T("konto_status_angemeldet", Auth.Email)
            : L.T("konto_status_anonym");
        KontingentZeile.Text = Auth.Premium
            ? L.T("konto_kontingent_unbegrenzt")
            : L.T("konto_kontingent_genutzt", Auth.RoutenHeute, Auth.GratisProTag, Auth.CreditsRouten, Auth.OfflineGekauft);

        AuthCard.IsVisible = !angemeldet;
        AbmeldenBtn.IsVisible = angemeldet;
        AuthTitel.Text = Auth.Anonym ? L.T("auth_titel_anlegen") : L.T("auth_titel_anmelden");
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        FehlerLabel.IsVisible = false;
        var fehler = await Auth.LoginAsync(EmailFeld.Text ?? "", PasswortFeld.Text ?? "");
        if (fehler != null) { FehlerLabel.Text = fehler; FehlerLabel.IsVisible = true; return; }
        PasswortFeld.Text = "";
        Anzeigen();
    }

    private async void OnRegister(object? sender, EventArgs e)
    {
        FehlerLabel.IsVisible = false;
        // Diese Seite hat keine Namensfelder – schon gepflegte Namen (z. B. aus den
        // Einstellungen) mitgeben, sonst würden sie bei der Registrierung geleert.
        var fehler = await Auth.RegistrierenAsync(EmailFeld.Text ?? "", PasswortFeld.Text ?? "",
            Auth.Vorname, Auth.Name);
        if (fehler != null) { FehlerLabel.Text = fehler; FehlerLabel.IsVisible = true; return; }
        PasswortFeld.Text = "";
        Anzeigen();
    }

    private async void OnAbmelden(object? sender, EventArgs e)
    {
        bool ja = await DisplayAlert(L.T("logout_titel"), L.T("abmelden_frage"), L.T("logout_titel"), L.T("abbrechen"));
        if (!ja) return;
        await Auth.AbmeldenAsync();
        Anzeigen();
    }

    private async void OnKaufen(object? sender, EventArgs e)
    {
        // Stufe 2: In-App-Kauf (StoreKit/Play Billing). Vorerst Hinweis + Web-Kauf.
        bool web = await DisplayAlert(L.T("premium_titel"), L.T("premium_text"),
            L.T("premium_website"), L.T("schliessen"));
        if (web) { try { await Launcher.OpenAsync("https://spin1more.com/konto/"); } catch { } }
    }
}
