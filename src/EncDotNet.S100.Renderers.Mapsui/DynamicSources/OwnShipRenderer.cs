using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Own-ship renderer that draws a true-scale hull outline when the
/// viewport is zoomed in far enough for the vessel to be visually
/// distinguishable, falls back to a coloured disc when zoomed out,
/// and decorates the course / speed vector with an arrowhead in both
/// modes. Honours the IEC 62388 CCRP offsets carried by
/// <see cref="DynamicVesselGeometry"/> on the feature.
/// </summary>
/// <remarks>
/// <para>
/// The actual symbol-emission logic is shared with
/// <c>AisVesselRenderer</c> via the internal
/// <see cref="VesselSymbology"/> helper — own-ship simply binds the
/// helper to the S-52 own-ship blue palette.
/// </para>
/// <para>
/// Standards alignment (see <c>docs/design/own-ship-symbology.md</c>
/// §1 for citations): hull = SY(OWNSHP02), pictogram = SY(OWNSHP01),
/// CCRP cross = §8.3.1, COG vector convention from S-52 Annex A.
/// </para>
/// </remarks>
public sealed class OwnShipRenderer : IDynamicFeatureRenderer
{
    /// <summary>Pixel size at which the hull outline begins to display
    /// (≈ 6 mm at 96 dpi — IHO S-52 Ed 6.1 §§7.4.5 / 13.2.7).</summary>
    public const double MinVesselPixels = VesselSymbology.MinVesselPixels;

    /// <summary>Pixel size of the arrowhead on the course / speed
    /// vector (S-52 COG vector convention).</summary>
    public const double HeadingArrowPx = VesselSymbology.HeadingArrowPx;

    /// <summary>Pixel half-span of each arm of the CCRP cross
    /// (per IHO S-52 §8.3.1).</summary>
    public const double CcrpCrossPx = VesselSymbology.CcrpCrossPx;

    /// <summary>Bow-taper ratio.</summary>
    public const double BowTaperRatio = VesselSymbology.BowTaperRatio;

    private static readonly VesselSymbology.Palette OwnShipPalette = new(
        Stroke: new MapsuiColor(0x00, 0x7A, 0xCC),
        Fill: new MapsuiColor(0x66, 0xB3, 0xE6),
        HullFill: new MapsuiColor(0x66, 0xB3, 0xE6, 160));

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.GeometryType == GeometryType.Point;
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
        => VesselSymbology.Render(feature, OwnShipPalette);
}
