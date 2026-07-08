using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Builds the hover tooltip for a single cell of the S-411 WMO / SIGRID-3 egg
/// diagram. Takes two bindings — <c>[definition, role]</c> — where
/// <c>definition</c> is the Feature-Catalogue prose meaning of the value (may
/// be <c>null</c>) and <c>role</c> is the <see cref="IceEggValueRole"/>. When a
/// definition is present it is shown with the role in parentheses
/// (<c>"Grey Ice (Stage of development …)"</c>); otherwise the role label alone
/// is used.
/// </summary>
internal sealed class EggCodeValueTooltipConverter : IMultiValueConverter
{
    public static EggCodeValueTooltipConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var definition = values.Count > 0 ? values[0] as string : null;
        var roleLabel = values.Count > 1 && values[1] is IceEggValueRole role
            ? EggCodeRoleTooltipConverter.Instance.Convert(role, typeof(string), null, culture) as string
            : null;

        if (string.IsNullOrWhiteSpace(definition))
            return roleLabel;

        return string.IsNullOrWhiteSpace(roleLabel)
            ? definition
            : $"{definition} ({roleLabel})";
    }
}
