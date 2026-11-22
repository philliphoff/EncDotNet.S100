using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Removes overlapping labels across a whole stack of composite layers at one
/// display scale, in world space rather than in one viewport, so every tile or
/// image drawn at that scale drops the same labels (issue #1664).
/// </summary>
/// <remarks>
/// <para>
/// The rules are those of <see cref="LabelDeclutterer"/>, the viewer's
/// per-frame pass: only labels (<see cref="TextPaintOp"/>) take part; point
/// symbols are never suppressed and are never obstacles; labels that one
/// feature emits at one anchor (a composite label) never suppress each other;
/// and the label drawn on top wins. Footprints come from the same text layout
/// the renderer draws with.
/// </para>
/// <para>
/// Unlike the viewer's pass, this one runs over every layer at once. Layers
/// are visited top-most first and each layer's ops from the last drawn, so a
/// label from a layer drawn above (a finer cell, say) wins over one beneath
/// it, and labels from neighbouring cells don't collide either. A label is
/// not an obstacle when it would not be drawn: when its layer is past its
/// minimum display scale, when the op is culled by scale visibility, or when
/// its anchor lies under the coverage of an active finer cell that hides the
/// layer there.
/// </para>
/// <para>
/// Each footprint is laid out around its anchor in pixels of
/// <c>metresPerPixel</c> Web Mercator metres. The anchor is the op's own world
/// position, so the result fits layers drawn with
/// <see cref="VectorCompositeLayer.KeepAreaSymbolsInView"/> off, as tiles are.
/// </para>
/// </remarks>
public static class WorldLabelDeclutterer
{
    private const double CellSize = 64.0;

    /// <summary>
    /// Returns the labels to suppress when <paramref name="layers"/> are drawn
    /// at <paramref name="scaleDenominator"/> with
    /// <paramref name="metresPerPixel"/> Web Mercator metres per pixel.
    /// </summary>
    /// <param name="layers">The layers, bottom-most first, as they are drawn.</param>
    /// <param name="scaleDenominator">The display scale denominator the layers are drawn at.</param>
    /// <param name="metresPerPixel">Web Mercator metres per (logical) pixel.</param>
    /// <returns>The text ops to skip, by reference; empty when nothing collides.</returns>
    public static IReadOnlySet<TextPaintOp> Declutter(
        IReadOnlyList<CompositeLayer> layers, double scaleDenominator, double metresPerPixel)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (!(metresPerPixel > 0) || !double.IsFinite(metresPerPixel))
            throw new ArgumentOutOfRangeException(nameof(metresPerPixel), metresPerPixel, "Metres per pixel must be positive and finite.");

        var suppressed = new HashSet<TextPaintOp>();
        var index = new Dictionary<(long X, long Y), List<Placed>>();
        using var scratch = new SkiaDisplayListRenderer.TextDrawScratch();
        var active = new List<FinerCoverage>();

        for (int layerIndex = layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            if (layers[layerIndex] is not VectorCompositeLayer layer || layer.IsPastMinimumDisplayScale(scaleDenominator))
                continue;

            active.Clear();
            foreach (var coverage in layer.FinerCoverages)
            {
                if (!coverage.IsEmpty && coverage.IsActiveAt(scaleDenominator, layer.HonorScaleVisibility))
                    active.Add(coverage);
            }

            var ops = layer.Scene.Ops;
            for (int i = ops.Count - 1; i >= 0; i--)
            {
                if (ops[i] is not TextPaintOp text || string.IsNullOrEmpty(text.Text))
                    continue;
                if (layer.HonorScaleVisibility && !ScaleVisibility.IsVisibleAtScale(text, scaleDenominator))
                    continue;
                if (IsUnderCoverage(active, text.World))
                    continue;

                // Laid out around a zero anchor, then placed in world pixels in
                // double precision: pixel coordinates reach millions at high zoom.
                var rect = SkiaDisplayListRenderer.LayoutText(text, 0f, 0f, scratch.FontFor(text), scratch.Paint).Background;
                double x = text.World.X / metresPerPixel;
                double y = -text.World.Y / metresPerPixel;
                var placed = new Placed(
                    x + rect.Left, y + rect.Top, x + rect.Right, y + rect.Bottom,
                    new Owner(layerIndex, text.FeatureReference, text.World));

                if (Intersects(index, placed))
                    suppressed.Add(text);
                else
                    Add(index, placed);
            }
        }

        return suppressed;
    }

    private static bool IsUnderCoverage(List<FinerCoverage> coverages, (double X, double Y) world)
    {
        foreach (var coverage in coverages)
        {
            if (coverage.Contains(world.X, world.Y))
                return true;
        }

        return false;
    }

    private static void Add(Dictionary<(long X, long Y), List<Placed>> index, Placed placed)
    {
        var (minX, minY, maxX, maxY) = CellRange(placed);
        for (long cy = minY; cy <= maxY; cy++)
        {
            for (long cx = minX; cx <= maxX; cx++)
            {
                if (!index.TryGetValue((cx, cy), out var list))
                    index[(cx, cy)] = list = new List<Placed>(4);
                list.Add(placed);
            }
        }
    }

    private static bool Intersects(Dictionary<(long X, long Y), List<Placed>> index, Placed placed)
    {
        var (minX, minY, maxX, maxY) = CellRange(placed);
        for (long cy = minY; cy <= maxY; cy++)
        {
            for (long cx = minX; cx <= maxX; cx++)
            {
                if (!index.TryGetValue((cx, cy), out var list))
                    continue;
                foreach (var other in list)
                {
                    if (placed.Left < other.Right && other.Left < placed.Right
                        && placed.Top < other.Bottom && other.Top < placed.Bottom
                        && placed.Owner != other.Owner)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static (long MinX, long MinY, long MaxX, long MaxY) CellRange(Placed placed) => (
        (long)Math.Floor(placed.Left / CellSize),
        (long)Math.Floor(placed.Top / CellSize),
        (long)Math.Floor(placed.Right / CellSize),
        (long)Math.Floor(placed.Bottom / CellSize));

    /// <summary>
    /// A composite label: the labels one feature of one layer emits at one
    /// anchor. Labels sharing an owner never collide with each other.
    /// </summary>
    private readonly record struct Owner(int Layer, string FeatureReference, (double X, double Y) World);

    private readonly record struct Placed(double Left, double Top, double Right, double Bottom, Owner Owner);
}
