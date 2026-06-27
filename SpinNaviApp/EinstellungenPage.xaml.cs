using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace SpinNaviApp;

public partial class EinstellungenPage : ContentPage
{
    private static readonly Color Aktiv = Color.FromArgb("#0f172a");
    private static readonly Color Inaktiv = Color.FromArgb("#64748b");
    private static readonly Color Weiss = Colors.White;
    private bool _laedt;

    public EinstellungenPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString} (Build {AppInfo.Current.BuildString})";
        ServerLabel.Text = "Server: " + AppConfig.ApiBase;

        _laedt = true;
        // Allgemein
        BildschirmSchalter.IsToggled = Einst.BildschirmWach;
        KompaktSchalter.IsToggled = Einst.KompakteSuche;
        AufnahmeSchalter.IsToggled = Einst.AutoAufnahme;
        FotosStartSchalter.IsToggled = Einst.FotosBeimStart;
        FotosWlanSchalter.IsToggled = Einst.FotosNurWlan;
        SpracheLabelSetzen();
        EinheitenLabelSetzen();
        // Navigation
        FortbewegungMarkieren();
        WegtypMarkieren();
        AutobahnSchalter.IsToggled = Einst.VermeideAutobahn;
        UnbefestigtSchalter.IsToggled = Einst.VermeideUnbefestigt;
        OberflaecheSchalter.IsToggled = Einst.VermeideSchlechteOberflaeche;
        SprachnaviSchalter.IsToggled = Einst.Ton;
        LautstaerkeSlider.Value = Einst.Ansagelautstaerke;
        BenachrichtigungSchalter.IsToggled = Einst.Benachrichtigungstoene;
        FarbmodusMarkieren();
        KartenansichtMarkieren();
        // Karte
        KartenmodusMarkieren();
        OverlaySchalter.IsToggled = Einst.Wanderwege;
        ReliefSchalter.IsToggled = Einst.SchattiertesRelief;
        HangneigungSchalter.IsToggled = Einst.Hangneigung;
        DrehungSchalter.IsToggled = Einst.ManuelleDrehung;
        _laedt = false;

        TabWechseln("allgemein");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        CacheGroesseAnzeigen();
        try { await Auth.AktualisiereAsync(); } catch (Exception ex) { Debug.WriteLine(ex); }
        KontoAnzeigen();
    }

    // ---- Tab-Umschaltung ---------------------------------------------------
    private void OnTab(object? sender, TappedEventArgs e) => TabWechseln(e.Parameter as string ?? "allgemein");

    private void TabWechseln(string name)
    {
        TabAllgemein.IsVisible = name == "allgemein";
        TabNavigation.IsVisible = name == "navigation";
        TabKarte.IsVisible = name == "karte";
        TabAnmeldung.IsVisible = name == "anmeldung";
        SegSet(SegAllg, LblAllg, name == "allgemein");
        SegSet(SegNavi, LblNavi, name == "navigation");
        SegSet(SegKarte, LblKarte, name == "karte");
        SegSet(SegKonto, LblKonto, name == "anmeldung");
    }

    // Aktives Segment: weißer Pill + dunkler, fetter Text; inaktiv: transparent + grau.
    private static void SegSet(Border b, Label l, bool aktiv)
    {
        b.BackgroundColor = aktiv ? Weiss : Colors.Transparent;
        l.TextColor = aktiv ? Aktiv : Inaktiv;
        l.FontAttributes = aktiv ? FontAttributes.Bold : FontAttributes.None;
    }

    // ---- Allgemein ---------------------------------------------------------
    private void OnBildschirm(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.BildschirmWach = e.Value; }
    private void OnKompakt(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.KompakteSuche = e.Value; }
    private void OnAutoAufnahme(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.AutoAufnahme = e.Value; }
    private void OnFotosStart(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.FotosBeimStart = e.Value; }
    private void OnFotosWlan(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.FotosNurWlan = e.Value; }

    private void OnSpracheWechseln(object? sender, TappedEventArgs e)
    {
        Einst.Sprache = Einst.Sprache == "de" ? "en" : "de";
        SpracheLabelSetzen();
    }

    private void SpracheLabelSetzen()
    {
        string t = Einst.Sprache == "en" ? "English" : "Deutsch";
        SpracheWert.Text = t;
        NaviSpracheWert.Text = t;
    }

    private void OnEinheitenWechseln(object? sender, TappedEventArgs e)
    {
        Einst.Einheiten = Einst.Einheiten == "metrisch" ? "imperial" : "metrisch";
        EinheitenLabelSetzen();
    }

    private void EinheitenLabelSetzen() =>
        EinheitenWert.Text = Einst.Einheiten == "imperial" ? "Imperial (mi, ft)" : "Metrisch (km, m)";

    // ---- Navigation --------------------------------------------------------
    private void OnSprachnavi(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.Ton = e.Value; }
    private void OnBenachrichtigung(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.Benachrichtigungstoene = e.Value; }
    private void OnLautstaerke(object? sender, ValueChangedEventArgs e) { if (!_laedt) Einst.Ansagelautstaerke = e.NewValue; }

    private void OnFarbmodus(object? sender, TappedEventArgs e)
    {
        Einst.Farbmodus = e.Parameter as string ?? "auto";
        FarbmodusMarkieren();
    }

    private void FarbmodusMarkieren()
    {
        SegSet(FmAuto, LblFmAuto, Einst.Farbmodus == "auto");
        SegSet(FmTag, LblFmTag, Einst.Farbmodus == "tag");
        SegSet(FmNacht, LblFmNacht, Einst.Farbmodus == "nacht");
    }

    private void OnKartenansicht(object? sender, TappedEventArgs e)
    {
        Einst.Kartenansicht = e.Parameter as string ?? "2d";
        KartenansichtMarkieren();
    }

    private void KartenansichtMarkieren()
    {
        SegSet(KaGeneigt, LblKaGeneigt, Einst.Kartenansicht == "3d");
        SegSet(Ka2d, LblKa2d, Einst.Kartenansicht == "2d");
    }

    private async void OnBluetooth(object? sender, TappedEventArgs e) =>
        await DisplayAlert("Bluetooth-Wiedergabe", "Die Auswahl der Bluetooth-Wiedergabe folgt in einer späteren Version.", "OK");

    // ---- Fortbewegung / Wegtyp / Routenoptionen (vom Navi-Sheet hierher verschoben) ----
    private void OnFortbewegung(object? sender, TappedEventArgs e)
    {
        Einst.Profil = e.Parameter as string ?? "pedestrian";
        FortbewegungMarkieren();
    }

    private void FortbewegungMarkieren()
    {
        SegSet(PfFuss, LblPfFuss, Einst.Profil == "pedestrian");
        SegSet(PfRad, LblPfRad, Einst.Profil == "bicycle");
        SegSet(PfAuto, LblPfAuto, Einst.Profil == "auto");
    }

    private void OnWegtypEinst(object? sender, TappedEventArgs e)
    {
        Einst.Wegtyp = e.Parameter as string ?? "neutral";
        WegtypMarkieren();
    }

    private void WegtypMarkieren()
    {
        SegSet(WtFest, LblWtFest, Einst.Wegtyp == "fest");
        SegSet(WtNeutral, LblWtNeutral, Einst.Wegtyp == "neutral");
        SegSet(WtNatur, LblWtNatur, Einst.Wegtyp == "natur");
    }

    private void OnAutobahnEinst(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.VermeideAutobahn = e.Value; }
    private void OnUnbefestigtEinst(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.VermeideUnbefestigt = e.Value; }
    private void OnOberflaecheEinst(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.VermeideSchlechteOberflaeche = e.Value; }

    // ---- Karte -------------------------------------------------------------
    private void OnKartenmodus(object? sender, TappedEventArgs e)
    {
        if (Enum.TryParse<Kartenmodus>(e.Parameter as string, out var m)) Einst.Karte = m;
        KartenmodusMarkieren();
    }

    private void KartenmodusMarkieren()
    {
        SegSet(KmWandern, LblKmWandern, Einst.Karte == Kartenmodus.Wandern);
        SegSet(KmStandard, LblKmStandard, Einst.Karte == Kartenmodus.Standard);
        SegSet(KmSatellit, LblKmSatellit, Einst.Karte == Kartenmodus.Satellit);
        SegSet(KmDunkel, LblKmDunkel, Einst.Karte == Kartenmodus.Dunkel);
    }

    private void OnOverlay(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.Wanderwege = e.Value; }

    private void OnRelief(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.SchattiertesRelief = e.Value; }
    private void OnHangneigung(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.Hangneigung = e.Value; }
    private void OnDrehung(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.ManuelleDrehung = e.Value; }

    private void CacheGroesseAnzeigen()
    {
        try
        {
            long bytes = 0; int dateien = 0;
            if (Directory.Exists(OfflineKarte.CacheDir))
                foreach (var f in Directory.EnumerateFiles(OfflineKarte.CacheDir, "*", SearchOption.AllDirectories))
                { bytes += new FileInfo(f).Length; dateien++; }
            CacheLabel.Text = dateien == 0 ? "Zwischenspeicher: leer"
                : $"Zwischenspeicher: {dateien} Kacheln · {bytes / 1024.0 / 1024.0:0.0} MB";
        }
        catch (Exception ex) { Debug.WriteLine(ex); CacheLabel.Text = "Zwischenspeicher: –"; }
    }

    private async void OnCacheLeeren(object? sender, EventArgs e)
    {
        bool ja = await DisplayAlert("Offline-Cache leeren", "Alle gespeicherten Kartenkacheln löschen?", "Leeren", "Abbrechen");
        if (!ja) return;
        try
        {
            if (Directory.Exists(OfflineKarte.CacheDir))
                Directory.Delete(OfflineKarte.CacheDir, true);
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        CacheGroesseAnzeigen();
    }

    // ---- Anmeldung / Premium ----------------------------------------------
    private void KontoAnzeigen()
    {
        bool angemeldet = !Auth.Anonym && !string.IsNullOrEmpty(Auth.Email);
        StatusTitel.Text = Auth.Premium
            ? (Auth.AlleFunktionen ? "Premium (alle Funktionen)" : "Premium")
            : (angemeldet ? "Konto" : "Testkonto");
        StatusZeile.Text = angemeldet ? $"Angemeldet als {Auth.Email}" : "Anonymes Testkonto (ohne Anmeldung).";
        KontingentZeile.Text = Auth.Premium
            ? "Unbegrenzte Routen/Suchen."
            : $"Heute {Auth.RoutenHeute}/{Auth.GratisProTag} Gratis-Routen · {Auth.CreditsRouten} Route-Credits · {Auth.OfflineGekauft} Offline-Karten.";

        AuthCard.IsVisible = !angemeldet;
        AbmeldenBtn.IsVisible = angemeldet;
        PremiumBtn.IsVisible = !Auth.Premium;
        AuthTitel.Text = Auth.Anonym ? "Konto anlegen oder anmelden" : "Anmelden";

        // Felder stets am persistierten Profil ausrichten (nicht nur wenn leer) – sonst
        // bliebe ein veralteter/fremder Tippwert stehen und würde mitregistriert.
        VornameFeld.Text = Auth.Vorname;
        NameFeld.Text = Auth.Name;
        if (angemeldet) EmailFeld.Text = Auth.Email;
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        FehlerLabel.IsVisible = false;
        var fehler = await Auth.LoginAsync(EmailFeld.Text ?? "", PasswortFeld.Text ?? "");
        if (fehler != null) { FehlerLabel.Text = fehler; FehlerLabel.IsVisible = true; return; }
        PasswortFeld.Text = "";
        KontoAnzeigen();
    }

    private async void OnRegister(object? sender, EventArgs e)
    {
        FehlerLabel.IsVisible = false;
        var fehler = await Auth.RegistrierenAsync(EmailFeld.Text ?? "", PasswortFeld.Text ?? "",
            VornameFeld.Text ?? "", NameFeld.Text ?? "");
        if (fehler != null) { FehlerLabel.Text = fehler; FehlerLabel.IsVisible = true; return; }
        PasswortFeld.Text = "";
        KontoAnzeigen();
    }

    private async void OnAbmelden(object? sender, EventArgs e)
    {
        bool ja = await DisplayAlert("Abmelden", "Du wirst abgemeldet und nutzt wieder ein anonymes Testkonto.", "Abmelden", "Abbrechen");
        if (!ja) return;
        await Auth.AbmeldenAsync();
        EmailFeld.Text = "";
        KontoAnzeigen();
    }

    private async void OnKaufen(object? sender, EventArgs e)
    {
        bool web = await DisplayAlert("Premium freischalten",
            "Der In-App-Kauf folgt. Du kannst Premium vorerst auf spin1more.com (Stripe/PayPal) buchen – die Freischaltung gilt dann auch in der App.",
            "Website öffnen", "Schließen");
        if (web) { try { await Launcher.OpenAsync("https://spin1more.com/konto/"); } catch (Exception ex) { Debug.WriteLine(ex); } }
    }
}
