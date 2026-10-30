using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Datasets.S57.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Renders an S-57 ENC base cell by translating it in-memory to an
/// <see cref="S101Document"/> and reusing the S-101 portrayal pipeline.
/// A maritime ENC is translated into, and portrayed with, S-101; an inland ENC
/// (DSID <c>PRSP</c> = <see cref="S57ProductSpecification.InlandElectronicNavigationalChart"/>)
/// is translated into S-401 and portrayed with the S-401 catalogues (issue
/// #608), provided the host has an S-401 portrayal catalogue (otherwise it
/// falls back to S-101). Either way the product identity stays S-57. Symbology is S-100
/// (not S-52); coverage is breadth-first.
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S57"/> registration, or
/// through <see cref="DatasetPipelineFactory.CreateS57ProcessorWithUpdates"/>
/// to fold a base cell's in-set update files into the S-57 document before
/// translation. The raw S-57 document is retained so validation can run
/// S-57 pre-translation rules against fields the translation drops.
/// </remarks>
public sealed class S57DatasetProcessor : IDatasetProcessor, IVectorPortrayalSource, IHeadlessImageRenderer, ILoadedDatasetProjection, IChartDepthCell
{
    // Serialises render calls so the catalogue's mutable palette / ECDIS
    // state is not mutated mid-build by a concurrent render.
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly EncDotNet.S57.S57Document _rawS57Document;
    private readonly S101Dataset _translatedDataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S101PortrayalCatalogue _catalogue;
    private readonly ILuaEngine _luaEngine;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;
    private readonly string _contentScope;

    /// <summary>
    /// The S-100 product this cell is translated into, chosen from its declared
    /// S-57 product specification. Its <see cref="S57TranslationTarget.Spec"/>
    /// names the Feature and Portrayal Catalogues this processor uses.
    /// </summary>
    private readonly S57TranslationTarget _target;
    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>? _featureIndex;
    private (int? RankingScale, IReadOnlyList<CoverageArea> Coverage)? _cellExtent;
    private EncDotNet.S100.Features.FeatureCatalogueDecoder? _decoder;
    private bool _decoderLoaded;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    private DatasetMetadata? _metadata;

    // Single-entry cache of the unfiltered Lua rule output, keyed by the
    // mariner settings (S101DatasetProcessor.BuildRuleOutputCacheKey). A
    // display-category, viewing-group or display-plane change only re-filters
    // it instead of re-running the Part 9A rules. Guarded by _renderGate.
    private string? _cachedRuleKey;
    private IReadOnlyList<DrawingInstruction>? _cachedRuleOutput;

    /// <summary>
    /// How many times this processor has run the Part 9A Lua rules. A render
    /// that only changes the ECDIS display state re-filters the cached rule
    /// output and doesn't count. Test seam.
    /// </summary>
    internal int RuleRunCount { get; private set; }

    // Memoized prefix of the pattern-clip cache key; see GetPatternClipScopeAsync.
    private string? _patternClipScope;

    // Sub-layer keys of the S-98 fill / line-work split (mirrors the S-101
    // processor's "<spec>.areas" / "<spec>.linework" keys).
    private const string AreasLayerKey = "s57.areas";
    private const string LineworkLayerKey = "s57.linework";

    // ECDIS settings that hide nothing — used when a render context carries no
    // explicit display state, so a standalone/headless render draws everything
    // the catalogue can (Category.All maps to a null display mode). Mirrors
    // S101DatasetProcessor.UnfilteredEcdisDisplay.
    private static readonly EcdisDisplaySettings UnfilteredEcdisDisplay =
        new() { Category = EcdisDisplayCategory.All };

    /// <inheritdoc/>
    /// <remarks>
    /// Always <c>S-57</c> with no edition, whichever product the cell is
    /// translated into; see <see cref="PortrayalSpec"/> for that.
    /// </remarks>
    public SpecRef Spec => new("S-57", default);

    /// <summary>
    /// The specification whose catalogues portray this cell: S-101 for a
    /// maritime ENC, S-401 for an inland ENC. Unlike the default
    /// <see cref="ProductRegistration.PortrayalSpec"/> trait (S-101) this depends on the cell, not just
    /// on its S-57 identity.
    /// </summary>
    public SpecRef PortrayalSpec => new(_target.Spec, default);

    /// <summary>
    /// The lightweight <see cref="DatasetMetadata"/> for this cell — canonical
    /// <c>S-57</c> specification, WGS-84 extent, and compilation-scale display
    /// window — derived from the already-parsed (update-folded) document without
    /// a second read, and memoized (issue #460 / #467 WS1). The extent folds the
    /// raw spatial coordinates directly rather than going through the S-101
    /// translation, so it is a cheap byproduct.
    /// </summary>
    public DatasetMetadata Metadata => _metadata ??= S57Dataset.ReadMetadata(_rawS57Document);

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() =>
        new S101DatasetData(_translatedDataset, CompilationScale: S57Dataset.ResolveCompilationScale(_rawS57Document));

    /// <summary>
    /// Initializes a new <see cref="S57DatasetProcessor"/> by reading the S-57
    /// base cell at <paramref name="path"/> and translating it in memory.
    /// </summary>
    /// <param name="path">Path to the S-57 ISO 8211 base cell (<c>….000</c>).</param>
    /// <param name="catalogueManager">
    /// Supplies the portrayal catalogue of the translation target (S-101, or
    /// S-401 for an inland cell when that catalogue is available).
    /// </param>
    /// <param name="luaEngine">Lua engine that executes the portrayal catalogue's rules.</param>
    /// <param name="featureCatalogueManager">
    /// Supplies the feature catalogue used to decode feature and attribute
    /// names in feature info.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public S57DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(OpenFromFile(path), Path.GetFileName(path), catalogueManager, luaEngine, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S57DatasetProcessor"/> by reading
    /// the ISO 8211 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S57DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(
            OpenFromSource(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            luaEngine,
            featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S57DatasetProcessor"/> by reading the base
    /// cell <paramref name="baseRelativePath"/> from <paramref name="source"/>
    /// and applying the in-set sequential update files at
    /// <paramref name="updateRelativePaths"/> (in ascending update-number order)
    /// before translation. Used by exchange-set bulk loading to collapse a cell
    /// and its updates into a single up-to-date dataset (S-57 Part 3, dataset
    /// updating).
    /// </summary>
    /// <remarks>
    /// Updates are folded into the <see cref="EncDotNet.S57.S57Document"/>
    /// <em>before</em> the S-57 → S-101 translation runs; the translator and
    /// portrayal pipeline only ever see the fully-updated document.
    /// </remarks>
    public S57DatasetProcessor(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(
            OpenFromSource(source, baseRelativePath, updateRelativePaths),
            AssetSourceHelpers.GetFileName(baseRelativePath),
            catalogueManager,
            luaEngine,
            featureCatalogueManager)
    {
    }

    private S57DatasetProcessor(
        (S57Dataset Dataset, string ContentHash) opened,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
    {
        var s57 = opened.Dataset;
        ArgumentNullException.ThrowIfNull(s57);
        _fileName = fileName;
        _luaEngine = luaEngine;
        _catalogueManager = catalogueManager;
        _target = SelectTarget(s57, catalogueManager);
        _provider = catalogueManager.GetProvider(_target.Spec);
        _catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);
        _featureCatalogueManager = featureCatalogueManager;

        // Retain the raw S-57 document so the pre-translation
        // validation pack (S57PreTranslationRules) can run against
        // fields that do not survive translation — see
        // docs/design/non-gml-validation.md §9.3.
        _rawS57Document = s57.Document;
        var translator = S57ToS101Translator.ForTarget(_target);
        var s101Doc = translator.Translate(s57);
        _translatedDataset = S101Dataset.FromDocument(s101Doc);

        // S-57 datasets render through the target product's portrayal catalogue.
        Diagnostics.CatalogueResolutionDiagnostics.Report(this, PortrayalSpec, _catalogue.CatalogueRef, "portrayal");

        _contentScope = $"ds:{opened.ContentHash}|target:{_target.Spec}|pc:{_catalogue.CatalogueRef}";
    }

    /// <summary>
    /// The product to translate <paramref name="s57"/> into: the one its
    /// declared S-57 product specification calls for, unless the host has no
    /// portrayal catalogue for that product, in which case the cell is
    /// translated into S-101 as before — so a host that registers only the
    /// S-101 catalogue still loads inland cells.
    /// </summary>
    private static S57TranslationTarget SelectTarget(
        S57Dataset s57, PortrayalCatalogueManager catalogueManager)
    {
        var target = s57.TranslationTarget;
        return catalogueManager.HasCatalogue(target.Spec) ? target : S57TranslationTarget.S101;
    }

    private static (S57Dataset, string) OpenFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Stream stream = File.OpenRead(path);
        var contentHash = DatasetContentHash.HashAndRewind(ref stream);
        using (stream)
            return (S57Dataset.Open(stream), contentHash);
    }

    private static (S57Dataset, string) OpenFromSource(IAssetSource source, string relativePath)
    {
        var stream = AssetSourceHelpers.OpenSeekable(source, relativePath);
        var contentHash = DatasetContentHash.HashAndRewind(ref stream);
        using (stream)
            return (S57Dataset.Open(stream), contentHash);
    }

    private static (S57Dataset, string) OpenFromSource(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(updateRelativePaths);

        var updateStreams = new List<Stream>(updateRelativePaths.Count);
        var baseStream = AssetSourceHelpers.OpenSeekable(source, baseRelativePath);
        try
        {
            foreach (var updatePath in updateRelativePaths)
                updateStreams.Add(AssetSourceHelpers.OpenSeekable(source, updatePath));

            var contentHash = DatasetContentHash.HashAndRewind([baseStream, .. updateStreams]);
            return (S57Dataset.Open(baseStream, updateStreams), contentHash);
        }
        finally
        {
            baseStream.Dispose();
            foreach (var stream in updateStreams)
                stream.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildVectorPortrayalCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private async Task<VectorPortrayalResult> BuildVectorPortrayalCoreAsync(RenderContext? context, CancellationToken cancellationToken)
    {
        var mariner = context?.Mariner ?? MarinerSettings.Default;

        var fc = RequireFeatureCatalogue();

        Console.WriteLine($"[S57] Translated to {_target.Spec} in-memory; running Part 9 portrayal pipeline...");

        var s101Cat = _catalogue;
        var paletteType = context?.Palette ?? PaletteType.Day;
        await s101Cat.SwitchPaletteAsync(paletteType, cancellationToken).ConfigureAwait(false);
        var palette = s101Cat.ActivePalette;

        // Activate the ECDIS display-mode / category and write the hidden
        // viewing-group overrides before portrayal. The catalogue's Spec.Name
        // is this cell's portrayal spec (S-101 or S-401), so ApplyTo keys that
        // product's category mapping and default-hidden VGs correctly.
        (context?.EcdisDisplay ?? UnfilteredEcdisDisplay).ApplyTo(s101Cat);

        var (prepared, unfiltered, ruleKey) = await GetDisplayInstructionsAsync(mariner, fc, cancellationToken).ConfigureAwait(false);

        // Pattern-clip cache identity, as for S-101: the clip reads only the
        // visible area fills, so a display toggle that leaves them unchanged
        // reuses the clipped geometry instead of re-clipping every pattern.
        var patternClipKey = $"{await GetPatternClipScopeAsync(cancellationToken).ConfigureAwait(false)}|{ruleKey}|areas:{S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, prepared)}";

        var prewarm = await CataloguePreWarm.ForInstructionsAsync(s101Cat, prepared, cancellationToken).ConfigureAwait(false);

        var sourceFeatures = new S101VectorSource(_translatedDataset).GetFeatures().ToList();
        var geometryProvider = new FeatureGeometryProvider<Feature>(sourceFeatures);
        var coverageAreas = CoverageAreaResolver.Resolve(sourceFeatures);

        var info = $"{_translatedDataset.DatasetName} (S-57 → {_target.Spec}) — " +
                   $"{_translatedDataset.FeatureCount} features, {prepared.Count} instructions";

        // Out-of-scale-band declutter. S-57 has no DataCoverage /
        // minimumDisplayScale (S-101 FC §3.1.1). The whole-cell zoom-out
        // window is the larger of the compilation scale (CSCL, S-57 Appendix
        // B.1 §7.3.1.1) and the cell's largest feature SCAMIN — see
        // S57Dataset.ResolveCellMinimumDisplayScale. CSCL alone is the
        // *largest* intended viewing scale (S-52 §3.1.7; S-65 Annex B §2.1.6
        // maps it to the S-101 optimum display scale), so it stays the cell's
        // ranking scale against overlapping cells (CellCompilationScale)
        // rather than its zoom-out cutoff.
        //
        // S-57 feeds the window into the ungated whole-cell window
        // (CellMinimumDisplayScale) only — NOT the per-feature line-work cap
        // (OutOfBandMinDisplayScale / ApplyOutOfBandCap). The two mechanisms
        // would trigger at the same denominator, but they differ in
        // placeholder: the whole-cell window (viewer MapsuiDatasetRenderer.
        // ApplyCellScaleWindow) hides the cell AND draws its extent border when
        // zoomed out, whereas the per-feature cap silently blanks every feature
        // with no placeholder. The mariner's IgnoreScaleMinimum override is
        // therefore applied by the viewer against the ungated whole-cell value.
        var cellMinimumDisplayScale = Metadata.DisplayScale?.Minimum;
        var cellCompilationScale = S57Dataset.ResolveCompilationScale(_rawS57Document);

        // S-98 plane split (Annex A §A-6.9.1; Main §9.2.1 layers 2 / 5 / 6):
        // the cell is translated to S-101 / S-401 and portrayed with that
        // catalogue, so it splits exactly like a native S-101 cell — area
        // fills on BaseChartUnder, everything else on BaseChartOver — letting
        // S-102 (Bathymetry) and the S-104 water-level band (OnDemandSurface)
        // interleave between the fills and the line work instead of being
        // painted over by the cell's DepthArea fills.
        var areaInstructions = prepared.Where(i => i is AreaInstruction).ToList();
        var otherInstructions = prepared.Where(i => i is not AreaInstruction).ToList();

        return new VectorPortrayalResult
        {
            SubLayers = new[]
            {
                new VectorSubLayer
                {
                    LayerKey = AreasLayerKey,
                    LayerName = $"S-57 (areas): {_fileName}",
                    Instructions = areaInstructions,
                    Plane = S98DisplayPlane.BaseChartUnder,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "area",
                    PatternClipCacheKey = patternClipKey,
                    // The per-feature out-of-band cap is deliberately not
                    // applied for S-57 (see above); the whole-cell window
                    // handles zoom-out suppression with an extent border.
                    ApplyOutOfBandCap = false,
                },
                new VectorSubLayer
                {
                    LayerKey = LineworkLayerKey,
                    LayerName = $"S-57 (lines): {_fileName}",
                    Instructions = otherInstructions,
                    Plane = S98DisplayPlane.BaseChartOver,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "linework",
                    ApplyOutOfBandCap = false,
                },
            },
            LayerNames = new[] { AreasLayerKey, LineworkLayerKey },
            Palette = palette,
            GeometryProvider = geometryProvider,
            Product = "S-57",
            Spec = new SpecRef("S-57", default),
            SourceDatasetId = _fileName,
            ContentScope = _contentScope,
            Info = info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = name => prewarm.ResolveSymbolSvg(name),
            AreaFillProvider = name => prewarm.ResolveAreaFill(name),
            LineStyleProvider = name => prewarm.ResolveLineStyle(name),
            OutOfBandMinDisplayScale = null,
            CellMinimumDisplayScale = cellMinimumDisplayScale,
            CellCompilationScale = cellCompilationScale,
            CoverageAreas = coverageAreas,
        };
    }

    /// <summary>
    /// Returns the cell's drawing instructions under the catalogue's current
    /// ECDIS display state (already applied by the caller). The Lua rules run
    /// only when the mariner settings change; otherwise the cached rule output
    /// is re-filtered. Call with <see cref="_renderGate"/> held.
    /// </summary>
    private async Task<(IReadOnlyList<DrawingInstruction> Visible, IReadOnlyList<DrawingInstruction> Unfiltered, string RuleKey)> GetDisplayInstructionsAsync(
        MarinerSettings mariner, FeatureCatalogue fc, CancellationToken cancellationToken)
    {
        var s101Cat = _catalogue;
        var ruleKey = S101DatasetProcessor.BuildRuleOutputCacheKey(
            mariner,
            VectorPipeline.RuleOutputDependsOnDisplayMode(s101Cat) ? s101Cat.DisplayModes.ActiveDisplayModeId ?? string.Empty : null);

        if (_cachedRuleOutput is null || !string.Equals(_cachedRuleKey, ruleKey, StringComparison.Ordinal))
        {
            var executor = new S101LuaRuleExecutor(_luaEngine, _translatedDataset, s101Cat, fc);
            var featureSource = new S101FeatureXmlSource(_translatedDataset);
            _cachedRuleOutput = await new VectorPipeline(executor)
                .ExecuteRulesAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _cachedRuleKey = ruleKey;
            RuleRunCount++;
            Console.WriteLine($"[S57] Pipeline produced {_cachedRuleOutput.Count} drawing instructions");
        }

        return (VectorPipeline.ApplyDisplayFilters(_cachedRuleOutput, s101Cat), _cachedRuleOutput, ruleKey);
    }

    /// <summary>
    /// Computes (once, then memoizes) the content prefix of this cell's
    /// pattern-clip cache key. The clip result can be persisted by the host's
    /// disk cache, so the prefix covers everything that shapes the clipped
    /// geometry: the cell bytes and target product (<see cref="_contentScope"/>),
    /// the portrayal catalogue content, and the code that translates, portrays
    /// and clips it.
    /// </summary>
    private async ValueTask<string> GetPatternClipScopeAsync(CancellationToken cancellationToken)
    {
        if (_patternClipScope is not null)
            return _patternClipScope;

        var sb = new StringBuilder("s57clip:1|");
        sb.Append(_contentScope);
        sb.Append("|pc:").Append(
            await _catalogueManager.GetCatalogueHashAsync(PortrayalSpec, cancellationToken).ConfigureAwait(false) ?? "none");
        sb.Append("|asm:");
        S101DatasetProcessor.AppendModuleVersion(sb, typeof(PortrayalPipeline).Assembly);              // Core (pipeline)
        S101DatasetProcessor.AppendModuleVersion(sb, typeof(PortrayalCatalogue).Assembly);             // Portrayals
        S101DatasetProcessor.AppendModuleVersion(sb, typeof(S101LuaRuleExecutor).Assembly);            // Datasets.S101 (executor)
        S101DatasetProcessor.AppendModuleVersion(sb, typeof(S57ToS101Translator).Assembly);            // Datasets.S57 (translator)
        S101DatasetProcessor.AppendModuleVersion(sb, typeof(S57DatasetProcessor).Assembly);            // Datasets.Pipelines (this)
        S101DatasetProcessor.AppendModuleVersion(sb, typeof(EncDotNet.S100.Rendering.Scene.PatternPriorityClipper).Assembly); // clipper
        S101DatasetProcessor.AppendModuleVersion(sb, _luaEngine.GetType().Assembly);                   // Lua engine

        _patternClipScope = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
        return _patternClipScope;
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var featureId))
            return null;

        _featureIndex ??= BuildFeatureIndex();

        if (!_featureIndex.TryGetValue(featureId, out var feature))
            return null;

        EnsureDecoder();
        return BuildFeatureInfo(feature);
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        _featureIndex ??= BuildFeatureIndex();
        if (ordinal < 0 || ordinal >= _featureIndex.Count)
            return null;
        EnsureDecoder();
        var feature = System.Linq.Enumerable.ElementAt(_featureIndex.Values, ordinal);
        return BuildFeatureInfo(feature);
    }

    /// <inheritdoc/>
    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(
        double latitude, double longitude, double radiusMeters)
    {
        // Enumerate the same insertion-ordered feature index GetFeatureInfoAt
        // indexes, so each hit's Ordinal round-trips to the same feature.
        _featureIndex ??= BuildFeatureIndex();
        return FeatureHitTester.HitTest(_featureIndex.Values, latitude, longitude, radiusMeters);
    }

    /// <summary>
    /// The cell's scale rank against overlapping cells (see
    /// <see cref="CellScaleRanking"/>): its compilation
    /// scale (DSPM CSCL), else the translated <c>DataCoverage</c> minimum
    /// display scale, the same rank the display and the route check use.
    /// Smaller is finer; <see langword="null"/> when the cell declares none.
    /// </summary>
    public int? CellRankingScale => CellExtent.RankingScale;

    /// <summary>
    /// Whether the cell's translated <c>DataCoverage</c> (from <c>M_COVR</c>
    /// with <c>CATCOV</c> = 1) contains a WGS-84 point. A cell that declares
    /// no coverage is treated as covering every point.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns><see langword="true"/> when the cell covers the point.</returns>
    public bool CoversPoint(double latitude, double longitude)
    {
        var coverage = CellExtent.Coverage;
        if (coverage.Count == 0)
            return true;

        foreach (var area in coverage)
        {
            if (area.Contains(latitude, longitude))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Samples the nearest translated <c>Sounding</c> (from <c>SOUNDG</c>) to a
    /// WGS-84 location, or <c>null</c> when the cell has none. Delegates to
    /// <see cref="S101SoundingSampler.SampleNearest"/>.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns>The nearest sounding sample, or <c>null</c>.</returns>
    public S101SoundingSample? SampleNearestSounding(double latitude, double longitude)
        => S101SoundingSampler.SampleNearest(_translatedDataset.Document, latitude, longitude);

    private (int? RankingScale, IReadOnlyList<CoverageArea> Coverage) CellExtent
    {
        get
        {
            if (_cellExtent is { } extent)
                return extent;

            _featureIndex ??= BuildFeatureIndex();
            extent = (
                CellScaleRanking.RankingScale(
                    S57Dataset.ResolveCompilationScale(_rawS57Document),
                    S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(_featureIndex.Values)),
                CoverageAreaResolver.Resolve(_featureIndex.Values));
            _cellExtent = extent;
            return extent;
        }
    }

    private FeatureCatalogue RequireFeatureCatalogue()
        => _featureCatalogueManager.GetCatalogue(_target.Spec)
            ?? throw new InvalidOperationException(
                $"{_target.Spec} feature catalogue is required to render this S-57 dataset but none was provided.");

    private void EnsureDecoder()
    {
        if (!_decoderLoaded)
        {
            _decoder = _featureCatalogueManager.GetDecoder(_target.Spec);
            _decoderLoaded = true;
        }
    }

    private FeatureInfo BuildFeatureInfo(EncDotNet.S100.Pipelines.Vector.Feature feature)
    {
        var attributes = FeatureInfoBuilder.BuildFlat(
            feature.Attributes.Select(kv =>
                new KeyValuePair<string, string?>(kv.Key, kv.Value?.ToString())),
            _decoder);

        return new FeatureInfo
        {
            FeatureRef = feature.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
        };
    }

    /// <inheritdoc/>
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        _featureIndex ??= BuildFeatureIndex();
        EnsureDecoder();

        int i = 0;
        foreach (var feature in _featureIndex.Values)
        {
            yield return new FeatureSummary
            {
                FeatureRef = feature.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Ordinal = i++,
                FeatureType = feature.FeatureType,
                FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            };
        }
    }

    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature> BuildFeatureIndex()
    {
        var vectorSource = new S101VectorSource(_translatedDataset);
        var features = vectorSource.GetFeatures();
        var index = new Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>(features.Count);
        foreach (var f in features)
            index[f.Id] = f;
        return index;
    }

    /// <summary>
    /// Runs the S-57 validation pipeline against this dataset and
    /// returns the aggregated report. Composes two rule packs:
    /// <list type="number">
    /// <item><description>
    /// <see cref="S57PreTranslationRules.Default"/> against the raw
    /// <see cref="EncDotNet.S57.S57Document"/> — catches the few
    /// dataset-identity / coverage-metadata issues that do not
    /// survive translation.
    /// </description></item>
    /// <item><description>
    /// <see cref="S101DatasetRules.Default"/> against the translated
    /// <see cref="S101Document"/> via the
    /// <see cref="S101DatasetView"/> façade — every finding produced
    /// here is rebadged with the prefix <c>"S101-as-S57/"</c> so
    /// downstream consumers can distinguish native S-101 findings
    /// from those inherited via translation
    /// (<c>docs/design/non-gml-validation.md</c> §9.3, Q-s57-rebadge).
    /// </description></item>
    /// </list>
    /// The second pass runs only for a maritime cell translated into S-101.
    /// An inland cell is translated into S-401, which has no rule pack (the
    /// S-101 pack asserts S-101 normative clauses), so its report carries the
    /// pre-translation findings alone.
    /// The result is cached on the processor and returned verbatim
    /// on subsequent calls.
    /// </summary>
    public ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            var pre = S57PreTranslationRules.Default.Run(_rawS57Document);
            if (ReferenceEquals(_target, S57TranslationTarget.S101))
            {
                EnsureDecoder();
                var view = S101DatasetView.From(_translatedDataset.Document, _decoder);
                var post = S101DatasetRules.Default.Run(view);
                _validationReport = ConcatReports.Concat(pre, post, rebadgePrefix: "S101-as-S57/");
            }
            else
            {
                _validationReport = pre;
            }
            _validationCached = true;
        }
        return _validationReport;
    }

    /// <summary>
    /// Renders this S-57 cell to a standalone <see cref="SKBitmap"/> through
    /// the headless, backend-agnostic Skia vector core, bypassing Mapsui. The
    /// S-57 dataset is translated to S-101 in-memory (the same pipeline as
    /// <see cref="BuildVectorPortrayalAsync"/>) and the resulting drawing instructions are
    /// rasterised by <see cref="HeadlessVectorRenderer"/>.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, symbol/text scale).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    /// <remarks>
    /// Same caveats apply as for the S-101 headless path: pattern area-fills
    /// are not yet represented in the shared IR, so areas with an area-fill
    /// reference are omitted; the dominant solid depth-area colour fills,
    /// lines, soundings/symbols, and text are rendered.
    /// </remarks>
    public async Task<SKBitmap> RenderHeadlessAsync(
        int widthPixels,
        int heightPixels,
        RenderContext? context = null,
        RgbaColor? background = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        // Hold the render gate across portrayal prep AND symbol/line-style asset
        // resolution: the providers close over the mutable catalogue palette
        // state, so a concurrent render must not mutate it mid-build.
        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mariner = context?.Mariner ?? MarinerSettings.Default;
            var fc = RequireFeatureCatalogue();

            var s101Cat = _catalogue;
            await s101Cat.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);
            var palette = s101Cat.ActivePalette;

            // Honor the ECDIS display state (category + hidden VGs) before
            // portrayal, keyed on this cell's portrayal spec — see the core path.
            (context?.EcdisDisplay ?? UnfilteredEcdisDisplay).ApplyTo(s101Cat);

            var (prepared, _, _) = await GetDisplayInstructionsAsync(mariner, fc, cancellationToken).ConfigureAwait(false);

            var geometryProvider = new FeatureGeometryProvider<Feature>(new S101VectorSource(_translatedDataset).GetFeatures());

            var prewarm = await CataloguePreWarm.ForInstructionsAsync(s101Cat, prepared, cancellationToken).ConfigureAwait(false);

            return HeadlessVectorRenderer.Render(
                prepared,
                geometryProvider,
                palette,
                symbolProvider: name => prewarm.ResolveSymbolSvg(name),
                lineStyleProvider: name => prewarm.ResolveLineStyle(name),
                symbolScale: context?.SymbolScale ?? 1.0,
                textScale: context?.TextScale ?? 1.0,
                widthPixels: widthPixels,
                heightPixels: heightPixels,
                background: background ?? new RgbaColor(255, 255, 255, 255),
                areaFillProvider: name => prewarm.ResolveAreaFill(name),
                hiddenCategories: context?.HiddenInstructionCategories
                    ?? DrawingInstructionCategory.None,
                basemap: context?.Basemap ?? BasemapKind.None,
                viewport: context?.Viewport);
        }
        finally
        {
            _renderGate.Release();
        }
    }
}
