using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Builds a tooltip string that pairs a friendly label/value with its raw
/// counterpart so the full text remains readable even when the table cell
/// truncates it. The converter takes two bindings — <c>[friendly, raw]</c>
/// — and returns <c>"friendly (raw)"</c>. The parenthesised raw form is
/// omitted when the raw text is empty or equal (ordinal, case-insensitive)
/// to the friendly text, avoiding redundant <c>"Foo (Foo)"</c> tooltips.
/// </summary>
internal sealed class AttributeTooltipConverter : IMultiValueConverter
{
    public static AttributeTooltipConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var friendly = values.Count > 0 ? values[0] as string : null;
        var raw = values.Count > 1 ? values[1] as string : null;

        if (string.IsNullOrWhiteSpace(friendly))
            return string.IsNullOrWhiteSpace(raw) ? null : raw;

        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(friendly, raw, StringComparison.OrdinalIgnoreCase))
            return friendly;

        return $"{friendly} ({raw})";
    }
}
