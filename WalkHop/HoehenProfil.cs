using Microsoft.Maui.Graphics;

namespace WalkHop;

/// <summary>Zeichnet das Höhenprofil (range_height) als gefüllte Linie in eine GraphicsView.</summary>
public class HoehenProfil : IDrawable
{
    public List<(double dist, double hoehe)> Daten { get; set; } = new();

    public void Draw(ICanvas c, RectF r)
    {
        if (Daten.Count < 2) return;
        double minH = double.MaxValue, maxH = double.MinValue, maxD = Daten[^1].dist;
        foreach (var (_, h) in Daten) { if (h < minH) minH = h; if (h > maxH) maxH = h; }
        if (maxD <= 0) maxD = 1;
        if (maxH - minH < 1) maxH = minH + 1;

        const float pad = 4f;
        float Bx(double d) => pad + (float)(d / maxD) * (r.Width - 2 * pad);
        float By(double h) => r.Height - pad - (float)((h - minH) / (maxH - minH)) * (r.Height - 2 * pad);

        var linie = new PathF();
        var flaeche = new PathF();
        flaeche.MoveTo(Bx(0), r.Height - pad);
        for (int i = 0; i < Daten.Count; i++)
        {
            float x = Bx(Daten[i].dist), y = By(Daten[i].hoehe);
            if (i == 0) linie.MoveTo(x, y); else linie.LineTo(x, y);
            flaeche.LineTo(x, y);
        }
        flaeche.LineTo(Bx(maxD), r.Height - pad);
        flaeche.Close();

        c.FillColor = Color.FromRgba(13, 148, 136, 40);   // #0d9488, transparent
        c.FillPath(flaeche);
        c.StrokeColor = Color.FromArgb("#0d9488");
        c.StrokeSize = 2;
        c.DrawPath(linie);
    }
}
