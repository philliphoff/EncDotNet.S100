using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S57;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// SoundCharts' own AML portrayal (<see cref="AmlPortrayal"/> in
/// <c>EncDotNet.S100.Datasets.Aml</c>) behind the <see cref="IAmlPortrayal"/>
/// seam: the overlay signature in the AML colour families, on
/// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.AdditionalMilitaryLayers"/>
/// (epic #1369, slice S5).
/// </summary>
/// <remarks>
/// <para>
/// A cell is drawn when any of its classes has a style in the
/// <see cref="AmlStyleCatalog"/>; classes without one stay pickable. The
/// drawing names colour tokens, so it is made once per cell (per parsed
/// document) and reused for every palette.
/// </para>
/// <para>
/// Not the official AML portrayal, which is restricted. AML never feeds depth
/// answers or route checks.
/// </para>
/// </remarks>
public sealed class AmlOverlayPortrayal : IAmlPortrayal
{
    private readonly AmlPortrayal _portrayal;
    private readonly ConditionalWeakTable<S57Document, AmlDrawing> _drawings = new();

    /// <summary>Creates the portrayal with <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The styles.</param>
    public AmlOverlayPortrayal(AmlStyleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _portrayal = new AmlPortrayal(catalog);
    }

    /// <summary>The portrayal with the built-in styles (<see cref="AmlStyleCatalog.Default"/>).</summary>
    public static AmlOverlayPortrayal Default { get; } = new(AmlStyleCatalog.Default);

    /// <summary>The styles.</summary>
    public AmlStyleCatalog Catalog => _portrayal.Catalog;

    /// <inheritdoc />
    public bool Draws => Catalog.Classes.Count > 0;

    /// <inheritdoc />
    public bool DrawsAny(IReadOnlyList<Feature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        return features.Any(f => Catalog.Find(f.FeatureType) is not null);
    }

    /// <inheritdoc />
    public Task<VectorPortrayalResult> PortrayAsync(AmlPortrayalInput cell, RenderContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cancellationToken.ThrowIfCancellationRequested();
        var drawing = _drawings.GetValue(cell.Document, document => _portrayal.Draw(new AmlDataSet(document), cell.FileName));
        return Task.FromResult(drawing.ToVectorPortrayalResult(cell.Spec, cell.FileName, context, cell.ContentScope, cell.Coverage));
    }
}
