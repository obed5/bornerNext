using System.Windows.Markup;

namespace BorderNext.UI.Resources;

/// <summary>
/// Extension XAML de localisation : <c>Header="{res:Str Key=Admin_Tab_Users}"</c>.
/// Résolue à l'analyse du XAML via <see cref="Strings.Get"/>.
/// </summary>
public sealed class StrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}

/// <summary>Extension XAML de mise en forme localisée : <c>{res:StrF Key=... Arg1=...}</c>.</summary>
public sealed class StrFormatExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;
    public object? Arg1 { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Format(Key, Arg1 ?? string.Empty);
}
