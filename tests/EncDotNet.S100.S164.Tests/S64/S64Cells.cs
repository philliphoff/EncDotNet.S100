using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// Opens S-57 cells from the S-64 test data through the S-57 translation
/// pipeline (<see cref="S57DatasetProcessor"/>: S-57 translated to S-101 and
/// portrayed with the bundled S-101 portrayal catalogue).
/// </summary>
internal static class S64Cells
{
    /// <summary>The S-101 PC draws this symbol when a rule fails or a feature is unknown.</summary>
    public const string QuestionMark = "QUESMRK1";

    /// <summary>
    /// Opens the base cell <paramref name="cell"/> with the update files
    /// <paramref name="updates"/> folded in, in the order given. Both are paths
    /// relative to the archive root, such as
    /// <c>2.2.5 Good Base Cells/ENC_ROOT/GB5X01SW.000</c>.
    /// </summary>
    public static async Task<S57DatasetProcessor> OpenAsync(string cell, IReadOnlyList<string> updates, CancellationToken cancellationToken)
    {
        var source = await S64TestData.OpenSourceAsync(cancellationToken);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S57DatasetProcessor(
            source,
            cell,
            updates,
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }

    /// <summary>
    /// Portrays <paramref name="processor"/> with every display category on
    /// and the mariner settings <paramref name="mariner"/>.
    /// </summary>
    public static async Task<IReadOnlyList<DrawingInstruction>> PortrayAsync(
        S57DatasetProcessor processor, MarinerSettings mariner, CancellationToken cancellationToken)
    {
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext
            {
                EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.OtherInformation },
                Mariner = mariner,
            },
            cancellationToken);
        return result.SubLayers.SelectMany(l => l.Instructions).ToList();
    }

    /// <summary>
    /// The S-101 feature types in <paramref name="instructions"/> that drew
    /// <see cref="QuestionMark"/>; an S-57 object of a class translation has
    /// no rule for goes by its pick-report name, such as
    /// <c>"Unknown S-57 object NEWOBJ (163)"</c>.
    /// </summary>
    public static IReadOnlyList<string> QuestionMarks(S57DatasetProcessor processor, IEnumerable<DrawingInstruction> instructions) =>
        instructions.OfType<PointInstruction>()
            .Where(p => p.SymbolReference == QuestionMark)
            .Select(p => processor.GetFeatureInfo(p.FeatureReference) is { } info
                ? info.FeatureType == S57UnknownObject.FeatureTypeCode ? info.FeatureTypeName! : info.FeatureType
                : p.FeatureReference)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
}
