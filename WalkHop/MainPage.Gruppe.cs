using Mapsui;
using Mapsui.Layers;

namespace WalkHop;

public partial class MainPage
{
    // ---- Gruppen-Position (Live-Standort teilen) ---------------------------
    // Beitreten/Verlassen/Teilen sitzt jetzt in Einstellungen → Tab „Gruppe" (kein Karten-Knopf mehr).
    // Diese Seite gleicht den aktiven Code beim Erscheinen mit den Einstellungen ab (OnAppearing),
    // pollt die Mitglieder-Positionen und zeichnet sie als Marker; die eigene Position sendet die
    // GPS-Schleife (MainPage.Gps.cs).
    private static string GruppenAnzeigename()
    {
        if (!string.IsNullOrWhiteSpace(Einst.GruppenName)) return Einst.GruppenName.Trim();
        if (!string.IsNullOrWhiteSpace(Auth.Name)) return Auth.Name.Trim();
        return L.T("gruppe_default_name");
    }

    // Ohne Karten-Knopf: bei inaktiver Gruppe nur die Mitglieder-Marker entfernen.
    private void GruppeIconAktualisieren()
    {
        if (_gruppeCode.Length > 0) return;
        _gruppeLayer.Features = new List<IFeature>();
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
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
    // (eigener Beam) auslassen.
    private void GruppeZeichnen(List<GruppenMitglied> mitglieder)
    {
        var (feats, _) = KarteHelfer.GruppenMarker(mitglieder, GruppenAnzeigename());
        _gruppeLayer.Features = feats;
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
    }
}
