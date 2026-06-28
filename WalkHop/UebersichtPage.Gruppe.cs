using System.Diagnostics;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices.Sensors;
using NetTopologySuite.Geometries;

namespace WalkHop;

public partial class UebersichtPage
{
    // ---- Gruppe (Live-Position teilen) ----------------------------------------
    private async void OnGruppe(object? sender, EventArgs e)
    {
        if (!GruppeLive.Aktiv)
        {
            string erstellen = L.T("gruppe_erstellen"), beitreten = L.T("gruppe_beitreten_btn");
            string wahl = await DisplayActionSheet(L.T("gruppe_titel"), L.T("abbrechen"), null, erstellen, beitreten);
            if (wahl == erstellen) await GruppeErstellen();
            else if (wahl == beitreten) await GruppeBeitreten();
            return;
        }
        string teilen = L.T("gruppe_teilen"), verlassen = L.T("gruppe_verlassen"), nameA = L.T("gruppe_name_aendern");
        string w = await DisplayActionSheet(L.T("gruppe_aktiv_titel", GruppeLive.Code), L.T("abbrechen"), verlassen, teilen, nameA);
        if (w == teilen) await GruppeTeilen();
        else if (w == verlassen) { GruppeLive.Verlassen(); StatusKurz(L.T("gruppe_verlassen_ok"), 3); }
        else if (w == nameA) await GruppeNameAendern();
    }

    private async Task GruppeErstellen()
    {
        string vorgabe = GruppeLive.Anzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("gruppe_erstellen_btn"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (name == null) return;
        GruppeLive.Beitreten(GruppeLive.NeuerCode(), string.IsNullOrWhiteSpace(name) ? vorgabe : name.Trim());
        if (_letzteGeo is { } g) GruppeLive.Sende(g.lat, g.lon);
        await GruppeTeilen();   // direkt den Einladungs-Link teilen
    }

    private async Task GruppeBeitreten()
    {
        string code = await DisplayPromptAsync(L.T("gruppe_titel"), L.T("gruppe_code_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), L.T("gruppe_code_placeholder"), maxLength: 32);
        code = GruppeService.CodeSaeubern(code);
        if (code.Length == 0) return;
        string vorgabe = GruppeLive.Anzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (name == null) return;
        GruppeLive.Beitreten(code, string.IsNullOrWhiteSpace(name) ? vorgabe : name.Trim());
        if (_letzteGeo is { } g) GruppeLive.Sende(g.lat, g.lon);
        StatusKurz(L.T("gruppe_beigetreten", code), 3);
    }

    private async Task GruppeTeilen()
    {
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

    private async Task GruppeNameAendern()
    {
        string vorgabe = GruppeLive.Anzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("ok"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (string.IsNullOrWhiteSpace(name)) return;
        Einst.GruppenName = name.Trim();
    }

    // Mitglieder als beschriftete Marker zeichnen (mich selbst auslassen; frisch=orange, alt=grau).
    private void GruppeMarkerZeichnen(List<GruppenMitglied> mitglieder)
    {
        var (feats, andere) = KarteHelfer.GruppenMarker(mitglieder, GruppeLive.Anzeigename());
        _gruppeLayer.Features = feats;
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
        if (GruppeBadgeText != null) GruppeBadgeText.Text = andere.ToString();
        if (GruppeBadge != null) GruppeBadge.IsVisible = GruppeLive.Aktiv;
    }

    private void GruppeIconAktualisieren()
    {
        bool aktiv = GruppeLive.Aktiv;
        if (GruppeBorder != null)
            GruppeBorder.BackgroundColor = aktiv ? Microsoft.Maui.Graphics.Color.FromArgb("#16a34a") : Microsoft.Maui.Graphics.Colors.White;
        if (GruppeBadge != null) GruppeBadge.IsVisible = aktiv;
        if (aktiv && GruppeBadgeText != null && string.IsNullOrEmpty(GruppeBadgeText.Text)) GruppeBadgeText.Text = "0";
        if (!aktiv)
        {
            _gruppeLayer.Features = new List<IFeature>();
            _gruppeLayer.DataHasChanged();
            _map.RefreshGraphics();
        }
    }
}
