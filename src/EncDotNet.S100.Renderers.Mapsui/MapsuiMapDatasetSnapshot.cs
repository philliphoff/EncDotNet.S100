using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using Mapsui;
using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Immutable snapshot of one dataset managed by a
/// <see cref="MapsuiDatasetLayerSession"/>.
/// </summary>
public sealed class MapsuiMapDatasetSnapshot
{
    /// <summary>Gets the renderer-neutral dataset state.</summary>
    public required MapDataset Dataset { get; init; }

    /// <summary>Gets the processor-rendered layers before host projection.</summary>
    public required IReadOnlyList<ILayer> Layers { get; init; }

    /// <summary>
    /// Gets stable layer keys parallel to <see cref="Layers"/>, or
    /// <see langword="null"/> when the processor supplied none.
    /// </summary>
    public IReadOnlyList<string>? LayerKeys { get; init; }

    /// <summary>Gets the processor-supplied interoperability entries.</summary>
    public IReadOnlyList<LayerStackEntry>? StackEntries { get; init; }

    /// <summary>Gets the rendered EPSG:3857 extent, or <see langword="null"/>.</summary>
    public MRect? Extent { get; init; }

    /// <summary>Gets the renderer's human-readable information line.</summary>
    public string? Info { get; init; }

    /// <summary>Gets the EPSG:3857 data-coverage geometry, or <see langword="null"/>.</summary>
    public Geometry? CoverageGeometry { get; init; }

    /// <summary>
    /// Gets the effective coarsest display-scale denominator. A catalogue value
    /// takes precedence over the processor-derived value.
    /// </summary>
    public int? MinimumDisplayScale { get; init; }

    /// <summary>Gets the dataset compilation-scale denominator, or <see langword="null"/>.</summary>
    public int? MaximumDisplayScale { get; init; }

    /// <summary>
    /// Gets the coarsest EPSG:3857 resolution at which dataset content draws, or
    /// <see langword="null"/> when no whole-cell scale window applies.
    /// </summary>
    public double? ContentMaxVisibleResolution { get; init; }

    /// <summary>
    /// Gets whether at least one generated layer is currently enabled with
    /// non-zero opacity.
    /// </summary>
    public bool IsDrawing { get; init; }
}
