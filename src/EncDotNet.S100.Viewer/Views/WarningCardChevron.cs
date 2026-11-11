using System.Globalization;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>A collapsible Warning card's chevron: up while open, down while collapsed (#932).</summary>
internal sealed class WarningCardChevron : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static readonly WarningCardChevron Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Icon.ChevronUp : Icon.ChevronDown;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
