using System.Collections.Generic;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// One out-of-scale extent indicator: the EPSG:3857 extent of a loaded dataset
/// together with the coarsest resolution at which that dataset's content still
/// draws. The indicator's accent border is shown only when the viewport is
/// zoomed out past <see cref="MinVisibleResolution"/> — i.e. exactly when the
/// dataset itself has dropped out — giving the mariner a target to zoom toward
/// (issue #446).
/// </summary>
/// <param name="Extent">The dataset's EPSG:3857 (web-mercator) extent.</param>
/// <param name="MinVisibleResolution">
/// The dataset's whole-cell zoom-out cutoff (metres per pixel), i.e.
/// <see cref="ViewModels.DatasetEntry.ContentMaxVisibleResolution"/>. Beyond
/// this resolution the dataset renders nothing, so the border is drawn from
/// here outward (used as the border style's <c>MinVisible</c>).
/// </param>
internal readonly record struct DatasetExtentIndicator(
    MRect Extent,
    double MinVisibleResolution);

/// <summary>
/// Builds the Mapsui <see cref="MemoryLayer"/> overlay that outlines the
/// extents of loaded datasets which have zoomed out of scale (issue #446).
/// </summary>
/// <remarks>
/// Each indicator is a dashed accent rectangle tracing the dataset extent, with
/// its border <see cref="VectorStyle"/> gated by <c>MinVisible</c> set to the
/// dataset's content cutoff. Mapsui therefore reveals the border precisely when
/// the viewport zooms out past the point where the dataset's content stops
/// drawing, and hides it again once the mariner zooms back in — so the overlay
/// is viewport-agnostic and needs no navigator subscription. The outline is
/// deliberately thin, dashed, and unfilled so it reads as a "here be data"
/// hint rather than chart content (see the <c>chart-cartography</c> guidance on
/// subtle, non-competing decoration).
/// </remarks>
internal static class DatasetExtentIndicatorOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Dataset Extent Indicators";

    /// <summary>
    /// Default accent (matches <c>ViewerSettings.AccentColor</c> default of
    /// <c>#007ACC</c>). Used when no accent has been pushed to the overlay yet.
    /// </summary>
    public static readonly (byte R, byte G, byte B) DefaultAccent = (0x00, 0x7A, 0xCC);

    // Screen-independent dotted hairline. Kept thin so a border around a whole
    // cell never dominates the (otherwise empty) zoomed-out view. A round stroke
    // cap combined with a near-zero on-segment renders each dash as a round dot,
    // which reads more cleanly than dashes at coarse zoom. Values are multiplied
    // by the pen width by the renderer, so the gap here is ~3x the width. This
    // only takes effect with PenStyle.UserDefined; the preset PenStyle.Dash
    // ignores DashArray. The stroke is drawn semi-transparent so the indicator
    // stays muted when it overlaps another dataset's content.
    private const double OutlineWidth = 2.0;
    private const float OutlineOpacity = 0.5f;
    private static readonly float[] DashArray = { 0.01f, 3.0f };

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with one dashed accent
    /// rectangle per indicator in <paramref name="indicators"/>, coloured with
    /// <paramref name="accent"/>. An empty list clears the overlay.
    /// </summary>
    /// <param name="layer">The overlay layer to update.</param>
    /// <param name="indicators">The out-of-scale dataset extents to outline.</param>
    /// <param name="accent">Accent colour (RGB bytes) for the borders.</param>
    public static void Update(
        MemoryLayer layer,
        IReadOnlyList<DatasetExtentIndicator> indicators,
        (byte R, byte G, byte B) accent)
    {
        var features = new List<IFeature>(indicators.Count);
        var color = new MapsuiColor(accent.R, accent.G, accent.B);

        foreach (var indicator in indicators)
        {
            var extent = indicator.Extent;
            var shell = new LinearRing(new[]
            {
                new Coordinate(extent.MinX, extent.MinY),
                new Coordinate(extent.MaxX, extent.MinY),
                new Coordinate(extent.MaxX, extent.MaxY),
                new Coordinate(extent.MinX, extent.MaxY),
                new Coordinate(extent.MinX, extent.MinY),
            });

            var feature = new GeometryFeature(new Polygon(shell));
            feature.Styles.Add(new VectorStyle
            {
                Fill = null,
                Line = null,
                Outline = new Pen
                {
                    Color = color,
                    Width = OutlineWidth,
                    PenStyle = PenStyle.UserDefined,
                    PenStrokeCap = PenStrokeCap.Round,
                    DashArray = DashArray,
                },
                Opacity = OutlineOpacity,
                // Show the border only when zoomed out past the dataset's
                // content cutoff — the moment the dataset itself vanishes.
                MinVisible = indicator.MinVisibleResolution,
            });
            features.Add(feature);
        }

        layer.Features = features;
        layer.DataHasChanged();
    }
}
