using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Multi-binding converter that returns <c>true</c> when a tab's
/// <see cref="IActivityTab.Id"/> equals
/// <see cref="MainViewModel.SelectedTabId"/>. Drives the activity-bar
/// <c>ToggleButton.IsChecked</c> and accent-indicator <c>IsVisible</c>
/// bindings without each tab needing its own bool property on
/// <see cref="MainViewModel"/>.
/// </summary>
/// <remarks>
/// Binding order is <c>[SelectedTabId, Tab.Id]</c>.
/// </remarks>
internal sealed class ActiveTabConverter : IMultiValueConverter
{
    public static readonly ActiveTabConverter Instance = new();

    public object Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (values is null || values.Count < 2)
        {
            return false;
        }

        var selected = values[0] as string;
        var tabId = values[1] as string;
        if (selected is null || tabId is null)
        {
            return false;
        }

        return string.Equals(selected, tabId, StringComparison.Ordinal);
    }
}
