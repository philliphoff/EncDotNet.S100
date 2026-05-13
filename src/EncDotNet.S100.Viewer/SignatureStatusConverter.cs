using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts <see cref="SignatureStatus"/> to a boolean indicating whether a
/// specific status value matches. Used to toggle visibility of badge icons
/// in the exchange set header template.
/// </summary>
internal sealed class SignatureStatusConverter : IValueConverter
{
    /// <summary>Comma-separated list of matching <see cref="SignatureStatus"/> names.</summary>
    public string Match { get; set; } = "";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SignatureStatus status) return false;
        var statusName = status.ToString();
        foreach (var part in Match.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, statusName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
