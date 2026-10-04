using System.Runtime.CompilerServices;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A <see cref="MemoryLayer"/> whose features also draw at the adjacent world
/// copies (<see cref="WorldCopies"/>), so overlays tied to chart data (pick
/// highlights, extent outlines, the overscale curtain, validation findings)
/// follow the chart wherever it is drawn (issue #773). A tile-rendered
/// dataset layer whose features only carry pick identity uses it too, so a
/// click on a copy of the chart hits its features. Hosts use it for their own
/// overlays of chart data in EPSG:3857.
/// </summary>
/// <remarks>
/// <see cref="ILayer.Extent"/> stays the extent of the features themselves, so
/// the copies never widen <see cref="Map.Extent"/>.
/// </remarks>
public sealed class WorldCopyMemoryLayer : MemoryLayer
{
    private readonly WorldCopyFeatures _copies = new();

    /// <inheritdoc />
    public override IEnumerable<IFeature> GetFeatures(MRect? rect, double resolution)
    {
        if (rect is null)
        {
            yield break;
        }

        foreach (var offset in WorldCopies.OffsetsX)
        {
            // MemoryLayer grows the query by a symbol's width; so does the
            // check that skips a copy with nothing near the view.
            var shifted = WorldCopyFeatures.ShiftBack(rect, offset);
            var grow = SymbolStyle.DefaultWidth * 2.0 * resolution;
            if (offset != 0.0 && Extent?.Intersects(shifted.Grow(grow, grow)) != true)
            {
                continue;
            }

            foreach (var feature in base.GetFeatures(shifted, resolution))
            {
                yield return _copies.Get(feature, offset);
            }
        }
    }
}

/// <summary>
/// The world-copy clones of a layer's features: a clone shifted by one world,
/// built once per feature and copy and reused, so its id (which keys Mapsui's
/// path cache) is stable across frames.
/// </summary>
internal sealed class WorldCopyFeatures
{
    private readonly ConditionalWeakTable<IFeature, IFeature[]> _copies = new();

    /// <summary>
    /// <paramref name="feature"/> as drawn at the copy <paramref name="offsetX"/>
    /// metres east: the feature itself for the own frame, else its clone.
    /// </summary>
    public IFeature Get(IFeature feature, double offsetX)
    {
        if (offsetX == 0.0)
        {
            return feature;
        }

        var copies = _copies.GetValue(feature, static _ => new IFeature[2]);
        var slot = offsetX < 0 ? 0 : 1;
        return copies[slot] ??= Shift(feature, offsetX);
    }

    /// <summary>
    /// The query rectangle in the features' own frame for the copy
    /// <paramref name="offsetX"/> metres east of it.
    /// </summary>
    public static MRect ShiftBack(MRect rect, double offsetX) =>
        offsetX == 0.0 ? rect : new MRect(rect.MinX - offsetX, rect.MinY, rect.MaxX - offsetX, rect.MaxY);

    /// <summary>
    /// A clone of <paramref name="feature"/> moved <paramref name="offsetX"/>
    /// metres east, keeping its fields and styles; the original is left
    /// untouched.
    /// </summary>
    public static IFeature Shift(IFeature feature, double offsetX)
    {
        var copy = (IFeature)feature.Clone();
        copy.CoordinateVisitor((x, y, setter) => setter(x + offsetX, y));
        copy.Modified();
        return copy;
    }
}
