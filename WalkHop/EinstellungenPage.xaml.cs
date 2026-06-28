using System.Diagnostics;
using Mapsui;
using Mapsui.Projections;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace WalkHop;

public partial class EinstellungenPage : ContentPage
{
    private static readonly Color Aktiv = Color.FromArgb("#0f172a");
    private static readonly Color Inaktiv = Color.FromArgb("#64748b");
    private static readonly Color Weiss = Colors.White;
    private bool _laedt;

    // Radius (km) der „Umgebung offline laden"-Funktion rund um den aktuellen Standort.
    // Hält den in der Lade-Logik verwendeten Wert an einer Stelle (Hinweistext „~3 km"
    // steht in der Lokalisierung, Texte.cs → einst_offline_info).
    private const double UmgebungRadiusKm = 3;

    public EinstellungenPage()
    {
        InitializeComponent();
        VersionLabel.Text = L.T("version_label", AppInfo.Current.VersionString, AppInfo.Current.BuildString);
        ServerLabel.Text = L.T("server_label", AppConfig.ApiBase);

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
        NaviSpracheLabelSetzen();
        FarbmodusMarkieren();
        // Karte
        KartenmodusMarkieren();
        OverlaySchalter.IsToggled = Einst.Wanderwege;
        DrehungSchalter.IsToggled = Einst.ManuelleDrehung;
        _laedt = false;

        TabWechseln("allgemein");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Bei Sprachwechsel die imperativ gesetzten Texte (Sprach-/Einheiten-/Konto-/Cache-Labels) neu rendern.
        // Symmetrisch zu OnDisappearing abonnieren (die Seite wird von der Shell zwischengespeichert, der
        // Konstruktor läuft nur einmal) – so bleibt nach Verlassen+Zurückkehren genau EIN Abo bestehen.
        L.Geaendert -= SpracheAngewendet;
        L.Geaendert += SpracheAngewendet;
        CacheGroesseAnzeigen();
        StandardPunktAnzeigen();
        try { await Auth.AktualisiereAsync(); } catch (Exception ex) { Debug.WriteLine(ex); }
        KontoAnzeigen();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        L.Geaendert -= SpracheAngewendet;   // Sprachwechsel-Abo lösen (kein Leak/Mehrfach-Aufruf)
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
        // App-Sprache umschalten: Lokalisierung wechselt + persistiert + steuert NUR die Oberfläche.
        Lokalisierung.Instanz.Wechsle(Einst.Sprache == "de" ? "en" : "de");
        SpracheLabelSetzen();
        // Solange der Nutzer die Navi-Sprache nicht explizit gewählt hat, folgt sie der App-Sprache
        // (Einst.NaviSprache liefert dann den App-Sprachwert) → Anzeige mitziehen.
        NaviSpracheLabelSetzen();
    }

    // App-Sprache (Oberfläche) – steuert die gesamte Bedienoberfläche.
    private void SpracheLabelSetzen() =>
        SpracheWert.Text = Einst.Sprache == "en" ? L.T("sprache_english") : L.T("sprache_deutsch");

    private void OnNaviSpracheWechseln(object? sender, TappedEventArgs e)
    {
        // NUR die Navigationssprache (Sprachansagen + Routing-Anweisungen) umschalten. Bewusst KEIN
        // Lokalisierung.Wechsle und KEIN Einst.Sprache: die App-Oberfläche bleibt unverändert.
        Einst.NaviSprache = Einst.NaviSprache == "de" ? "en" : "de";
        NaviSpracheLabelSetzen();
    }

    // Navigationssprache (Ansagen + Routing-Anweisungen) – unabhängig von der Oberfläche.
    private void NaviSpracheLabelSetzen() =>
        NaviSpracheWert.Text = Einst.NaviSprache == "en" ? L.T("sprache_english") : L.T("sprache_deutsch");

    private void OnEinheitenWechseln(object? sender, TappedEventArgs e)
    {
        Einst.Einheiten = Einst.Einheiten == "metrisch" ? "imperial" : "metrisch";
        EinheitenLabelSetzen();
    }

    private void EinheitenLabelSetzen() =>
        EinheitenWert.Text = Einst.Einheiten == "imperial" ? L.T("einheiten_imperial") : L.T("einheiten_metrisch");

    // Bei Laufzeit-Sprachwechsel alle imperativ gesetzten Texte neu rendern (die {loc:Translate}-Bindings
    // aktualisieren sich selbst). Auf dem UI-Thread, da das Event aus beliebigem Kontext kommen kann.
    private void SpracheAngewendet() => MainThread.BeginInvokeOnMainThread(() =>
    {
        VersionLabel.Text = L.T("version_label", AppInfo.Current.VersionString, AppInfo.Current.BuildString);
        ServerLabel.Text = L.T("server_label", AppConfig.ApiBase);
        SpracheLabelSetzen();
        NaviSpracheLabelSetzen();
        EinheitenLabelSetzen();
        CacheGroesseAnzeigen();
        StandardPunktAnzeigen();
        KontoAnzeigen();
    });

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

    private void OnDrehung(object? sender, ToggledEventArgs e) { if (!_laedt) Einst.ManuelleDrehung = e.Value; }

    private void CacheGroesseAnzeigen()
    {
        try
        {
            long bytes = 0; int dateien = 0;
            if (Directory.Exists(OfflineKarte.CacheDir))
                foreach (var f in Directory.EnumerateFiles(OfflineKarte.CacheDir, "*", SearchOption.AllDirectories))
                { bytes += new FileInfo(f).Length; dateien++; }
            CacheLabel.Text = dateien == 0 ? L.T("cache_leer")
                : L.T("cache_info", dateien, bytes / 1024.0 / 1024.0);
        }
        catch (Exception ex) { Debug.WriteLine(ex); CacheLabel.Text = L.T("cache_strich"); }
        PaketeAnzeigen();   // gespeicherte Offline-Pakete mit aktualisieren
    }

    // Liste der gespeicherten Offline-Pakete (Regionen/Touren) füllen.
    private void PaketeAnzeigen()
    {
        if (OfflinePaketeBox == null) return;
        OfflinePaketeBox.Children.Clear();
        var pakete = OfflinePakete.Laden();
        if (pakete.Count == 0)
        {
            OfflinePaketeBox.Children.Add(new Label
            {
                Text = L.T("offline_pakete_leer"), FontSize = 12,
                TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#94a3b8"),
            });
            return;
        }
        foreach (var p in pakete)
        {
            double mb = p.Bytes / 1024.0 / 1024.0;
            var texte = new VerticalStackLayout { Spacing = 1 };
            texte.Add(new Label
            {
                Text = (p.IstTour ? "🥾 " : "🗺 ") + p.Name, FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#0f172a"),
            });
            texte.Add(new Label
            {
                Text = L.T("offline_paket_detail", p.Kacheln, p.Fotos) + $" · {mb:0} MB",
                FontSize = 11, TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#64748b"),
            });
            OfflinePaketeBox.Children.Add(new Border
            {
                BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#f8fafc"),
                StrokeThickness = 0, Padding = new Thickness(10, 8),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Content = texte,
            });
        }
    }

    // Lädt die Kacheln rund um den aktuellen Standort (~3 km) offline vor. In den Einstellungen
    // gibt es keinen Karten-Viewport → Region kommt aus der GPS-Position. Budget wie auf der Navi-Seite.
    private async void OnOfflineLaden(object? sender, EventArgs e)
    {
        if (_laedtOffline) return;
        int budget = Auth.AlleFunktionen ? int.MaxValue : (Auth.Premium ? 3 : 0) + Auth.OfflineGekauft;
        if (Einst.OfflineAnzahl >= budget)
        {
            bool hin = await DisplayAlert(L.T("offline_titel"), L.T("offline_premium_text"),
                L.T("offline_zum_konto"), L.T("abbrechen"));
            if (hin) await Shell.Current.GoToAsync("//konto");
            return;
        }
        Location? loc = null;
        try
        {
            loc = await Geolocation.GetLastKnownLocationAsync()
                  ?? await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        if (loc == null)
        {
            await DisplayAlert(L.T("offline_laden_titel"), L.T("offline_kein_standort"), L.T("ok"));
            return;
        }

        _laedtOffline = true;
        OfflineLadenBtn.IsEnabled = false;
        var (x, y) = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
        double r = UmgebungRadiusKm * 1000.0 / Math.Max(0.1, Math.Cos(loc.Latitude * Math.PI / 180));   // Radius (km→m) in Web-Mercator-Einheiten
        var bereich = new MRect(x - r, y - r, x + r, y + r);
        var quelle = MapQuellen.Quelle(Einst.Karte);
        var prog = new Progress<(int done, int total)>(p =>
            MainThread.BeginInvokeOnMainThread(() => CacheLabel.Text = L.T("offline_fortschritt", p.done, p.total)));
        try
        {
            int n = await Task.Run(() => OfflineKarte.DownloadAsync(quelle, bereich, 13, 15, 400, prog));
            if (n > 0) Einst.OfflineAnzahl++;
            await DisplayAlert(L.T("offline_titel"), L.T("offline_gespeichert", n), L.T("ok"));
        }
        catch (Exception ex) { Debug.WriteLine(ex); await DisplayAlert(L.T("offline_laden_titel"), L.T("offline_fehler"), L.T("ok")); }
        finally
        {
            _laedtOffline = false;
            OfflineLadenBtn.IsEnabled = true;
            CacheGroesseAnzeigen();
        }
    }

    private bool _laedtOffline;

    // Eine vordefinierte Region (Server-Liste, mit bbox) offline speichern: Kacheln (z10–14, gedeckelt)
    // + die verkleinerten Fotos der Region. Zeigt vorher eine Größen-Schätzung zur Bestätigung.
    private async void OnRegionLaden(object? sender, EventArgs e)
    {
        if (_laedtOffline) return;
        int budget = Auth.AlleFunktionen ? int.MaxValue : (Auth.Premium ? 3 : 0) + Auth.OfflineGekauft;
        if (Einst.OfflineAnzahl >= budget)
        {
            bool hin = await DisplayAlert(L.T("offline_titel"), L.T("offline_premium_text"), L.T("offline_zum_konto"), L.T("abbrechen"));
            if (hin) await Shell.Current.GoToAsync("//konto");
            return;
        }
        RegionLadenBtn.IsEnabled = false;
        try
        {
            var regionen = await RegionenService.LadeAsync();
            if (regionen.Count == 0) { await DisplayAlert(L.T("einst_region_laden_btn"), L.T("region_keine"), L.T("ok")); return; }
            var namen = regionen.Select(r => $"{r.Gruppe} – {r.Name}").ToArray();
            string wahl = await DisplayActionSheet(L.T("region_waehlen_titel"), L.T("abbrechen"), null, namen);
            if (string.IsNullOrEmpty(wahl) || wahl == L.T("abbrechen")) return;
            int idx = Array.IndexOf(namen, wahl);
            if (idx < 0) return;
            var region = regionen[idx];

            List<FotoPunkt> fotos;
            try { fotos = await FotoService.LadeAsync(); } catch (Exception ex) { Debug.WriteLine(ex); fotos = new(); }

            var schaetz = OfflineManager.SchaetzeRegion(region, fotos);
            double mb = schaetz.Bytes / 1024.0 / 1024.0;
            bool los = await DisplayAlert(region.Name,
                L.T("region_schaetzung", schaetz.Kacheln, schaetz.Fotos, mb.ToString("0")),
                L.T("region_laden_btn"), L.T("abbrechen"));
            if (!los) return;

            _laedtOffline = true;
            var quelle = MapQuellen.Quelle(Einst.Karte);
            var prog = new Progress<(int done, int total, string phase)>(p =>
                MainThread.BeginInvokeOnMainThread(() =>
                    CacheLabel.Text = L.T(p.phase == "fotos" ? "region_fortschritt_fotos" : "region_fortschritt_kacheln", p.done, p.total)));
            var erg = await Task.Run(() => OfflineManager.LadeRegionAsync(region, quelle, fotos, prog));
            if (erg.Ok)
            {
                Einst.OfflineAnzahl++;
                await DisplayAlert(L.T("offline_titel"),
                    L.T("region_fertig", region.Name, erg.Kacheln, erg.Fotos, (erg.Bytes / 1024.0 / 1024.0).ToString("0")), L.T("ok"));
            }
            else await DisplayAlert(L.T("offline_laden_titel"), L.T("offline_fehler"), L.T("ok"));
        }
        catch (Exception ex) { Debug.WriteLine(ex); await DisplayAlert(L.T("offline_laden_titel"), L.T("offline_fehler"), L.T("ok")); }
        finally
        {
            _laedtOffline = false;
            RegionLadenBtn.IsEnabled = true;
            CacheGroesseAnzeigen();   // aktualisiert Größe + Pakete-Liste
        }
    }

    // ---- Standard-Punkt (Bezugspunkt für Entfernungsanzeige) ----
    private void StandardPunktAnzeigen()
    {
        StandardPunktLabel.Text = L.T("standardpunkt_label",
            Standort.StandardnameAnzeige(Einst.StandardName), Einst.StandardLat, Einst.StandardLng);
    }

    private async void OnStandardAufPosition(object? sender, EventArgs e)
    {
        StandardAufPositionBtn.IsEnabled = false;
        try
        {
            // Berechtigung AKTIV anfordern (nicht still verschlucken): erst prüfen, bei Bedarf anfragen.
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert(L.T("standardpunkt_titel"), L.T("standort_keine_berechtigung"), L.T("ok"));
                return;
            }

            // Erst die zuletzt bekannte Position (sofort), sonst frischen Fix anfordern. Medium ist
            // Netzwerk-/Fused-fähig (NICHT nur GPS) + längeres Timeout → liefert auch drinnen einen Wert.
            var loc = await Geolocation.GetLastKnownLocationAsync()
                      ?? await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(20)));
            if (loc != null)
            {
                Einst.StandardLat = loc.Latitude;
                Einst.StandardLng = loc.Longitude;
                Einst.StandardName = "eigener Punkt";
                StandardPunktAnzeigen();
                await DisplayAlert(L.T("standardpunkt_titel"), L.T("standardpunkt_gesetzt"), L.T("ok"));
            }
            else await DisplayAlert(L.T("standardpunkt_titel"), L.T("standardpunkt_kein_standort"), L.T("ok"));
        }
        catch (FeatureNotEnabledException ex)   // Ortungsdienste am Gerät ausgeschaltet
        {
            Debug.WriteLine(ex);
            await DisplayAlert(L.T("standardpunkt_titel"), L.T("standort_gps_aus"), L.T("ok"));
        }
        catch (PermissionException ex)          // Berechtigung fehlt/verweigert
        {
            Debug.WriteLine(ex);
            await DisplayAlert(L.T("standardpunkt_titel"), L.T("standort_keine_berechtigung"), L.T("ok"));
        }
        catch (Exception ex)                    // alles andere: Fehler NICHT mehr still schlucken
        {
            Debug.WriteLine(ex);
            await DisplayAlert(L.T("standardpunkt_titel"), L.T("standort_fehler"), L.T("ok"));
        }
        finally { StandardAufPositionBtn.IsEnabled = true; }
    }

    private void OnStandardZuruecksetzen(object? sender, EventArgs e)
    {
        Einst.StandardLat = Einst.BrandenburgerTorLat;
        Einst.StandardLng = Einst.BrandenburgerTorLng;
        Einst.StandardName = "Berlin Mitte";
        StandardPunktAnzeigen();
    }

    private async void OnCacheLeeren(object? sender, EventArgs e)
    {
        bool ja = await DisplayAlert(L.T("cache_leeren_titel"), L.T("cache_leeren_frage"), L.T("cache_leeren_btn"), L.T("abbrechen"));
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
        // Vollzugriff (Premium ODER Admin) → „Premium"-Status; Admin/Alle-Funktionen → „alle Funktionen".
        StatusTitel.Text = Auth.Vollzugriff
            ? (Auth.IstAdmin ? L.T("konto_premium_alle") : L.T("konto_premium"))
            : (angemeldet ? L.T("konto_titel") : L.T("konto_testkonto"));
        StatusZeile.Text = angemeldet ? L.T("konto_status_angemeldet", Auth.Email) : L.T("konto_status_anonym");
        KontingentZeile.Text = Auth.Vollzugriff
            ? L.T("konto_kontingent_unbegrenzt")
            : L.T("konto_kontingent", Auth.RoutenHeute, Auth.GratisProTag, Auth.CreditsRouten, Auth.OfflineGekauft);

        AuthCard.IsVisible = !angemeldet;
        AbmeldenBtn.IsVisible = angemeldet;
        PremiumBtn.IsVisible = !Auth.Vollzugriff;   // „Premium freischalten" NUR ohne Vollzugriff
        AuthTitel.Text = Auth.Anonym ? L.T("auth_titel_anlegen") : L.T("auth_titel_anmelden");

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
        bool ja = await DisplayAlert(L.T("logout_titel"), L.T("abmelden_frage"), L.T("logout_titel"), L.T("abbrechen"));
        if (!ja) return;
        await Auth.AbmeldenAsync();
        EmailFeld.Text = "";
        KontoAnzeigen();
    }

    private async void OnKaufen(object? sender, EventArgs e)
    {
        bool web = await DisplayAlert(L.T("premium_titel"), L.T("premium_text"),
            L.T("premium_website"), L.T("schliessen"));
        if (web) { try { await Launcher.OpenAsync("https://spin1more.com/konto/"); } catch (Exception ex) { Debug.WriteLine(ex); } }
    }
}
