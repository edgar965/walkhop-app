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
        await Auth.AktualisiereAsync();
        Anzeigen();
    }

    private void Anzeigen()
    {
        bool angemeldet = !Auth.Anonym && !string.IsNullOrEmpty(Auth.Email);
        StatusTitel.Text = Auth.Premium
            ? (Auth.AlleFunktionen ? "Premium (alle Funktionen)" : "Premium")
            : (angemeldet ? "Konto" : "Testkonto");
        StatusZeile.Text = angemeldet ? $"Angemeldet als {Auth.Email}"
            : "Anonymes Testkonto (ohne Anmeldung).";
        KontingentZeile.Text = Auth.Premium
            ? "Unbegrenzte Routen/Suchen."
            : $"Heute {Auth.RoutenHeute}/{Auth.GratisProTag} Gratis-Routen genutzt · {Auth.CreditsRouten} Route-Credits · {Auth.OfflineGekauft} Offline-Karten.";

        AuthCard.IsVisible = !angemeldet;
        AbmeldenBtn.IsVisible = angemeldet;
        AuthTitel.Text = Auth.Anonym ? "Konto anlegen oder anmelden" : "Anmelden";
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
        bool ja = await DisplayAlert("Abmelden", "Du wirst abgemeldet und nutzt wieder ein anonymes Testkonto.", "Abmelden", "Abbrechen");
        if (!ja) return;
        await Auth.AbmeldenAsync();
        Anzeigen();
    }

    private async void OnKaufen(object? sender, EventArgs e)
    {
        // Stufe 2: In-App-Kauf (StoreKit/Play Billing). Vorerst Hinweis + Web-Kauf.
        bool web = await DisplayAlert("Premium freischalten",
            "Der In-App-Kauf folgt. Du kannst Premium vorerst auf spin1more.com (Stripe/PayPal) buchen – die Freischaltung gilt dann auch in der App.",
            "Website öffnen", "Schließen");
        if (web) { try { await Launcher.OpenAsync("https://spin1more.com/konto/"); } catch { } }
    }
}
