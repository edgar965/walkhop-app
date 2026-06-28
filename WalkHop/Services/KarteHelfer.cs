using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace WalkHop;

/// <summary>
/// Geteilte, rein darstellungs-/Mapsui-nahe Helfer für die beiden Kartenseiten
/// (MainPage = Navigation, UebersichtPage = Übersicht). Enthält NUR Logik, die in
/// beiden Seiten funktional IDENTISCH war (modulo Feld-/Variablennamen) – z. B. den
/// Standort-Beam, das Zeichnen des Positions-Symbols, die Gruppen-Marker und die
/// Treffer-Suche auf Tour-Routen. Seiten-spezifisches Verhalten (andere Layer-
/// Reihenfolge, Navigations-/Vorschau-Sonderfälle, Foto-Ebene) bleibt in den Seiten.
/// Bewusst NICHT im Test-Projekt referenziert (Mapsui-Abhängigkeit, nur App-intern).
/// </summary>
public static class KarteHelfer
{
    // ---- Reine Winkel-/Zoom-Helfer -----------------------------------------

    /// <summary>Kleinste Winkeldifferenz (0–180°) zwischen zwei Kursen in Grad –
    /// robust über den 360°-Übergang. Für die Redraw-/Dreh-Drosselung des Kompasses.</summary>
    public static double Winkeldifferenz(double a, double b)
        => Math.Abs(((a - b + 540) % 360) - 180);

    /// <summary>True, wenn die neue Auflösung eine WESENTLICHE Zoom-Änderung (&gt;0,2 %)
    /// gegenüber der zuletzt behandelten ist – Pan/Zentrieren (gleiche Auflösung) löst
    /// dadurch die Vektor-Glättung nicht aus. <paramref name="res"/> &lt;= 0 → false.</summary>
    public static bool ZoomWesentlich(double res, double letzteZoomRes)
        => res > 0 && !(letzteZoomRes > 0 && Math.Abs(res - letzteZoomRes) / letzteZoomRes < 0.002);

    /// <summary>Tour-Farbe aus Hex; bei ungültigem Wert das Teal-Standardgrün (#0d9488).</summary>
    public static Mapsui.Styles.Color Farbe(string hex)
    {
        try { return Mapsui.Styles.Color.FromString(hex); }
        catch { return Mapsui.Styles.Color.FromString("#0d9488"); }
    }

    // ---- Standort-Beam (Google-Stil) ---------------------------------------
    // Einmalig (prozessweit) gerenderte Bitmap: dunkelgrüner Punkt + breiter Trichter
    // (radialer Alpha-Verlauf ab der Position, dunkelgrün → transparent, ohne Ring/Kontur).
    // Beide Seiten nutzen exakt dieselbe Grafik → genau EINE Registrierung statt zweier
    // pixelgleicher Bitmaps. Bildschirm-konstant verwendet, dreht mit Kompass/GPS-Kurs.
    private static int _beamBitmapId = -1;
    private static readonly object _beamLock = new();

    public static int BeamBitmapId()
    {
        lock (_beamLock)
        {
            if (_beamBitmapId >= 0) return _beamBitmapId;
            const int g = 240;                                   // Bitmapgröße (px); per SymbolScale verkleinert
            const float c = g / 2f;                              // Mitte = Standortpunkt = Drehzentrum
            const float radius = 104f;                           // Beam-Länge
            const float halb = 39f * (float)(Math.PI / 180);     // halber Öffnungswinkel
            var dunkel = new SkiaSharp.SKColor(22, 101, 52);     // Dunkelgrün (#166534): Punkt UND Trichter-Basis
            using var bitmap = new SkiaSharp.SKBitmap(g, g);
            using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            {
                canvas.Clear(SkiaSharp.SKColors.Transparent);
                // Trichter: Sektor nach oben, beginnt DIREKT an der Position (Verlauf ab 0, kein Loch),
                // dunkelgrün an der Position → nach außen transparent. Kein weißer Ring.
                using var shader = SkiaSharp.SKShader.CreateRadialGradient(
                    new SkiaSharp.SKPoint(c, c), radius,
                    new[] { dunkel.WithAlpha(210), dunkel.WithAlpha(0) },
                    new[] { 0f, 1f }, SkiaSharp.SKShaderTileMode.Clamp);
                using var beam = new SkiaSharp.SKPaint { Shader = shader, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
                using var pfad = new SkiaSharp.SKPath();
                pfad.MoveTo(c, c);
                for (int i = 0; i <= 28; i++)
                {
                    float ang = -halb + 2 * halb * i / 28f;       // um die Aufwärtsachse (−y)
                    pfad.LineTo(c + radius * (float)Math.Sin(ang), c - radius * (float)Math.Cos(ang));
                }
                pfad.Close();
                canvas.DrawPath(pfad, beam);
                // Standortpunkt: nur dunkelgrün, KEIN weißer Ring.
                using var fuell = new SkiaSharp.SKPaint { Color = dunkel, IsAntialias = true };
                canvas.DrawCircle(c, c, 15f, fuell);
            }
            using var img = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var png = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            _beamBitmapId = Mapsui.Styles.BitmapRegistry.Instance.Register(new System.IO.MemoryStream(png.ToArray()), "walkhop_positions_beam");
            return _beamBitmapId;
        }
    }

    /// <summary>Zeichnet das Positions-Symbol (Beam) in die übergebene Ebene: Standortpunkt +
    /// glatter Blickrichtungs-Beam, gedreht um <paramref name="kursGrad"/>, mit der Karte
    /// drehend. Ist <paramref name="pos"/> null (kein gültiger Standort), wird die Ebene geleert.</summary>
    public static void PositionBeamZeichnen(MemoryLayer layer, MPoint? pos, double kursGrad)
    {
        if (pos == null)
        {
            layer.Features = new List<IFeature>();
            layer.DataHasChanged();
            return;
        }
        var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(pos.X, pos.Y) };
        f.Styles.Add(new SymbolStyle
        {
            BitmapId = BeamBitmapId(),
            SymbolScale = 0.5,
            SymbolRotation = kursGrad,    // dreht in Blickrichtung …
            RotateWithMap = true,         // … und mit der Karte (Norden-/Fahrtrichtungs-Ansicht korrekt)
            Fill = null, Outline = null,  // kein Standard-Ellipsen-Symbol (heller Ring) hinter der Bitmap
        });
        layer.Features = new List<IFeature> { f };
        layer.DataHasChanged();
    }

    // ---- Gruppen-Marker ----------------------------------------------------

    /// <summary>Baut die Marker-Features der Gruppen-Mitglieder (frisch = orange, veraltet = grau,
    /// beschriftet). Das eigene Mitglied (<paramref name="ich"/>) wird ausgelassen. Liefert die
    /// Features plus die Zahl der anderen sichtbaren Mitglieder (für das Badge). Das Setzen der
    /// Ebene/UI bleibt seitenspezifisch (unterschiedliche Aktiv-Bedingung des Badges).</summary>
    public static (List<IFeature> features, int andere) GruppenMarker(List<GruppenMitglied> mitglieder, string ich)
    {
        var feats = new List<IFeature>();
        int andere = 0;
        foreach (var m in mitglieder)
        {
            if (string.Equals(m.Name, ich, StringComparison.OrdinalIgnoreCase)) continue;   // mich nicht doppelt
            andere++;
            var (x, y) = SphericalMercator.FromLonLat(m.Lng, m.Lat);
            string farbe = m.AlterS < 90 ? "#f59e0b" : "#94a3b8";   // frisch orange, veraltet grau
            var f = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(x, y) };
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse, SymbolScale = 0.7,
                Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString(farbe)),
                Outline = new Pen(Mapsui.Styles.Color.White, 2),
            });
            f.Styles.Add(new LabelStyle
            {
                Text = m.Name, Offset = new Offset(0, 20), Font = new Mapsui.Styles.Font { Size = 12, Bold = true },
                ForeColor = Mapsui.Styles.Color.FromString("#0f172a"),
                BackColor = new Mapsui.Styles.Brush(Mapsui.Styles.Color.White),
                Halo = new Pen(Mapsui.Styles.Color.White, 2),
            });
            feats.Add(f);
        }
        return (feats, andere);
    }

    // ---- Treffer-Suche auf Tour-Routen -------------------------------------

    /// <summary>Nächstgelegene Tour-Route zum Punkt (Abstand zum LINIENSEGMENT, nicht nur zu
    /// Stützpunkten), null wenn keine in <paramref name="pxToleranz"/> Pixeln Reichweite.
    /// Trefferradius rein pixelbasiert (zoom-unabhängig). <paramref name="res"/> = aktuelle
    /// Mercator-Auflösung (Meter/Pixel); &lt;= 0 → null.</summary>
    public static TourInfo? NaechsteRoute(IEnumerable<TourInfo> touren, double lat, double lon, double res, double pxToleranz)
    {
        if (res <= 0) return null;
        double mProPixel = res * Math.Cos(lat * Math.PI / 180);   // ≈ reale Meter/Pixel
        double tol = mProPixel * pxToleranz;
        TourInfo? best = null;
        double bestD = tol;
        foreach (var t in touren)
        {
            if (t.Route.Count < 2) continue;
            for (int i = 1; i < t.Route.Count; i++)
            {
                double d = NavGeo.DistanzZuSegment(lat, lon, t.Route[i - 1], t.Route[i]);
                if (d < bestD) { bestD = d; best = t; }
            }
        }
        return best;
    }
}
