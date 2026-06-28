using System.Diagnostics;
using BruTile.Web;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using NetTopologySuite.Geometries;

namespace WalkHop;

public partial class MainPage
{
    // ---- Gruppen-Position (Live-Standort teilen) ---------------------------
    private static string GruppenAnzeigename()
    {
        if (!string.IsNullOrWhiteSpace(Einst.GruppenName)) return Einst.GruppenName.Trim();
        if (!string.IsNullOrWhiteSpace(Auth.Name)) return Auth.Name.Trim();
        return L.T("gruppe_default_name");
    }

    private void GruppeIconAktualisieren()
    {
        bool aktiv = _gruppeCode.Length > 0;
        if (GruppeBorder != null) GruppeBorder.BackgroundColor = aktiv ? Microsoft.Maui.Graphics.Color.FromArgb("#16a34a") : Weiss;
        if (GruppeBadge != null) GruppeBadge.IsVisible = aktiv;
        if (aktiv && GruppeBadgeText != null && string.IsNullOrEmpty(GruppeBadgeText.Text)) GruppeBadgeText.Text = "0";
    }

    private void GruppeStart()
    {
        if (_gruppeTimer == null)
        {
            _gruppeTimer = Dispatcher.CreateTimer();
            _gruppeTimer.Interval = TimeSpan.FromSeconds(5);   // Mitglieder-Positionen alle 5 s nachladen
            _gruppeTimer.IsRepeating = true;
            _gruppeTimer.Tick += (_, __) => _ = GruppeAktualisieren();
        }
        _gruppeTimer.Stop();
        _gruppeTimer.Start();
        _ = GruppeAktualisieren();   // sofort einmal laden
    }

    private void GruppeStop() => _gruppeTimer?.Stop();   // Code bleibt erhalten (Fortsetzung bei OnAppearing)

    private async Task GruppeAktualisieren()
    {
        if (_gruppeCode.Length == 0) return;
        var mitglieder = await GruppeService.HoleAsync(_gruppeCode);
        if (!_seiteLebt || _gruppeCode.Length == 0) return;   // Seite verlassen / Gruppe zwischenzeitlich verlassen
        GruppeZeichnen(mitglieder);
    }

    // Mitglieder als beschriftete Marker zeichnen (frisch = orange, veraltet = grau); mich selbst
    // (eigener Beam) auslassen. Badge zeigt die Zahl der sichtbaren Mitreisenden.
    private void GruppeZeichnen(List<GruppenMitglied> mitglieder)
    {
        var (feats, andere) = KarteHelfer.GruppenMarker(mitglieder, GruppenAnzeigename());
        _gruppeLayer.Features = feats;
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
        if (GruppeBadgeText != null) GruppeBadgeText.Text = andere.ToString();
        if (GruppeBadge != null) GruppeBadge.IsVisible = _gruppeCode.Length > 0;
    }

    private async void OnGruppe(object? sender, EventArgs e)
    {
        if (_gruppeCode.Length == 0) { await GruppeBeitretenDialog(); return; }
        string verlassen = L.T("gruppe_verlassen"), nameAendern = L.T("gruppe_name_aendern");
        string wahl = await DisplayActionSheet(L.T("gruppe_aktiv_titel", _gruppeCode), L.T("abbrechen"), verlassen, nameAendern);
        if (wahl == verlassen) GruppeVerlassen();
        else if (wahl == nameAendern) await GruppeNameDialog();
    }

    private async Task GruppeBeitretenDialog()
    {
        string code = await DisplayPromptAsync(L.T("gruppe_titel"), L.T("gruppe_code_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), L.T("gruppe_code_placeholder"), maxLength: 32);
        code = GruppeService.CodeSaeubern(code);
        if (code.Length == 0) return;   // abgebrochen oder leer
        string vorgabe = GruppenAnzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("gruppe_beitreten_btn"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (name == null) return;   // abgebrochen
        name = string.IsNullOrWhiteSpace(name) ? vorgabe : name.Trim();
        Einst.GruppenName = name;
        _gruppeCode = code; Einst.GruppenCode = code;
        GruppeIconAktualisieren();
        GruppeStart();
        if (_letzteGeo is { } g)   // sofort die eigene Position teilen
        {
            _letztGruppeSendeMs = Environment.TickCount64;
            _ = GruppeService.SendePositionAsync(_gruppeCode, name, g.lat, g.lon);
        }
        Status(L.T("gruppe_beigetreten", code), autoAus: true);
    }

    private void GruppeVerlassen()
    {
        _gruppeCode = ""; Einst.GruppenCode = "";
        GruppeStop();
        _gruppeLayer.Features = new List<IFeature>();
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
        GruppeIconAktualisieren();
        Status(L.T("gruppe_verlassen_ok"), autoAus: true);
    }

    private async Task GruppeNameDialog()
    {
        string vorgabe = GruppenAnzeigename();
        string name = await DisplayPromptAsync(L.T("gruppe_name_titel"), L.T("gruppe_name_msg"),
            L.T("ok"), L.T("abbrechen"), null, maxLength: 40, initialValue: vorgabe);
        if (string.IsNullOrWhiteSpace(name)) return;
        Einst.GruppenName = name.Trim();
        _ = GruppeAktualisieren();   // Selbst-Filter/Marker neu auswerten
    }
}
