using Microsoft.Maui.Controls.Xaml;

namespace SpinNaviApp;

/// <summary>XAML-Markup-Extension für mehrsprachige Texte: <c>Text="{loc:Translate nav_start}"</c>.
/// Liefert ein OneWay-Binding auf den Indexer von <see cref="Lokalisierung.Instanz"/> – beim
/// Sprachwechsel meldet der Singleton „Item[]", sodass der Text sofort aktualisiert wird.</summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    /// <summary>Übersetzungs-Schlüssel (positional: <c>{loc:Translate nav_start}</c>).</summary>
    public string Key { get; set; } = "";

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = $"[{Key}]",
        Source = Lokalisierung.Instanz,
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ((IMarkupExtension<BindingBase>)this).ProvideValue(serviceProvider);
}
