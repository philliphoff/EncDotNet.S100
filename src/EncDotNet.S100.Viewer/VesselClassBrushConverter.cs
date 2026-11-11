using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EncDotNet.S100.DynamicSources.Ais;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Maps an <see cref="AisShipTypeClass"/> to the brush used to tint the
/// vessel pictogram in the Vessels panel. Mirrors the stroke palette of
/// <c>AisVesselRenderer</c> so the list row and the on-map symbol read as
/// the same colour family.
/// </summary>
internal sealed class VesselClassBrushConverter : IValueConverter
{
    public static VesselClassBrushConverter Instance { get; } = new();

    private static readonly IBrush Default = new SolidColorBrush(Color.FromRgb(0x00, 0xA0, 0x40));
    private static readonly IBrush Tanker = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
    private static readonly IBrush Passenger = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x80));
    private static readonly IBrush HighSpeed = new SolidColorBrush(Color.FromRgb(0x80, 0x33, 0xCC));
    private static readonly IBrush Unknown = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AisShipTypeClass cls
            ? cls switch
            {
                AisShipTypeClass.Tanker => Tanker,
                AisShipTypeClass.Passenger => Passenger,
                AisShipTypeClass.HighSpeedCraft => HighSpeed,
                AisShipTypeClass.Unknown => Unknown,
                _ => Default,
            }
            : Unknown;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
