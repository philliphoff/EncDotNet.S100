using System;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Datasets.S57.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Renders an S-57 ENC base cell by translating it in-memory to an
/// <see cref="S101Document"/> and reusing the S-101 portrayal pipeline.
/// Symbology is S-101 (not S-52); coverage is breadth-first.
/// </summary>
public sealed class S57DatasetProcessor : IDatasetProcessor, IVectorPortrayalSource, IHeadlessImageRenderer
{
    // Serialises render calls so the catalogue's mutable palette / ECDIS
    // state is not mutated mid-build by a concurrent render.
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly EncDotNet.S57.S57Document _rawS57Document;
    private readonly S101Dataset _translatedDataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S101PortrayalCatalogue _catalogue;
    private readonly ILuaEngine _luaEngine;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;
    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>? _featureIndex;
    private EncDotNet.S100.Features.FeatureCatalogueDecoder? _decoder;
    private bool _decoderLoaded;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    public SpecRef Spec => new("S-57", default);

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
        S57Dataset s57,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
    {
        ArgumentNullException.ThrowIfNull(s57);
        _fileName = fileName;
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-101");
        _catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);
        _featureCatalogueManager = featureCatalogueManager;

        // Retain the raw S-57 document so the pre-translation
        // validation pack (S57PreTranslationRules) can run against
        // fields that do not survive translation — see
        // docs/design/non-gml-validation.md §9.3.
        _rawS57Document = s57.Document;
        var translator = new S57ToS101Translator();
        var s101Doc = translator.Translate(s57);
        _translatedDataset = S101Dataset.FromDocument(s101Doc);

        // S-57 datasets render through the S-101 portrayal catalogue.
        Diagnostics.CatalogueResolutionDiagnostics.Report(this, new SpecRef("S-101", default), _catalogue.CatalogueRef, "portrayal");
    }

    private static S57Dataset OpenFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S57Dataset.Open(stream);
    }

    private static S57Dataset OpenFromSource(IAssetSource source, string relativePath)
    {
        using var stream = AssetSourceHelpers.OpenSeekable(source, relativePath);
        return S57Dataset.Open(stream);
    }

    private static S57Dataset OpenFromSource(
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

            return S57Dataset.Open(baseStream, updateStreams);
        }
        finally
        {
            baseStream.Dispose();
            foreach (var stream in updateStreams)
                stream.Dispose();
        }
    }

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
        var mariner = MarinerSettings.Default;

        var fc = _featureCatalogueManager.GetCatalogue("S-101")
            ?? throw new InvalidOperationException(
                "S-101 feature catalogue is required to render S-57 datasets but none was provided.");

        Console.WriteLine("[S57] Translated to S-101 in-memory; running Part 9 portrayal pipeline...");

        var s101Cat = _catalogue;
        var paletteType = context?.Palette ?? PaletteType.Day;
        await s101Cat.SwitchPaletteAsync(paletteType, cancellationToken).ConfigureAwait(false);
        var palette = s101Cat.ActivePalette;

        var executor = new S101LuaRuleExecutor(_luaEngine, _translatedDataset, s101Cat, fc);
        var featureSource = new S101FeatureXmlSource(_translatedDataset);
        var pipeline = new PortrayalPipeline(executor);
        var portrayalLayer = await pipeline.ProcessAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var prepared = ((IVectorLayer)portrayalLayer).Instructions;
        Console.WriteLine($"[S57] Pipeline produced {prepared.Count} drawing instructions");

        var prewarm = await CataloguePreWarm.ForInstructionsAsync(s101Cat, prepared, cancellationToken).ConfigureAwait(false);

        var geometryProvider = new FeatureGeometryProvider<Feature>(new S101VectorSource(_translatedDataset).GetFeatures());

        var info = $"{_translatedDataset.DatasetName} (S-57 → S-101) — " +
                   $"{_translatedDataset.FeatureCount} features, {prepared.Count} instructions";

        return new VectorPortrayalResult
        {
            // S-57 is the legacy ENC fallback; treat the whole layer as
            // base-chart line-work + symbology on BaseChartOver (S-98 §9.2.1
            // layer 2). We do not split S-57 into areas vs lines — the legacy
            // renderer mixes them.
            SubLayers = new[]
            {
                new VectorSubLayer
                {
                    LayerKey = "s57.main",
                    LayerName = $"S-57: {_fileName}",
                    Instructions = prepared,
                    Plane = S98DisplayPlane.BaseChartOver,
                    WithinPlanePriority = 0,
                },
            },
            Palette = palette,
            GeometryProvider = geometryProvider,
            Product = "S-57",
            Spec = new SpecRef("S-57", default),
            SourceDatasetId = _fileName,
            Info = info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = name => prewarm.ResolveSymbolSvg(name),
            AreaFillProvider = name => prewarm.ResolveAreaFill(name),
            LineStyleProvider = name => prewarm.ResolveLineStyle(name),
        };
    }

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

    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        _featureIndex ??= BuildFeatureIndex();
        if (ordinal < 0 || ordinal >= _featureIndex.Count)
            return null;
        EnsureDecoder();
        var feature = System.Linq.Enumerable.ElementAt(_featureIndex.Values, ordinal);
        return BuildFeatureInfo(feature);
    }

    private void EnsureDecoder()
    {
        if (!_decoderLoaded)
        {
            _decoder = _featureCatalogueManager.GetDecoder("S-101");
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
    /// The result is cached on the processor and returned verbatim
    /// on subsequent calls.
    /// </summary>
    public ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            EnsureDecoder();
            var pre = S57PreTranslationRules.Default.Run(_rawS57Document);
            var view = S101DatasetView.From(_translatedDataset.Document, _decoder);
            var post = S101DatasetRules.Default.Run(view);
            _validationReport = ConcatReports.Concat(pre, post, rebadgePrefix: "S101-as-S57/");
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
            var mariner = MarinerSettings.Default;
            var fc = _featureCatalogueManager.GetCatalogue("S-101")
                ?? throw new InvalidOperationException(
                    "S-101 feature catalogue is required to render S-57 datasets but none was provided.");

            var s101Cat = _catalogue;
            await s101Cat.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);
            var palette = s101Cat.ActivePalette;

            var executor = new S101LuaRuleExecutor(_luaEngine, _translatedDataset, s101Cat, fc);
            var featureSource = new S101FeatureXmlSource(_translatedDataset);
            var pipeline = new PortrayalPipeline(executor);
            var portrayalLayer = await pipeline
                .ProcessAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var prepared = ((IVectorLayer)portrayalLayer).Instructions;

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
                    ?? DrawingInstructionCategory.None);
        }
        finally
        {
            _renderGate.Release();
        }
    }
}
