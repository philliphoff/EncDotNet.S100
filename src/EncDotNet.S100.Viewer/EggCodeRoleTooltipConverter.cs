using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Maps an <see cref="IceEggValueRole"/> (carried by each cell of the S-411
/// WMO / SIGRID-3 egg diagram) to a localized human-readable role label used
/// as the cell's tooltip. Unknown or unset roles yield <c>null</c> so no
/// tooltip is shown.
/// </summary>
internal sealed class EggCodeRoleTooltipConverter : IValueConverter
{
    public static EggCodeRoleTooltipConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IceEggValueRole role)
            return null;

        return role switch
        {
            IceEggValueRole.TotalConcentration => Resources.Strings.Pick_EggCode_Role_TotalConcentration,
            IceEggValueRole.PartialConcentration => Resources.Strings.Pick_EggCode_Role_PartialConcentration,
            IceEggValueRole.StageOfDevelopment => Resources.Strings.Pick_EggCode_Role_StageOfDevelopment,
            IceEggValueRole.FormOfIce => Resources.Strings.Pick_EggCode_Role_FormOfIce,
            IceEggValueRole.ThinnerStage => Resources.Strings.Pick_EggCode_Role_ThinnerStage,
            IceEggValueRole.ThinnerPartial => Resources.Strings.Pick_EggCode_Role_ThinnerPartial,
            IceEggValueRole.SnowDepth => Resources.Strings.Pick_EggCode_Role_SnowDepth,
            _ => null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
