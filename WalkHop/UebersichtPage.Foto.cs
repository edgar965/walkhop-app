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
    // Nächstgelegenes angezeigtes Foto zum Tipp-Punkt (null, wenn keins in ~22 px Reichweite).
    private FotoPunkt? NaechstesFoto(double lat, double lon)
    {
        if (!_fotoAn || _fotos.Count == 0) return null;
        double res = _map.Navigator.Viewport.Resolution;
        if (res <= 0) return null;
        double mProPixel = res * Math.Cos(lat * Math.PI / 180);
        double tol = mProPixel * 22;
        var sichtbar = new HashSet<int>(_gefiltert.Select(t => t.Id));
        FotoPunkt? best = null;
        double bestD = tol;
        foreach (var f in _fotos)
        {
            if (!sichtbar.Contains(f.TourId)) continue;
            double d = NavGeo.Haversine(lat, lon, f.Lat, f.Lon);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    // Foto-Betrachter: zeigt das Bild + Bildunterschrift in einem Modal; optional von dort navigieren.
    // Bildquelle: bevorzugt die offline gespeicherte (verkleinerte) Variante per Foto-Id,
    // sonst die Online-URL. So zeigt die App heruntergeladene Fotos auch ohne Netz.
    private static ImageSource Bildquelle(int id, string url)
    {
        var lokal = OfflineFotos.LokaleQuelle(id);
        if (lokal != null) return lokal;
        var u = url.StartsWith("http") ? url : AppConfig.ApiBase + url;
        return ImageSource.FromUri(new Uri(u));
    }

    private async Task FotoBetrachten(FotoPunkt foto)
    {
        var bild = new Image { Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Fill };
        try { bild.Source = Bildquelle(foto.Id, foto.Url); } catch (Exception ex) { Debug.WriteLine(ex); }
        string bu = !string.IsNullOrWhiteSpace(foto.Text) ? foto.Text : foto.Tour;
        var titel = new Label { Text = bu, TextColor = Microsoft.Maui.Graphics.Colors.White, FontSize = 14,
                                Padding = new Thickness(14, 10), BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#003153"),
                                LineBreakMode = LineBreakMode.WordWrap };
        var navBtn = new Button { Text = L.T("foto_navigieren"), BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#16a34a"),
                                  TextColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 10, Margin = new Thickness(12, 6) };
        var zuBtn = new Button { Text = L.T("schliessen"), BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#334155"),
                                 TextColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 10, Margin = new Thickness(12, 0, 12, 12) };
        var grid = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto },
                                                  new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } },
                              BackgroundColor = Microsoft.Maui.Graphics.Colors.Black };
        Grid.SetRow(bild, 0); Grid.SetRow(titel, 1); Grid.SetRow(navBtn, 2); Grid.SetRow(zuBtn, 3);
        grid.Children.Add(bild); grid.Children.Add(titel); grid.Children.Add(navBtn); grid.Children.Add(zuBtn);
        var seite = new ContentPage { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black, Content = grid };
        navBtn.Clicked += async (s, e) =>
        {
            await Navigation.PopModalAsync();
            MainPage.GeplantesZiel = (foto.Lat, foto.Lon);
            await Shell.Current.GoToAsync("//navigation");
        };
        zuBtn.Clicked += async (s, e) => await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(seite);
    }

    // Kamera-Pin für Foto-Marker: einmal mit SkiaSharp gezeichnet, dann für ALLE Fotos
    // wiederverwendet (eine Bitmap → performant, kein Thumbnail-Download je Punkt).
    private int FotoPinBitmapId()
    {
        if (_fotoPinBitmapId >= 0) return _fotoPinBitmapId;
        const int g = 72;
        var blau = new SkiaSharp.SKColor(29, 78, 216);   // #1d4ed8 (wie der Foto-Knopf)
        using var bitmap = new SkiaSharp.SKBitmap(g, g);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            using var weiss = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
            using var rand = new SkiaSharp.SKPaint { Color = blau, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 5 };
            using var fuell = new SkiaSharp.SKPaint { Color = blau, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill };
            var hoecker = new SkiaSharp.SKRect(26, 13, 46, 25);            // Sucher-Höcker oben
            canvas.DrawRoundRect(hoecker, 3, 3, weiss);
            canvas.DrawRoundRect(hoecker, 3, 3, rand);
            var body = new SkiaSharp.SKRect(10, 22, g - 10, g - 12);       // Kamera-Körper
            canvas.DrawRoundRect(body, 9, 9, weiss);
            canvas.DrawRoundRect(body, 9, 9, rand);
            float cy = (22 + g - 12) / 2f + 1;
            canvas.DrawCircle(g / 2f, cy, 10, fuell);                      // Linse
            using var linseInnen = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = true };
            canvas.DrawCircle(g / 2f, cy, 4.5f, linseInnen);
        }
        using var img = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        _fotoPinBitmapId = Mapsui.Styles.BitmapRegistry.Instance.Register(new System.IO.MemoryStream(png.ToArray()), "uebersicht_fotopin");
        return _fotoPinBitmapId;
    }

    private async void OnFoto(object? sender, EventArgs e)
    {
        _fotoAn = !_fotoAn;
        FotoKnopfAnzeigen();
        if (_fotoAn && _fotos.Count == 0)
        {
            Status(L.T("ue_st_fotos_laden"));
            try { _fotos = await FotoService.LadeAsync(); Status(null); }
            catch (Exception ex) { Debug.WriteLine(ex); Status(L.T("ue_st_fotos_nicht_verfuegbar")); }
        }
        FotoFilterAnwenden();
    }

    // Foto-Knopf-Optik an _fotoAn ausrichten: aktiv = blauer Knopf + weißes Kamera-Icon; sonst weiß + dunkel.
    private void FotoKnopfAnzeigen()
    {
        FotoBorder.BackgroundColor = _fotoAn ? Microsoft.Maui.Graphics.Color.FromArgb("#1d4ed8") : Microsoft.Maui.Graphics.Colors.White;
        var fi = new SolidColorBrush(_fotoAn ? Microsoft.Maui.Graphics.Colors.White : Microsoft.Maui.Graphics.Color.FromArgb("#0f172a"));
        FotoIcon.Stroke = fi;
        FotoLinse.Stroke = fi;
    }

    private void FotoFilterAnwenden()
    {
        // Foto-Ebene sicher aktivieren (die Zoom-Glättung deaktiviert sie zwischenzeitlich) UND die
        // Karte nach der Feature-Änderung neu zeichnen – sonst erscheinen die Marker auf iOS erst nach
        // einem Resize/Zoom (DataHasChanged allein löst dort kein Redraw aus).
        _fotoLayer.Enabled = _fotoAn;
        _vektorenVerborgen = false;
        if (!_fotoAn) { _fotoLayer.Features = new List<IFeature>(); _fotoLayer.DataHasChanged(); _map.RefreshGraphics(); return; }
        var sichtbar = new HashSet<int>(_gefiltert.Select(t => t.Id));
        var features = new List<IFeature>();
        foreach (var f in _fotos.Where(p => sichtbar.Contains(p.TourId)))
        {
            var (x, y) = SphericalMercator.FromLonLat(f.Lon, f.Lat);
            var pt = new GeometryFeature { Geometry = new NetTopologySuite.Geometries.Point(x, y) };
            pt.Styles.Add(new SymbolStyle
            {
                BitmapId = FotoPinBitmapId(),   // klares Kamera-Pin (kein anonymer gelber Punkt)
                SymbolScale = 0.5,
                Fill = null, Outline = null,    // kein Standard-Ellipsen-Symbol hinter der Bitmap
            });
            features.Add(pt);
        }
        _fotoLayer.Features = features;
        _fotoLayer.DataHasChanged();
        _map.RefreshGraphics();
    }
}
