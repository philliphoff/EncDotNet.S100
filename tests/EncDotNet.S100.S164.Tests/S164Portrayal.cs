using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// Portrays an S-101 cell from an S-164 exchange set with the bundled S-101
/// portrayal catalogue, for the Part 3 display tests.
/// </summary>
internal static class S164Portrayal
{
    private static readonly Lazy<PortrayalCatalogue> LazyCatalogue = new(() =>
    {
        using var provider = PortrayalCatalogueProvider
            .OpenAsync(Specification.CreatePortrayalCatalogueSource("S-101"))
            .GetAwaiter().GetResult();
        return provider.Catalogue;
    });

    /// <summary>The bundled S-101 portrayal catalogue.</summary>
    public static PortrayalCatalogue Catalogue => LazyCatalogue.Value;

    /// <summary>
    /// The mariner settings the Part 3 display tests (DisplayBase,
    /// DisplayStandard, DisplayOther) give: safety contour and safety depth
    /// 10 m, deep contour 30 m, shallow contour 2 m, symbolised boundaries,
    /// paper-chart symbols, two shades and short light sectors.
    /// </summary>
    public static MarinerSettings DisplayTestSettings { get; } = new()
    {
        SafetyContour = Depth.FromMetres(10),
        SafetyDepth = Depth.FromMetres(10),
        DeepContour = Depth.FromMetres(30),
        ShallowContour = Depth.FromMetres(2),
        FourShades = false,
        PlainBoundaries = false,
        SimplifiedSymbols = false,
        FullLightLines = false,
    };

    /// <summary>
    /// The viewing groups in the portrayal catalogue's display mode
    /// <paramref name="displayModeId"/> (<c>DisplayBase</c>,
    /// <c>StandardDisplay</c> or <c>OtherInformation</c>).
    /// </summary>
    public static IReadOnlySet<int> ViewingGroups(string displayModeId) =>
        DisplayModeMembership.Resolve(Catalogue, displayModeId);

    /// <summary>
    /// Portrays <c>S100_ROOT/S-101/DATASET_FILES/<paramref name="cell"/></c> from the
    /// exchange set <paramref name="exchangeSet"/> with every display category on
    /// and the mariner settings <paramref name="mariner"/>, drawing date-dependent
    /// features at <paramref name="time"/> (all of them when <see langword="null"/>).
    /// </summary>
    public static async Task<(S101DatasetProcessor Processor, IReadOnlyList<DrawingInstruction> Instructions)> PortrayAsync(
        string exchangeSet, string cell, MarinerSettings mariner, CancellationToken cancellationToken, PortrayalTime? time = null)
    {
        var processor = await OpenAsync(exchangeSet, cell, cancellationToken);
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext
            {
                EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.OtherInformation },
                Mariner = mariner,
                PortrayalTime = time,
            },
            cancellationToken);
        return (processor, result.SubLayers.SelectMany(l => l.Instructions).ToList());
    }

    /// <summary>
    /// Opens <c>S100_ROOT/S-101/DATASET_FILES/<paramref name="cell"/></c> from the
    /// exchange set <paramref name="exchangeSet"/> with the bundled S-101
    /// catalogues, resolving support files from the exchange set.
    /// </summary>
    public static async Task<S101DatasetProcessor> OpenAsync(string exchangeSet, string cell, CancellationToken cancellationToken)
    {
        var zip = await S164TestData.GetExchangeSetAsync(exchangeSet, cancellationToken);
        var source = ZipAssetSource.Create(zip, "S100_ROOT/");
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S101DatasetProcessor(
            source,
            $"S-101/DATASET_FILES/{cell}",
            catalogues,
            new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }
}
