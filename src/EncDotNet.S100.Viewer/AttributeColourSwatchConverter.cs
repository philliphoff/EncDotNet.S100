using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Renders a small colour swatch beside attribute values that name a
/// colour (e.g. an S-101 <c>colour</c> attribute decoded to "Red"). The
/// converter input is the <see cref="PickAttribute"/> row; with the default
/// (or <c>"brush"</c>) parameter it returns the swatch <see cref="IBrush"/>
/// (transparent when the row is not a recognised colour), and with the
/// <c>"visible"</c> parameter it returns a <see cref="bool"/> for the
/// swatch's visibility. The swatch is purely indicative — it is not a
/// portrayal-accurate colour token.
/// </summary>
internal sealed class AttributeColourSwatchConverter : IValueConverter
{
    public static AttributeColourSwatchConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = ResolveBrush(value as PickAttribute);
        var wantVisible = string.Equals(parameter as string, "visible", StringComparison.OrdinalIgnoreCase);
        if (wantVisible)
            return brush is not null;

        return brush ?? Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush? ResolveBrush(PickAttribute? attr)
    {
        if (attr is null)
            return null;

        // Only attempt a swatch for colour-typed attributes so non-colour
        // values that happen to spell a colour word are left untouched.
        if (!IsColourAttribute(attr.Code, attr.Name))
            return null;

        var text = attr.DisplayText;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Colour values may be compound (e.g. "Red;White"); the swatch shows
        // the first recognised colour.
        foreach (var token in text.Split([';', '/', ',', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryMapColour(token, out var color))
                return new SolidColorBrush(color);
        }

        return null;
    }

    private static bool IsColourAttribute(string? code, string? name)
        => Contains(code, "colour") || Contains(code, "color")
           || Contains(name, "colour") || Contains(name, "color");

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool TryMapColour(string token, out Color color)
    {
        color = token.ToLowerInvariant() switch
        {
            "white" => Colors.White,
            "black" => Colors.Black,
            "red" => Color.FromRgb(0xD0, 0x21, 0x21),
            "green" => Color.FromRgb(0x1F, 0x9D, 0x55),
            "blue" => Color.FromRgb(0x1F, 0x6F, 0xD0),
            "yellow" => Color.FromRgb(0xF2, 0xC4, 0x1D),
            "grey" or "gray" => Color.FromRgb(0x80, 0x80, 0x80),
            "brown" => Color.FromRgb(0x8B, 0x5A, 0x2B),
            "amber" => Color.FromRgb(0xFF, 0xBF, 0x00),
            "violet" => Color.FromRgb(0x7A, 0x3C, 0xC8),
            "orange" => Color.FromRgb(0xE8, 0x7A, 0x17),
            "magenta" => Color.FromRgb(0xC0, 0x37, 0x8A),
            "pink" => Color.FromRgb(0xE8, 0x8A, 0xA8),
            _ => default,
        };

        return color != default;
    }
}
