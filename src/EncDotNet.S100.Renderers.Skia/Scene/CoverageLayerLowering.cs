using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Lowers one coverage sub-layer (colour band, arrows, station glyphs, vector
/// symbols or isolines) to a Skia <see cref="CompositeLayer"/>, together with
/// its WGS-84 extent. Shared by the chart engine's compositor and by
/// single-dataset headless renders, so both draw a coverage the same way.
/// </summary>
internal static class CoverageLayerLowering
{
    /// <summary>The settings a lowered vector-coverage layer is drawn with.</summary>
    /// <param name="HiddenCategories">Drawing-instruction categories to suppress.</param>
    /// <param name="HonorScaleVisibility">Whether vector symbols apply scale-visibility culling.</param>
    /// <param name="EnableSeamWrap">Whether vector symbols wrap into a viewport across the antimeridian.</param>
    /// <param name="KeepAreaSymbolsInView">Whether area symbols move into the visible part of a surface.</param>
    internal readonly record struct Settings(
        DrawingInstructionCategory HiddenCategories,
        bool HonorScaleVisibility,
        bool EnableSeamWrap,
        bool KeepAreaSymbolsInView);

    /// <summary>
    /// Lowers <paramref name="subLayer"/>. Returns <see langword="false"/> for a
    /// sub-layer with no extent, an empty grid, or a kind that has no composite
    /// form.
    /// </summary>
    /// <param name="subLayer">The coverage sub-layer.</param>
    /// <param name="crsTransformFactory">Reprojects a grid's native CRS to WGS-84.</param>
    /// <param name="settings">How vector symbols in the coverage draw.</param>
    /// <param name="layer">The lowered layer.</param>
    /// <param name="west">The western edge, in degrees.</param>
    /// <param name="east">The eastern edge, in degrees.</param>
    /// <param name="south">The southern edge, in degrees.</param>
    /// <param name="north">The northern edge, in degrees.</param>
    public static bool TryLower(
        CoverageSubLayerBase subLayer,
        ICrsTransformFactory crsTransformFactory,
        Settings settings,
        out CompositeLayer layer,
        out double west, out double east, out double south, out double north)
    {
        layer = null!;
        west = east = south = north = 0;

        switch (subLayer)
        {
            case GridCoverageSubLayer grid:
                {
                    var extent = ReprojectExtent(grid.Coverage, crsTransformFactory);
                    if (extent is null)
                        return false;

                    var (w, e, s, n, nativeToWgs84) = extent.Value;
                    layer = new CoverageCompositeLayer(
                        grid.Coverage, w, e, s, n, nativeToWgs84: nativeToWgs84, landAreas: grid.LandAreaMask)
                    {
                        Opacity = grid.Opacity,
                    };
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            case ArrowCoverageSubLayer arrow:
                {
                    var extent = ReprojectExtent(arrow.Coverage, crsTransformFactory);
                    if (extent is null)
                        return false;

                    var (w, e, s, n, nativeToWgs84) = extent.Value;
                    var arrowRenderer = new SkiaCoverageArrowRenderer
                    {
                        // Without the palette the SCAROW fSCBN{N} colour tokens
                        // stay unresolved and every arrow draws black.
                        Palette = arrow.Palette,
                        SymbolProvider = arrow.SymbolProvider,
                        BaseSymbolScale = arrow.BaseSymbolScale,
                    };
                    layer = new CoverageCompositeLayer(arrow.Coverage, w, e, s, n, arrowRenderer, nativeToWgs84);
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            case GlyphCoverageSubLayer glyph:
                {
                    if (glyph.Extent is not { } extent)
                        return false;

                    layer = PointGlyphHeadlessAdapter.CreateLayer(glyph);
                    (west, east, south, north) = ToLonLat(extent);
                    return true;
                }

            case VectorCoverageSubLayer vector:
                {
                    if (vector.Extent is not { } extent)
                        return false;

                    layer = new VectorCompositeLayer(
                        PointGlyphHeadlessAdapter.BuildScene(vector, settings.HiddenCategories), settings.HonorScaleVisibility)
                    {
                        EnableSeamWrap = settings.EnableSeamWrap,
                        KeepAreaSymbolsInView = settings.KeepAreaSymbolsInView,
                    };
                    (west, east, south, north) = ToLonLat(extent);
                    return true;
                }

            case IsolineCoverageSubLayer isolines:
                {
                    if (isolines.Extent is not { } extent)
                        return false;

                    layer = new IsolineCompositeLayer(IsolineHeadlessAdapter.CreateScene(isolines));
                    (west, east, south, north) = ToLonLat(extent);
                    return true;
                }

            default:
                return false;
        }
    }

    private static (double West, double East, double South, double North) ToLonLat(MercatorBounds extent)
    {
        var (west, south) = WebMercator.ToLonLat(extent.MinX, extent.MinY, clampLatitude: false);
        var (east, north) = WebMercator.ToLonLat(extent.MaxX, extent.MaxY, clampLatitude: false);
        return (west, east, south, north);
    }

    private static (double West, double East, double South, double North, ICrsTransform NativeToWgs84)? ReprojectExtent(
        StyledCoverageLayer coverage, ICrsTransformFactory crsTransformFactory)
    {
        var metadata = coverage.Georeferencer.Metadata;
        if (metadata.NumRows <= 0 || metadata.NumColumns <= 0)
            return null;

        // Sampled grid nodes are cell centres. Derive the native bounds from the
        // sampled metadata and add the half-cell pad before reprojection so a
        // viewport subset is neither stretched to the source extent nor clipped
        // through its outer cells.
        double xEnd = metadata.OriginLongitude
            + (metadata.NumColumns - 1) * metadata.SpacingLongitudinal;
        double yEnd = metadata.OriginLatitude
            + (metadata.NumRows - 1) * metadata.SpacingLatitudinal;
        double halfCellX = Math.Abs(metadata.SpacingLongitudinal) / 2.0;
        double halfCellY = Math.Abs(metadata.SpacingLatitudinal) / 2.0;
        double minX = Math.Min(metadata.OriginLongitude, xEnd) - halfCellX;
        double maxX = Math.Max(metadata.OriginLongitude, xEnd) + halfCellX;
        double minY = Math.Min(metadata.OriginLatitude, yEnd) - halfCellY;
        double maxY = Math.Max(metadata.OriginLatitude, yEnd) + halfCellY;

        var crs = coverage.Georeferencer.CRS;
        var nativeToWgs84 = crsTransformFactory.Create(crs, "EPSG:4326");

        var corners = new (double Longitude, double Latitude)[4];
        corners[0] = Transform(minX, minY);
        corners[1] = Transform(minX, maxY);
        corners[2] = Transform(maxX, minY);
        corners[3] = Transform(maxX, maxY);

        double west = corners.Min(corner => corner.Longitude);
        double east = corners.Max(corner => corner.Longitude);
        double south = corners.Min(corner => corner.Latitude);
        double north = corners.Max(corner => corner.Latitude);

        return (west, east, south, north, nativeToWgs84);

        (double Longitude, double Latitude) Transform(double x, double y) =>
            nativeToWgs84.IsIdentity ? (x, y) : nativeToWgs84.Transform(x, y);
    }
}
