using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// AIS-target renderer. Mirrors <see cref="OwnShipRenderer"/>'s
/// hull / pictogram / CCRP-cross / heading-vector vocabulary via the
/// shared <see cref="VesselSymbology"/> helper, dispatched off the
/// AIS ship-type bucket encoded in <see cref="DynamicFeature.Kind"/>
/// (suffix produced by <c>AisShipTypeClass.ToKindToken()</c>).
/// </summary>
/// <remarks>
/// <para>
/// Dispatches via a small per-class palette table — not via
/// <c>KindMatchingRenderer</c>, since the rendering is structurally
/// the same shape and only the colours differ. AIS targets share
/// the <c>S98DisplayPlane.DynamicArrows</c> plane with own-ship; the
/// per-source visibility toggles in the layer-stack panel
/// (PR-D2.1) keep the two independently controllable.
/// </para>
/// <para>
/// Default colour is green per the S-52 convention for active AIS
/// targets (SY(AISDEF01) family). Tanker uses red (hazardous-cargo
/// convention), passenger uses dark teal, and unknown-class targets
/// use a neutral grey.
/// </para>
/// </remarks>
public sealed class AisVesselRenderer : IDynamicFeatureRenderer
{
    private const string KindPrefix = "vessel.ais.";

    private static readonly VesselSymbology.Palette DefaultPalette = new(
        Stroke: new MapsuiColor(0x00, 0xA0, 0x40),
        Fill: new MapsuiColor(0x66, 0xCC, 0x88),
        HullFill: new MapsuiColor(0x66, 0xCC, 0x88, 160));

    private static readonly VesselSymbology.Palette TankerPalette = new(
        Stroke: new MapsuiColor(0xCC, 0x33, 0x33),
        Fill: new MapsuiColor(0xE6, 0x99, 0x99),
        HullFill: new MapsuiColor(0xE6, 0x99, 0x99, 160));

    private static readonly VesselSymbology.Palette PassengerPalette = new(
        Stroke: new MapsuiColor(0x00, 0x80, 0x80),
        Fill: new MapsuiColor(0x66, 0xC2, 0xC2),
        HullFill: new MapsuiColor(0x66, 0xC2, 0xC2, 160));

    private static readonly VesselSymbology.Palette HighSpeedPalette = new(
        Stroke: new MapsuiColor(0x80, 0x33, 0xCC),
        Fill: new MapsuiColor(0xC2, 0x99, 0xE6),
        HullFill: new MapsuiColor(0xC2, 0x99, 0xE6, 160));

    private static readonly VesselSymbology.Palette UnknownPalette = new(
        Stroke: new MapsuiColor(0x66, 0x66, 0x66),
        Fill: new MapsuiColor(0xB3, 0xB3, 0xB3),
        HullFill: new MapsuiColor(0xB3, 0xB3, 0xB3, 160));

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.GeometryType == GeometryType.Point
            && feature.Kind is { } kind
            && kind.StartsWith(KindPrefix, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return VesselSymbology.Render(feature, PaletteFor(feature.Kind));
    }

    internal static VesselSymbology.Palette PaletteFor(string? kind)
    {
        if (kind is null || !kind.StartsWith(KindPrefix, StringComparison.Ordinal))
            return UnknownPalette;
        var token = kind[KindPrefix.Length..];
        return token switch
        {
            "tanker" => TankerPalette,
            "passenger" => PassengerPalette,
            "highspeedcraft" => HighSpeedPalette,
            "cargo" or "fishing" or "tug" or "pleasure" or "sailing"
                or "pilot" or "sar" or "lawenforcement" or "military"
                or "other" => DefaultPalette,
            _ => UnknownPalette,
        };
    }
}
