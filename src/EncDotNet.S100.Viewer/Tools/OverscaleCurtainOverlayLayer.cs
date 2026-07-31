using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Builds and updates the Mapsui <see cref="MemoryLayer"/> overlay that paints
/// the on-chart overscale "curtain" (issue #441, S-52 / S-101
/// <c>AP(OVERSC01)</c> Form A) — a subtle pattern of evenly spaced vertical
/// lines over the region of each cell being displayed beyond its compilation
/// scale (see <see cref="OverscaleCurtain"/> for the region maths).
/// </summary>
/// <remarks>
/// Each overscale region is filled with an <see cref="OverscaleCurtainStyle"/>,
/// whose renderer draws world-anchored vertical strokes clipped to the region.
/// The pattern stays crisp at any zoom and on HiDPI surfaces, moves with the
/// chart during panning, and is re-projected/clipped per frame in screen space
/// (no cached-tile invalidation). The lines are deliberately thin, widely
/// spaced, and semi-transparent so the chart reads through — an
/// <em>indication</em>, never an obstruction.
/// </remarks>
internal static class OverscaleCurtainOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Overscale Curtain";

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with one curtain-filled
    /// polygon per region in <paramref name="regions"/>. An empty list clears the
    /// overlay.
    /// </summary>
    /// <param name="layer">The overlay layer to update.</param>
    /// <param name="regions">The overscale-curtain regions to paint.</param>
    public static void Update(MemoryLayer layer, IReadOnlyList<OverscaleRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(regions);

        var features = new List<IFeature>(regions.Count);

        foreach (var region in regions)
        {
            if (region.Region is not { IsEmpty: false } geometry)
                continue;

            var feature = new GeometryFeature(geometry);
            feature.Styles.Add(new OverscaleCurtainStyle());
            features.Add(feature);
        }

        layer.Features = features;
        layer.DataHasChanged();
    }
}
