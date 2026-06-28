using System.Globalization;
using Microsoft.Maui.Controls.Xaml;

namespace WalkHop;

/// <summary>XAML-Markup-Extension für mehrsprachige Texte: <c>Text="{loc:Translate nav_start}"</c>.
/// Bindet an die <see cref="Lokalisierung.Sprache"/>-Property (die beim Wechsel zuverlässig
/// PropertyChanged feuert) und übersetzt den Schlüssel per Konverter. Ein Indexer-Binding auf
/// „Item[]" hat MAUI bei String-Indexern NICHT zuverlässig aktualisiert – darum dieser Weg.</summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    private static readonly UebersetzungsKonverter Konverter = new();

    /// <summary>Übersetzungs-Schlüssel (positional: <c>{loc:Translate nav_start}</c>).</summary>
    public string Key { get; set; } = "";

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = nameof(Lokalisierung.Sprache),   // ändert sich bei jedem Sprachwechsel → Binding refresht
        Source = Lokalisierung.Instanz,
        Converter = Konverter,
        ConverterParameter = Key,
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ((IMarkupExtension<BindingBase>)this).ProvideValue(serviceProvider);
}

/// <summary>Übersetzt den per ConverterParameter übergebenen Schlüssel in die aktuelle Sprache.
/// Der gebundene Wert (die Sprache) dient nur als Auslöser fürs Neu-Auswerten.</summary>
internal sealed class UebersetzungsKonverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Lokalisierung.Instanz[(parameter as string) ?? ""];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
