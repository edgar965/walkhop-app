using Mapsui;
using Mapsui.Layers;

namespace WalkHop;

public partial class UebersichtPage
{
    // ---- Gruppe (Live-Position teilen) ----------------------------------------
    // Beitreten/Verlassen/Teilen sitzt jetzt in Einstellungen → Tab „Gruppe" (kein Karten-Knopf mehr).
    // Diese Seite ist nur noch KONSUMENT der geteilten GruppeLive-Komponente: sie zeichnet die
    // Mitglieder als Marker und räumt sie beim Verlassen wieder ab.

    // Mitglieder als beschriftete Marker zeichnen (mich selbst auslassen; frisch=orange, alt=grau).
    private void GruppeMarkerZeichnen(List<GruppenMitglied> mitglieder)
    {
        var (feats, _) = KarteHelfer.GruppenMarker(mitglieder, GruppeLive.Anzeigename());
        _gruppeLayer.Features = feats;
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    // Reagiert auf Beitritt/Verlassen (GruppeLive.Geaendert): bei inaktiver Gruppe die Marker entfernen.
    private void GruppeIconAktualisieren()
    {
        if (GruppeLive.Aktiv) return;
        _gruppeLayer.Features = new List<IFeature>();
        _gruppeLayer.DataHasChanged();
        _map.RefreshGraphics();
    }
}
