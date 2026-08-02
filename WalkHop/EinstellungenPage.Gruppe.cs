using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices.Sensors;

namespace WalkHop;

/// <summary>Einstellungen → Tab „Gruppe": Live-Gruppe erstellen/beitreten/teilen/verlassen.
/// Der frühere 👥-Knopf auf den Karten-Seiten entfällt; die gesamte Bedienung sitzt hier.
/// Zustand/Polling laufen über die geteilte <see cref="GruppeLive"/>-Komponente, sodass beide
/// Karten-Seiten die Mitglieder weiterhin als Marker zeigen.</summary>
public partial class EinstellungenPage
{
    // GruppeLive.Geaendert kann aus beliebigem Kontext kommen → auf den UI-Thread marshallen.
    private void GruppeGeaendert() => MainThread.BeginInvokeOnMainThread(GruppeAnzeigen);

    // Status + Knopf-Sichtbarkeit an den aktuellen Gruppen-Zustand anpassen.
    private void GruppeAnzeigen()
    {
        bool aktiv = GruppeLive.Aktiv;
        GruppeStatusLabel.Text = aktiv ? L.T("einst_gruppe_aktiv", GruppeLive.Code) : L.T("einst_gruppe_inaktiv");
        GruppeErstellenBtn.IsVisible = !aktiv;
        GruppeBeitretenBtn.IsVisible = !aktiv;
        GruppeTeilenBtn.IsVisible = aktiv;
        GruppeNameBtn.IsVisible = aktiv;
        GruppeVerlassenBtn.IsVisible = aktiv;
    }

    private async void OnGruppeErstellen(object? sender, EventArgs e)
    {
        string vorgabe = GruppeLive.Anzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("gruppe_erstellen_btn"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (name == null) return;   // abgebrochen
        GruppeLive.Beitreten(GruppeLive.NeuerCode(), string.IsNullOrWhiteSpace(name) ? vorgabe : name.Trim());
        GruppeAnzeigen();
        _ = EigenePositionSendenAsync();   // sofort die eigene Position teilen → Eingeladene sehen gleich einen Marker
        await GruppeTeilen();   // direkt den Einladungs-Link teilen
    }

    private async void OnGruppeBeitreten(object? sender, EventArgs e)
    {
        string code = await DisplayPromptAsync(L.T("gruppe_titel"), L.T("gruppe_code_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), L.T("gruppe_code_placeholder"), maxLength: 32);
        code = GruppeService.CodeSaeubern(code);
        if (code.Length == 0) return;   // abgebrochen oder leer
        string vorgabe = GruppeLive.Anzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (name == null) return;   // abgebrochen
        GruppeLive.Beitreten(code, string.IsNullOrWhiteSpace(name) ? vorgabe : name.Trim());
        GruppeAnzeigen();
        _ = EigenePositionSendenAsync();   // sofort die eigene Position teilen → die anderen sehen mich gleich
    }

    // Eigene (zuletzt bekannte oder frische) Position einmalig in die Gruppe senden. In den Einstellungen
    // gibt es keinen Live-GPS-Beam, daher hier aktiv holen – sonst bliebe die Karte der anderen leer, bis
    // man selbst auf eine Kartenseite wechselt.
    private static async Task EigenePositionSendenAsync()
    {
        if (!GruppeLive.Aktiv) return;
        try
        {
            var loc = await Geolocation.GetLastKnownLocationAsync()
                      ?? await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
            if (loc != null) GruppeLive.Sende(loc.Latitude, loc.Longitude);
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private async void OnGruppeTeilen(object? sender, EventArgs e) => await GruppeTeilen();

    private async Task GruppeTeilen()
    {
        if (!GruppeLive.Aktiv) return;
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = L.T("gruppe_titel"),
                Text = L.T("gruppe_teilen_text", GruppeLive.TeilenLink(GruppeLive.Code)),
            });
        }
        catch (Exception ex) { Debug.WriteLine(ex); Meldung.Fehler("Einladung teilen", ex); }
    }

    private async void OnGruppeNameAendern(object? sender, EventArgs e)
    {
        string vorgabe = GruppeLive.Anzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("ok"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (string.IsNullOrWhiteSpace(name)) return;
        Einst.GruppenName = name.Trim();
        GruppeAnzeigen();
    }

    private void OnGruppeVerlassen(object? sender, EventArgs e)
    {
        GruppeLive.Verlassen();
        GruppeAnzeigen();
    }
}
