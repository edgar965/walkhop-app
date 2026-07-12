namespace WalkHop;

/// <summary>Reine, testbare GPS-Entscheidungen rund um Rekalibrierungs-Sprünge. Wenn sich der Standort
/// SCHLAGARTIG um viele Meter verschiebt (typisch: das GPS rekalibriert nach einem Kaltstart/Multipath),
/// darf das NICHT wie echte Bewegung behandelt werden:
///  • Die „gewanderte Route" (Breadcrumb) darf keine Linie über den Sprung ziehen (die graue Linie).
///  • Eine Route in der VORSCHAU (berechnet, aber noch nicht gestartet) muss vom korrigierten Standort
///    neu berechnet werden – sonst startet sie weiter am falschen alten Punkt.
/// Bewusst MAUI-frei → im Unit-Test-Projekt geprüft.</summary>
public static class GpsFilter
{
    /// <summary>Sprung (m) zwischen zwei aufeinanderfolgenden Fixes, ab dem eine Rekalibrierung/Lücke
    /// angenommen wird (statt echter, zusammenhängender Bewegung).</summary>
    public const double SpurTrennMeter = 150;

    /// <summary>Verschiebung (m) des Standorts in der Routen-Vorschau gegenüber dem Berechnungs-Start,
    /// ab der die Route neu vom korrigierten Standort berechnet wird.</summary>
    public const double VorschauNeuMeter = 80;

    /// <summary>Große Positionssprünge (Rekalibrierung/Lücke) NICHT als Breadcrumb-Linie verbinden.</summary>
    public static bool SpurTrennen(double sprungMeter) => sprungMeter > SpurTrennMeter;

    /// <summary>Standort in der Vorschau so weit korrigiert, dass die Route neu berechnet werden sollte?</summary>
    public static bool VorschauNeuBerechnen(double verschiebungMeter) => verschiebungMeter > VorschauNeuMeter;
}
