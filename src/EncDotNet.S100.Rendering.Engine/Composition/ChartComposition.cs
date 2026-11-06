using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// A chart composite whose S-98 ordering, suppression and scene lowering are
/// done, ready to paint against any number of viewports. Build one with
/// <see cref="ChartCompositor.Prepare"/>.
/// </summary>
/// <remarks>
/// A composition is immutable; <see cref="Render(Viewport, float)"/> and
/// <see cref="Draw"/> may be called concurrently from several threads.
/// </remarks>
public sealed class ChartComposition
{
    private readonly IReadOnlyList<Entry> _entries;
    private readonly bool _hasBounds;
    private readonly double _minX;
    private readonly double _minY;
    private readonly double _maxX;
    private readonly double _maxY;

    internal ChartComposition(IReadOnlyList<Entry> entries, SeamAwareBoundsAccumulator bounds, ChartCompositeOptions options)
    {
        _entries = entries;
        Layers = [.. entries.Select(e => e.Layer)];
        Background = options.Background;
        Basemap = options.Basemap;
        HonorScaleVisibility = options.HonorScaleVisibility;
        _hasBounds = bounds.TryResolve(out _minX, out _minY, out _maxX, out _maxY);
    }

    /// <summary>The lowered layers, bottom-most first.</summary>
    public IReadOnlyList<CompositeLayer> Layers { get; }

    /// <summary>The background <see cref="Render(Viewport, float)"/> clears to.</summary>
    public RgbaColor Background { get; }

    /// <summary>The basemap drawn beneath the chart layers.</summary>
    public BasemapKind Basemap { get; }

    /// <summary>Whether the vector layers apply scale-visibility culling.</summary>
    public bool HonorScaleVisibility { get; }

    /// <summary>
    /// Gets the EPSG:3857 union extent of the active layers. The extent is
    /// seam-aware: for data crossing the antimeridian, <paramref name="maxX"/>
    /// may lie east of +180° (see <see cref="SeamAwareBoundsAccumulator"/>).
    /// </summary>
    /// <param name="minX">The western edge, in metres.</param>
    /// <param name="minY">The southern edge, in metres.</param>
    /// <param name="maxX">The eastern edge, in metres.</param>
    /// <param name="maxY">The northern edge, in metres.</param>
    /// <returns><see langword="true"/> when any layer has geometry; otherwise <see langword="false"/>.</returns>
    public bool TryGetWorldBounds(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = _minX;
        minY = _minY;
        maxX = _maxX;
        maxY = _maxY;
        return _hasBounds;
    }

    /// <summary>
    /// A north-up viewport of <paramref name="widthPixels"/> ×
    /// <paramref name="heightPixels"/> framing the union extent with a 10%
    /// margin, grown to the output's aspect.
    /// </summary>
    /// <param name="widthPixels">The viewport width, in device-independent pixels.</param>
    /// <param name="heightPixels">The viewport height, in device-independent pixels.</param>
    /// <returns>The fitted viewport.</returns>
    public Viewport FitViewport(int widthPixels, int heightPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        if (!TryGetWorldBounds(out double minX, out double minY, out double maxX, out double maxY))
        {
            minX = -1000; minY = -1000; maxX = 1000; maxY = 1000;
        }

        double spanX = maxX - minX;
        double spanY = maxY - minY;

        // Pad 10% (guard degenerate zero-span extents), matching the
        // single-dataset HeadlessVectorRenderer.FitViewport behaviour.
        double padX = spanX > 0 ? spanX * 0.1 : 1000;
        double padY = spanY > 0 ? spanY * 0.1 : 1000;
        minX -= padX; maxX += padX;
        minY -= padY; maxY += padY;
        spanX = maxX - minX;
        spanY = maxY - minY;

        // Expand the smaller dimension so the extent's aspect matches the output.
        double viewAspect = (double)widthPixels / heightPixels;
        double dataAspect = spanX / spanY;
        if (dataAspect > viewAspect)
        {
            double grow = (spanX / viewAspect - spanY) / 2.0;
            minY -= grow; maxY += grow;
        }
        else
        {
            double grow = (spanY * viewAspect - spanX) / 2.0;
            minX -= grow; maxX += grow;
        }

        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY);

        double midLatRad = (minLat + maxLat) * 0.5 * Math.PI / 180.0;
        double groundMetresPerPixel = (maxX - minX) / widthPixels * Math.Cos(midLatRad);
        double denom = groundMetresPerPixel / ScaleVisibility.DenomToResolutionMetres;

        return new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = widthPixels,
            HeightPixels = heightPixels,
            ScaleDenominator = denom > 0 ? denom : 1.0,
        };
    }

    /// <summary>
    /// Renders the composition into a new bitmap cleared to <see cref="Background"/>.
    /// </summary>
    /// <param name="viewport">The viewport, in device-independent pixels; it may be rotated.</param>
    /// <returns>A newly allocated bitmap of the viewport's pixel size, owned by the caller.</returns>
    public SKBitmap Render(Viewport viewport) => Render(viewport, deviceScale: 1f);

    /// <summary>
    /// Renders the composition into a new bitmap cleared to <see cref="Background"/>,
    /// at <paramref name="deviceScale"/> image pixels per viewport pixel.
    /// </summary>
    /// <param name="viewport">The viewport, in device-independent pixels; it may be rotated.</param>
    /// <param name="deviceScale">Image pixels per viewport pixel; positive and finite.</param>
    /// <returns>
    /// A newly allocated bitmap of the viewport's size times <paramref name="deviceScale"/>,
    /// rounded, owned by the caller.
    /// </returns>
    public SKBitmap Render(Viewport viewport, float deviceScale)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var renderer = new HeadlessCompositeRenderer { Background = Background };
        return renderer.Render(viewport, WithBasemap(viewport), deviceScale);
    }

    /// <summary>
    /// Paints the basemap and the layers onto <paramref name="canvas"/> against
    /// a north-up <paramref name="viewport"/>, without clearing it. Each layer is
    /// also drawn shifted by each of <paramref name="longitudeOffsets"/> (in
    /// degrees, for example −360 and +360), so data stored one world east or
    /// west of the viewport shows up in it; layer order is kept across the copies.
    /// </summary>
    /// <remarks>
    /// A layer whose drawing in the viewport would be erased entirely by one
    /// finer cell's coverage is not drawn at all; the picture is the same.
    /// </remarks>
    /// <param name="canvas">The canvas to paint onto.</param>
    /// <param name="viewport">A north-up viewport.</param>
    /// <param name="longitudeOffsets">Extra world copies to draw, in degrees; may be empty.</param>
    public void Draw(SKCanvas canvas, Viewport viewport, IReadOnlyList<double> longitudeOffsets)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(longitudeOffsets);

        if (Basemap == BasemapKind.Offline)
        {
            new VectorCompositeLayer(NaturalEarthBasemap.GetLandScene(viewport), honorScaleVisibility: false)
                .Draw(canvas, viewport);
        }

        foreach (var entry in _entries)
        {
            DrawEntry(canvas, entry, viewport);
            foreach (var offset in longitudeOffsets)
            {
                // Data stored at longitude L + offset lands where L would.
                DrawEntry(canvas, entry, viewport with
                {
                    MinLongitude = viewport.MinLongitude + offset,
                    MaxLongitude = viewport.MaxLongitude + offset,
                });
            }
        }
    }

    private void DrawEntry(SKCanvas canvas, Entry entry, Viewport viewport)
    {
        if (IsHidden(entry, viewport))
        {
            ChartRenderTelemetry.LayersSkipped.Add(1);
            return;
        }

        entry.Layer.Draw(canvas, viewport);
    }

    /// <summary>
    /// Whether one active finer coverage covers the viewport, grown by a pixel
    /// so the clip's anti-aliased edge never leaks a partly covered pixel. A
    /// single coverage, not a union: see <see cref="HiddenCoverage"/>.
    /// </summary>
    private bool IsHidden(Entry entry, Viewport viewport)
    {
        if (entry.Finer.Count == 0 || viewport.RotationDegrees % 360.0 != 0)
            return false;

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        double padX = (maxX - minX) / viewport.WidthPixels;
        double padY = (maxY - minY) / viewport.HeightPixels;
        foreach (var finer in entry.Finer)
        {
            if (finer.Clip.IsActiveAt(viewport.ScaleDenominator, HonorScaleVisibility)
                && finer.Footprint.Covers(minX - padX, minY - padY, maxX + padX, maxY + padY))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<CompositeLayer> WithBasemap(Viewport viewport)
    {
        if (Basemap != BasemapKind.Offline)
            return Layers;

        // The land basemap draws under every chart layer, registered with the
        // viewport, at a level of detail matching its pixel size (#411, #731).
        var layers = new List<CompositeLayer>(Layers.Count + 1)
        {
            new VectorCompositeLayer(NaturalEarthBasemap.GetLandScene(viewport), honorScaleVisibility: false),
        };
        layers.AddRange(Layers);
        return layers;
    }

    /// <summary>A lowered layer and the finer cells that may hide it.</summary>
    /// <param name="Layer">The layer.</param>
    /// <param name="Finer">The finer, overlapping cells' coverages; empty for none.</param>
    internal sealed record Entry(CompositeLayer Layer, IReadOnlyList<CellOverlap> Finer);
}
