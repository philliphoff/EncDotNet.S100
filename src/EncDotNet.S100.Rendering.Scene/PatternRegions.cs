using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Where one area pattern is drawn once S-100 Part 9 pattern priority
/// clipping is applied: the pattern, the area instructions that fill with it
/// at one drawing priority, and the region of them that stays visible.
/// </summary>
/// <param name="PatternReference">The portrayal catalogue's area-fill name, such as <c>DQUALB01</c>.</param>
/// <param name="DrawingPriority">The drawing priority the group's instructions share.</param>
/// <param name="Instructions">
/// The area instructions filled with <paramref name="PatternReference"/> at
/// <paramref name="DrawingPriority"/>, in draw order, unchanged from the
/// display list (feature, scale limits).
/// </param>
/// <param name="Area">
/// The visible part of the instructions' surfaces, a <see cref="Polygon"/> or
/// <see cref="MultiPolygon"/> in EPSG:3857 metres: their union, less every
/// higher-priority pattern area and every opaque colour fill.
/// </param>
public sealed record PatternRegion(
    string PatternReference,
    int DrawingPriority,
    IReadOnlyList<AreaInstruction> Instructions,
    Geometry Area);

/// <summary>
/// Hands out a display list's area patterns as data, with S-100 Part 9
/// pattern priority clipping already applied, for renderers that draw the
/// portrayal themselves (GIS symbology, vector PDF or SVG).
/// </summary>
/// <remarks>
/// <para>
/// Where patterned areas overlap, each spot shows the pattern of the highest
/// drawing priority only, so a lower-priority pattern (for example
/// <c>DIAMOND1</c>) does not show through a higher-priority one (for example
/// <c>DQUALB01</c>). No pattern shows over an opaque colour fill, such as
/// land, unless that fill's feature itself carries a pattern; a colour fill
/// with <see cref="AreaInstruction.Transparency"/> does not hide patterns.
/// A consumer that fills each <see cref="PatternRegion.Area"/> with its
/// pattern therefore never draws two patterns over each other.
/// </para>
/// <para>
/// This is the clip <see cref="VectorSceneBuilder"/> applies to the viewer's
/// scene, with the same <see cref="PatternPriorityClipper"/>: the
/// <see cref="PatternAreaPaintOp"/>s of a built scene fill the same regions.
/// The areas are grouped by pattern (ignoring case) and priority, as the
/// scene groups them.
/// </para>
/// </remarks>
public static class PatternRegions
{
    // The clip doesn't look at the tile; any non-empty one lowers the pattern.
    private static readonly byte[] PlaceholderTile = [0];

    /// <summary>
    /// Resolves the pattern fills of <paramref name="instructions"/> into the
    /// regions each pattern is drawn in, in S-100 Part 9 draw order.
    /// </summary>
    /// <param name="instructions">The display list, in any order.</param>
    /// <param name="geometryProvider">The geometry of the display list's features.</param>
    /// <returns>
    /// One <see cref="PatternRegion"/> per pattern and drawing priority, in
    /// ascending priority. A pattern hidden everywhere by higher-priority
    /// patterns or opaque fills gives none; a pattern instruction without
    /// surface geometry is left out.
    /// </returns>
    public static IReadOnlyList<PatternRegion> Resolve(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        // Only area instructions take part: pattern fills are clipped, and
        // opaque colour fills clip them. Palette colours are opaque, so a
        // colour fill's opacity comes from its transparency alone.
        var areas = instructions.Where(i => i is AreaInstruction).ToList();
        var builder = new VectorSceneBuilder
        {
            ResolveColor = static _ => new RgbaColor(0, 0, 0, 255),
            PatternResolver = static _ => PlaceholderTile,
        };

        var regions = new List<PatternRegion>();
        builder.Build(areas, geometryProvider, regions);
        return regions;
    }
}
