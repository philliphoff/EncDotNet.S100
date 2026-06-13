using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Uppercases a string for display using the current culture. Used by the
/// <see cref="Views.ApplicationPanel"/> title bar so panel titles render in
/// upper case regardless of the casing of the source string.
/// </summary>
internal sealed class ToUpperConverter : IValueConverter
{
    /// <summary>Shared singleton instance for XAML <c>x:Static</c> use.</summary>
    public static ToUpperConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text ? text.ToUpper(culture) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
