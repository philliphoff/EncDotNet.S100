using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S125.DataModel;
using EncDotNet.S100.Datasets.S125.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Dataset processor that loads an S-125 Marine Aids to Navigation GML
/// dataset, executes the bundled XSLT portrayal pipeline, and produces a
/// Mapsui layer ready for display in the viewer.
/// </summary>
/// <remarks>
/// The S-125 Edition 1.0.0 Portrayal Catalogue portrays only AtoN status
/// indications and data coverage — it is designed as an overlay on the ENC's
/// own AtoN symbology. When a Lua engine and the S-101 portrayal and feature
/// catalogues are available, this processor also portrays the aids themselves
/// with the bundled S-101 AtoN rules (an implementation choice, see
/// <see cref="S125AtonPortrayalProjection"/>), merging both outputs into one
/// layer. Without them the dataset renders the S-125 catalogue output only.
/// </remarks>
public sealed class S125DatasetProcessor : GmlDatasetProcessorBase<S125Feature>
{
    private readonly S125Dataset _dataset;
    private readonly AtonPortrayal? _atonPortrayal;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    /// <inheritdoc />
    protected override string ProductDescription => "Marine Aids to Navigation";
    /// <inheritdoc />
    protected override IReadOnlyList<S125Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S125DatasetData(_dataset);

    /// <summary>Initializes a new <see cref="S125DatasetProcessor"/>.</summary>
    /// <param name="path">Path to the S-125 GML dataset.</param>
    /// <param name="catalogueManager">Supplies the S-125 (and, for AtoN symbology, S-101) portrayal catalogues.</param>
    /// <param name="authorityProvider">Resolves the S-98 display plane.</param>
    /// <param name="featureCatalogueManager">Supplies the feature catalogues (S-125 decoding; S-101 for AtoN symbology).</param>
    /// <param name="luaEngine">
    /// Lua engine for the S-101 AtoN rules; <see langword="null"/> renders the
    /// S-125 catalogue output (status indications) only.
    /// </param>
    public S125DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null,
        ILuaEngine? luaEngine = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager, luaEngine)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S125DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    /// <param name="source">Asset source holding the dataset.</param>
    /// <param name="relativePath">Path of the dataset within <paramref name="source"/>.</param>
    /// <param name="catalogueManager">Supplies the S-125 (and, for AtoN symbology, S-101) portrayal catalogues.</param>
    /// <param name="authorityProvider">Resolves the S-98 display plane.</param>
    /// <param name="featureCatalogueManager">Supplies the feature catalogues (S-125 decoding; S-101 for AtoN symbology).</param>
    /// <param name="luaEngine">
    /// Lua engine for the S-101 AtoN rules; <see langword="null"/> renders the
    /// S-125 catalogue output (status indications) only.
    /// </param>
    public S125DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null,
        ILuaEngine? luaEngine = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            authorityProvider,
            featureCatalogueManager,
            luaEngine)
    {
    }

    private S125DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager,
        ILuaEngine? luaEngine)
        : base(
            new S125PortrayalCatalogue(catalogueManager.GetProvider("S-125")),
            featureCatalogueManager?.GetDecoder("S-125"),
            fileName,
            authorityProvider,
            "S-125")
    {
        using (datasetStream)
        {
            _dataset = S125Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);

        if (luaEngine is not null
            && featureCatalogueManager is not null
            && catalogueManager.HasCatalogue("S-101"))
        {
            _atonPortrayal = new AtonPortrayal(
                luaEngine,
                new S101PortrayalCatalogue(catalogueManager.GetProvider("S-101"), luaEngine),
                featureCatalogueManager);
        }
    }

    /// <summary>
    /// The S-101 AtoN portrayal state: the S-101 catalogue (owned by this
    /// processor so its palette / display state is not shared) and the
    /// lazily-built rule executor.
    /// </summary>
    private sealed class AtonPortrayal(
        ILuaEngine luaEngine, S101PortrayalCatalogue catalogue, FeatureCatalogueManager featureCatalogues)
    {
        public ILuaEngine LuaEngine { get; } = luaEngine;
        public S101PortrayalCatalogue Catalogue { get; } = catalogue;
        public FeatureCatalogueManager FeatureCatalogues { get; } = featureCatalogues;
        public S125AtonLuaRuleExecutor? Executor { get; set; }
    }

    // ECDIS settings that hide nothing, for renders without explicit display
    // state (mirrors the S-57 / S-101 processors).
    private static readonly EcdisDisplaySettings UnfilteredEcdisDisplay =
        new() { Category = EcdisDisplayCategory.All };

    /// <inheritdoc />
    /// <remarks>
    /// Portrays the aids to navigation with the bundled S-101 AtoN rules. The
    /// S-101 palette replaces the S-125 one: the S-125 colour profile's tokens
    /// are the S-52 / S-101 colour tokens, and the S-125 catalogue's own
    /// instructions only reference its CHNG* symbols, whose colours come from
    /// the S-125 stylesheets.
    /// </remarks>
    protected override async Task<SupplementalPortrayal?> BuildSupplementalPortrayalAsync(
        RenderContext? context, CancellationToken cancellationToken)
    {
        if (_atonPortrayal is not { } aton)
            return null;

        var fc = aton.FeatureCatalogues.GetCatalogue("S-101");
        if (fc is null)
            return null;

        aton.Executor ??= new S125AtonLuaRuleExecutor(aton.LuaEngine, _dataset, aton.Catalogue, fc);
        if (aton.Executor.FeatureCount == 0)
            return null;

        var catalogue = aton.Catalogue;
        await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);
        (context?.EcdisDisplay ?? UnfilteredEcdisDisplay).ApplyTo(catalogue);

        // Run through the vector pipeline (as the S-57 processor does) so the
        // S-101 viewing-group, display-mode and display-plane state set above
        // filters the AtoN instructions exactly as it filters an ENC's. The
        // S-101 catalogue has no XSLT rules, so only the Lua stage emits.
        var mariner = context?.Mariner ?? MarinerSettings.Default;
        var pipeline = new PortrayalPipeline(aton.Executor);
        var layer = await pipeline
            .ProcessAsync(CreateFeatureXmlSource(), catalogue, mariner: mariner, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var instructions = ((IVectorLayer)layer).Instructions;
        var prewarm = await CataloguePreWarm.ForInstructionsAsync(catalogue, instructions, cancellationToken).ConfigureAwait(false);

        return new SupplementalPortrayal(
            instructions,
            catalogue.ActivePalette,
            prewarm.ResolveSymbolSvg,
            prewarm.ResolveLineStyle,
            prewarm.ResolveAreaFill,
            $"AtoN symbology: S-101 rules ({aton.Executor.FeatureCount} aids, {instructions.Count} instructions)");
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S125FeatureXmlSource(_dataset);

    /// <inheritdoc />
    protected override IReadOnlyList<FeatureReference> BuildFeatureReferences(S125Feature feature)
    {
        var references = new List<FeatureReference>();
        foreach (var infoRef in feature.InformationReferences)
        {
            if (string.IsNullOrWhiteSpace(infoRef.InformationRef))
                continue;
            references.Add(new FeatureReference
            {
                Role = infoRef.Role,
                TargetRef = infoRef.InformationRef,
            });
        }
        return references;
    }

    /// <inheritdoc />
    protected override string BuildInfoSuffix() =>
        $"Information types: {_dataset.InformationTypes.Count}";

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S125AtonDataset.From(raw, out _),
                S125AtonRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
