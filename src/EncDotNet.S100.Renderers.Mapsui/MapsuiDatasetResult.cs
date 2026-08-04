using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Result of converting a dataset processor's Mapsui-free portrayal output
/// (<c>IVectorPortrayalSource</c> / <c>ICoveragePortrayalSource</c>) into
/// Mapsui layers. Produced by <see cref="MapsuiDatasetRenderer"/>.
/// </summary>
/// <remarks>
/// This type is owned by the Mapsui renderer because it carries Mapsui
/// <see cref="ILayer"/> and <see cref="MRect"/> types.
/// </remarks>
public sealed class MapsuiDatasetResult
{
    /// <summary>The Mapsui layers to draw, in paint order.</summary>
    public required IReadOnlyList<ILayer> Layers { get; init; }

    /// <summary>The dataset's EPSG:3857 extent.</summary>
    public required MRect Extent { get; init; }

    /// <summary>Human-readable status line describing the dataset.</summary>
    public required string Info { get; init; }

    /// <summary>The product specification (name + edition) of the rendered dataset.</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>
    /// Optional stable per-layer keys for the viewer's per-sub-layer
    /// disclosure UI, parallel by index to <see cref="Layers"/>. Processors
    /// that emit more than one sub-layer (e.g. S-101 areas + line work,
    /// S-111 colour band + arrows) populate this so the UI can show
    /// per-sub-layer toggles; single-layer products leave it null. When
    /// non-null, the list length must match <see cref="Layers"/>.
    /// </summary>
    public IReadOnlyList<string>? LayerNames { get; init; }

    /// <summary>
    /// S-98 cross-dataset stack metadata, parallel by index to
    /// <see cref="Layers"/> when supplied (every entry's
    /// <see cref="LayerStackEntry.Layer"/> appears in <see cref="Layers"/>
    /// exactly once and at the same index). A host compositor can pump every
    /// loaded dataset's entries through
    /// <see cref="LayerStackBuilder"/> to compute the global paint order
    /// across products (S-98 Annex A §4.4.1; S-98 Main §9.2.1).
    /// </summary>
    public IReadOnlyList<LayerStackEntry>? StackEntries { get; init; }

    /// <summary>
    /// The rendered dataset's coarsest intended display-scale denominator when
    /// derived from the dataset's own content rather than an exchange-set
    /// <c>CATALOG.XML</c> (S-101 in-file <c>DataCoverage.minimumDisplayScale</c>,
    /// FC §3.1.1; S-57 DSPM compilation scale, Appendix B.1 §7.3.1.1). The
    /// map session uses this as the whole-cell zoom-out window
    /// (<c>ApplyCellScaleWindow</c>) when no catalogue value is available, so a
    /// standalone-loaded cell hides — with its extent border — when zoomed out
    /// past its scale band, matching the exchange-set behaviour. Null when the
    /// product carries no cell-wide scale.
    /// </summary>
    public int? CellMinimumDisplayScale { get; init; }

    /// <summary>
    /// The rendered cell's declared data-coverage footprint in EPSG:3857
    /// (Web Mercator), projected from the processor's EPSG:4326
    /// <c>DataCoverage</c> polygons (S-101 FC §3.1.1; S-57 <c>M_COVR</c>). The
    /// map session unions the footprints of finer, overlapping
    /// in-band cells and clips this cell to its coverage minus that union,
    /// suppressing coarse-under-fine overdraw for overlapping multi-scale ENC
    /// cells (issue #438 Phase 2). Null when the cell declares no usable
    /// coverage geometry.
    /// </summary>
    public NetTopologySuite.Geometries.Geometry? CoverageGeometry { get; init; }
}
