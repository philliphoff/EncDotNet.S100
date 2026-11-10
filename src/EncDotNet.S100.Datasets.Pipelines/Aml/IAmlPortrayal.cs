using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Draws an AML cell: the one place an AML portrayal plugs into
/// <see cref="AmlDatasetProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The official AML portrayal is restricted, so SoundCharts draws AML with its
/// own. Until that portrayal exists, <see cref="AmlNotDrawnPortrayal"/> draws
/// nothing: the cell is "recognised, not drawn", its features still pickable.
/// A portrayal registers by passing itself to the processor, which
/// <see cref="S100Products.Aml"/> does through <see cref="S100Products.AmlPortrayal"/>.
/// </para>
/// <para>
/// Whatever it draws, it draws on
/// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.AdditionalMilitaryLayers"/>.
/// AML never feeds depth answers or route checks.
/// </para>
/// </remarks>
public interface IAmlPortrayal
{
    /// <summary>
    /// Whether this portrayal draws anything. Hosts report a cell as
    /// "recognised, not drawn" when it doesn't.
    /// </summary>
    bool Draws { get; }

    /// <summary>
    /// The drawing for an AML cell.
    /// </summary>
    /// <param name="cell">The cell: its AML facts, features and coverage.</param>
    /// <param name="context">The render context (palette, scale, display settings), or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <returns>The sub-layers and the providers they draw with.</returns>
    Task<VectorPortrayalResult> PortrayAsync(AmlPortrayalInput cell, RenderContext? context, CancellationToken cancellationToken);
}

/// <summary>What an <see cref="IAmlPortrayal"/> draws an AML cell from.</summary>
public sealed record AmlPortrayalInput
{
    /// <summary>The cell's AML facts: product, version, marking, scale band.</summary>
    public required AmlDatasetInfo Info { get; init; }

    /// <summary>The parsed (and update-folded) S-57 document.</summary>
    public required EncDotNet.S57.S57Document Document { get; init; }

    /// <summary>The cell's object catalogue (AML 1.0, 2.1 or 3.0).</summary>
    public required EncDotNet.S57.S57ObjectCatalogue Catalogue { get; init; }

    /// <summary>
    /// The cell's features as read by <see cref="S57ChartFeatures.Read"/>:
    /// class and attribute acronyms, geometry in WGS-84 degrees.
    /// </summary>
    public required IReadOnlyList<Feature> Features { get; init; }

    /// <summary>The cell's coverage: its <c>M_COVR</c> areas, or its bounding box.</summary>
    public required IReadOnlyList<CoverageArea> Coverage { get; init; }

    /// <summary>The processor's spec (<c>AML</c> and the cell's edition).</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>The cell's file name, the dataset id of the result.</summary>
    public required string FileName { get; init; }

    /// <summary>A stable scope for caches keyed on the cell's content.</summary>
    public required string ContentScope { get; init; }
}

/// <summary>
/// The AML portrayal in use until SoundCharts has its own: it draws nothing,
/// so AML cells are "recognised, not drawn".
/// </summary>
public sealed class AmlNotDrawnPortrayal : IAmlPortrayal
{
    /// <summary>The single instance.</summary>
    public static AmlNotDrawnPortrayal Instance { get; } = new();

    private AmlNotDrawnPortrayal()
    {
    }

    /// <inheritdoc />
    public bool Draws => false;

    /// <inheritdoc />
    public Task<VectorPortrayalResult> PortrayAsync(AmlPortrayalInput cell, RenderContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return Task.FromResult(new VectorPortrayalResult
        {
            SubLayers = [],
            Palette = ColorPalette.Default,
            GeometryProvider = new FeatureGeometryProvider<Feature>(cell.Features),
            Product = AmlDatasetInfo.SpecName,
            Spec = cell.Spec,
            SourceDatasetId = cell.FileName,
            ContentScope = cell.ContentScope,
            Info = $"{cell.FileName}: {cell.Info.TagText}, recognised, not drawn",
            CoverageAreas = cell.Coverage,
        });
    }
}
