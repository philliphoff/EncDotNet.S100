using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One dataset extent indicator: the EPSG:3857 (web-mercator) extent of a loaded
/// dataset together with the coarsest resolution at which that dataset's content
/// still draws. The accent border is shown only when the viewport is zoomed out
/// past <see cref="MinVisibleResolution"/> — i.e. exactly when the dataset itself
/// has dropped out of scale — giving a mariner a target to zoom toward.
/// </summary>
/// <param name="Extent">The dataset's EPSG:3857 (web-mercator) extent.</param>
/// <param name="MinVisibleResolution">
/// The dataset's whole-cell zoom-out cutoff (metres per pixel), beyond which the
/// dataset renders nothing, so the border is drawn from here outward (used as the
/// border style's <c>MinVisible</c>). Pass <c>0</c> to always show the border —
/// e.g. for a catalogue footprint whose cell is registered but not yet loaded.
/// </param>
public readonly record struct S100DatasetExtentIndicator(
    MRect Extent,
    double MinVisibleResolution);

/// <summary>
/// A reusable Mapsui overlay layer that outlines the extents of loaded datasets
/// which have zoomed out of scale. A host adds <see cref="Layer"/> to its
/// <c>Map.Layers</c> once, then calls <see cref="Show(IEnumerable{S100DatasetExtentIndicator})"/>
/// as datasets, visibility, or zoom-cutoffs change; <see cref="Clear"/> empties it.
/// </summary>
/// <remarks>
/// <para>
/// This is step 8's "dataset extent indicators as an optional Mapsui module": it
/// depends only on Mapsui, not on the session, a catalogue, an application
/// palette, a view model, or Avalonia. Each indicator becomes one dashed accent
/// rectangle tracing the dataset extent, with its border
/// <see cref="VectorStyle"/> gated by <c>MinVisible</c> set to the dataset's
/// content cutoff. Mapsui therefore reveals the border precisely when the
/// viewport zooms out past the point where the dataset's content stops drawing,
/// and hides it again once the mariner zooms back in — so the overlay is
/// viewport-agnostic and needs no navigator subscription.
/// </para>
/// <para>
/// The outline is deliberately thin, dashed, and unfilled so it reads as a
/// "here be data" hint rather than chart content. A host that captures dataset
/// extents in mercator (Mapsui's native map units) passes them straight through;
/// a host holding geographic bounds projects them itself (splitting any
/// antimeridian-crossing footprint into two non-wrapping boxes) — which
/// projection, and how to treat wide footprints, is host policy, so the layer
/// takes already-projected rectangles.
/// </para>
/// <para>
/// Not thread-safe: build and mutate it on the host's UI thread, like any other
/// Mapsui layer. It is cheap to rebuild, so every update replaces the layer's
/// contents wholesale.
/// </para>
/// </remarks>
public sealed class S100DatasetExtentIndicatorLayer
{
    /// <summary>Default <see cref="Mapsui.Layers.ILayer.Name"/> for the overlay.</summary>
    public const string DefaultLayerName = "S-100 Dataset Extent Indicators";

    private readonly S100DatasetExtentIndicatorStyle _style;
    private readonly MemoryLayer _layer;

    /// <summary>
    /// Creates the overlay. Add <see cref="Layer"/> to a <c>Map.Layers</c>
    /// collection at the z-order the host wants the indicators to appear (above
    /// the basemap, typically below chart content).
    /// </summary>
    /// <param name="style">Appearance; defaults to <see cref="S100DatasetExtentIndicatorStyle.Default"/>.</param>
    /// <param name="name">Layer name; defaults to <see cref="DefaultLayerName"/>.</param>
    public S100DatasetExtentIndicatorLayer(
        S100DatasetExtentIndicatorStyle? style = null,
        string? name = null)
    {
        _style = style ?? S100DatasetExtentIndicatorStyle.Default;
        _layer = new MemoryLayer
        {
            Name = name ?? DefaultLayerName,
            Style = null,
            Features = new List<IFeature>(),
        };
    }

    /// <summary>
    /// The Mapsui layer to add to a <c>Map.Layers</c> collection. Starts empty;
    /// its contents are driven by <see cref="Show(IEnumerable{S100DatasetExtentIndicator})"/>
    /// and <see cref="Clear"/>.
    /// </summary>
    public ILayer Layer => _layer;

    /// <summary>
    /// Replaces the overlay with one dashed rectangle per indicator, coloured with
    /// the style's <see cref="S100DatasetExtentIndicatorStyle.Accent"/>. An empty
    /// collection clears the overlay.
    /// </summary>
    public void Show(IEnumerable<S100DatasetExtentIndicator> indicators) =>
        Show(indicators, _style.Accent);

    /// <summary>
    /// Replaces the overlay with one dashed rectangle per indicator, coloured with
    /// <paramref name="accent"/> (overriding the style's accent so a host can
    /// re-theme without rebuilding the layer). An empty collection clears it.
    /// </summary>
    public void Show(IEnumerable<S100DatasetExtentIndicator> indicators, (byte R, byte G, byte B) accent)
    {
        ArgumentNullException.ThrowIfNull(indicators);

        var features = new List<IFeature>();
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
                    Width = _style.OutlineWidth,
                    PenStyle = PenStyle.UserDefined,
                    PenStrokeCap = PenStrokeCap.Round,
                    // Clone so the Pen can't alias (and later mutate) the style's
                    // shared array — Default is a singleton reused across pens.
                    DashArray = (float[])_style.DashArray.Clone(),
                },
                Opacity = _style.OutlineOpacity,
                // Show the border only when zoomed out past the dataset's content
                // cutoff — the moment the dataset itself vanishes.
                MinVisible = indicator.MinVisibleResolution,
            });
            features.Add(feature);
        }

        SetFeatures(features);
    }

    /// <summary>Clears the indicators, leaving the layer attached and empty.</summary>
    public void Clear() => SetFeatures(new List<IFeature>());

    private void SetFeatures(List<IFeature> features)
    {
        _layer.Features = features;
        _layer.DataHasChanged();
    }
}
