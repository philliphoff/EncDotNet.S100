using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Validation;
using Mapsui;
using Mapsui.Layers;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S101DatasetProcessor : IDatasetProcessor, IHeadlessImageRenderer
{
    private readonly S101Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S101PortrayalCatalogue _catalogue;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly ILuaEngine _luaEngine;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;
    private readonly MapsuiRenderAssetCache _renderAssetCache = new();

    // Cache of the pattern-fill priority clip geometry. The clip result is
    // palette-independent, so caching lets a Day/Dusk/Night palette switch
    // reuse the previously computed clip instead of re-paying the multi-second
    // NetTopologySuite overlay.
    //
    // Defaults to a per-processor single-slot in-memory cache (step 1), which
    // only helps re-renders of this already-open dataset. When the host injects
    // a shared DiskPatternClipCache (step 2) it is used instead, persisting the
    // clip to disk so the cold first open of a previously-seen cell is fast even
    // after a restart. The disk cache is process-global, so the clip key is
    // fully qualified with _patternClipScope (see BuildDatasetScopeKey) to avoid
    // cross-cell collisions. Guarded by _renderGate.
    private readonly IPatternClipCache _patternClipCache;

    // Deterministic per-dataset scope prepended to the portrayal cache key when
    // forming the disk clip-cache key. Encodes dataset content + clip params +
    // CRS + FormatVersion so the disk key is globally unique and self-
    // invalidating. Constant for the lifetime of this processor.
    private readonly string _patternClipScope;
    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>? _featureIndex;
    private FeatureCatalogueDecoder? _decoder;
    private bool _decoderLoaded;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    // Serializes RenderAsync. The processor holds a single long-lived
    // S-101 portrayal catalogue whose palette / display-mode / viewing-
    // group / display-plane state is mutated at the top of every render
    // and then read throughout the pipeline and renderer. The viewer
    // fires re-renders re-entrantly (fire-and-forget) on settings
    // changes, so renders must not overlap on that shared state or on
    // the portrayal-instruction cache below.
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    // Recommendation #1: single-entry cache of the Lua drawing-instruction
    // list, keyed by the inputs that actually change it (see
    // BuildPortrayalCacheKey). Guarded by _renderGate.
    private string? _cachedPortrayalKey;
    private IReadOnlyList<DrawingInstruction>? _cachedPortrayalInstructions;

    // Cross-load cache of the post-pipeline drawing-instruction list. The
    // single-slot _cachedPortrayalInstructions above only helps re-renders of
    // this already-open processor; this shared cache (an InMemory LRU by
    // default, or a process-global DiskPortrayalInstructionCache when the host
    // injects one) lets a *fresh* processor for a previously-portrayed cell skip
    // the multi-second MoonSharp Part 9A Lua run entirely. The key is fully
    // qualified with the portrayal-content hash (see GetPortrayalContentHashAsync) so
    // a change to the dataset bytes, the feature / portrayal catalogue content
    // (including CLI / settings overrides and bundled Lua rules), or the
    // pipeline / VM assemblies yields a miss and a recompute. Guarded by
    // _renderGate.
    private readonly IPortrayalInstructionCache _instructionCache;

    // Memoized portrayal-content hash forming the cross-load instruction-cache
    // key prefix (and strengthening the pattern-clip key). Computed once on
    // first render and reused; constant for the lifetime of this processor.
    private string? _portrayalContentHash;

    // Bump when the portrayal-content hash composition changes (e.g. a new
    // input is folded in) so previously persisted instruction-cache entries are
    // treated as stale rather than reused under a now-incompatible key.
    private const int PortrayalContentFormatVersion = 2;

    /// <summary>
    /// Number of renders served from the portrayal-instruction cache
    /// (the ~2.4 s Lua pipeline was skipped). Exposed for tests.
    /// </summary>
    internal int PortrayalCacheHits { get; private set; }

    /// <summary>
    /// Number of renders that ran the full portrayal pipeline (cache
    /// miss). Exposed for tests.
    /// </summary>
    internal int PortrayalCacheMisses { get; private set; }

    /// <summary>
    /// Number of area renders whose pattern-fill priority clip was served from
    /// the pattern-clip cache (the multi-second NetTopologySuite overlay was
    /// skipped — e.g. on a palette switch, or a warm disk-cache cold open).
    /// Exposed for tests.
    /// </summary>
    internal long PatternClipCacheHits => _patternClipCache.Hits;

    /// <summary>
    /// Number of renders whose post-pipeline instruction list was served from
    /// the shared cross-load instruction cache (the MoonSharp Part 9A Lua run
    /// was skipped — e.g. a fresh processor re-opening a previously-portrayed
    /// cell). Exposed for tests.
    /// </summary>
    internal long SharedInstructionCacheHits => _instructionCache.Hits;

    // Canonical "no filter" ECDIS state used when a render supplies no
    // EcdisDisplay. Category.All maps to a null display mode (every
    // viewing group visible) with no hidden VGs or planes, matching the
    // fresh-catalogue default — so normalizing null to this changes no
    // behaviour but keeps catalogue state a deterministic function of
    // the cache key.
    private static readonly EcdisDisplaySettings UnfilteredEcdisDisplay =
        new() { Category = EcdisDisplayCategory.All };

    public SpecRef Spec => new("S-101", default);

    public S101DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPatternClipCache? sharedPatternClipCache = null,
        IPortrayalInstructionCache? sharedInstructionCache = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, featureCatalogueManager, sharedPatternClipCache, sharedInstructionCache)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S101DatasetProcessor"/> by reading
    /// the ISO 8211 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S101DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPatternClipCache? sharedPatternClipCache = null,
        IPortrayalInstructionCache? sharedInstructionCache = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            luaEngine,
            featureCatalogueManager,
            sharedPatternClipCache,
            sharedInstructionCache)
    {
    }

    private S101DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPatternClipCache? sharedPatternClipCache,
        IPortrayalInstructionCache? sharedInstructionCache)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-101");
        _catalogueManager = catalogueManager;
        _catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);

        // Buffer the raw dataset bytes once so we can (a) parse the document and
        // (b) compute a content hash for the disk clip-cache scope key. S-101
        // cells are small enough (a few MB) that buffering is cheap, and the
        // content hash makes the persisted clip auto-invalidate when the cell's
        // bytes change.
        byte[] datasetBytes;
        using (datasetStream)
        {
            datasetBytes = ReadAllBytes(datasetStream);
        }
        _dataset = S101Dataset.Open(new MemoryStream(datasetBytes, writable: false));

        // When a shared disk cache is injected use it (persistent, cross-cell,
        // process-global); otherwise fall back to a per-processor in-memory slot.
        _patternClipCache = sharedPatternClipCache ?? new InMemoryPatternClipCache();
        _patternClipScope = BuildDatasetScopeKey(datasetBytes, _dataset);

        // When a shared instruction cache is injected use it (persistent,
        // cross-cell, process-global); otherwise fall back to a bounded
        // per-processor in-memory LRU so the cross-load behaviour is exercised
        // even without a host-supplied disk cache.
        _instructionCache = sharedInstructionCache ?? new InMemoryPortrayalInstructionCache();

        _featureCatalogueManager = featureCatalogueManager;

        Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
    }

    /// <summary>
    /// Reads <paramref name="stream"/> to its end into a byte array. Used to
    /// snapshot the dataset bytes for parsing and content hashing.
    /// </summary>
    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream existing)
            return existing.ToArray();

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Builds the deterministic per-dataset scope that, prefixed to the
    /// portrayal cache key, fully qualifies the disk pattern-clip cache key so
    /// it is globally unique and self-invalidating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern-fill priority clip geometry is a pure function of the dataset
    /// geometry and the clip parameters (it is palette-independent — the palette
    /// only recolours tiles applied after clipping). The scope therefore
    /// encodes:
    /// </para>
    /// <list type="bullet">
    /// <item>the SHA-256 of the raw dataset bytes (content identity);</item>
    /// <item>the S-101 product-specification edition and dataset name from the
    /// DSID record (belt-and-suspenders alongside the content hash);</item>
    /// <item>the clip parameters that affect the output geometry —
    /// <see cref="MapsuiDisplayListRenderer.PatternClipSimplifyToleranceMetres"/>
    /// and <see cref="MapsuiDisplayListRenderer.MinPointsToSimplifyForClip"/>;</item>
    /// <item>the rendering CRS (EPSG:3857, the renderer's fixed projection);</item>
    /// <item>the <see cref="DiskPatternClipCache.FormatVersion"/> stamp.</item>
    /// </list>
    /// <para>
    /// Any change to dataset content, clip parameters, the CRS, or the cache /
    /// serialization format yields a different scope and therefore a cache miss
    /// (recompute), so persisted geometry can never be reused incorrectly.
    /// </para>
    /// </remarks>
    internal static string BuildDatasetScopeKey(byte[] datasetBytes, S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(datasetBytes);
        ArgumentNullException.ThrowIfNull(dataset);

        var c = System.Globalization.CultureInfo.InvariantCulture;
        var contentHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(datasetBytes)).ToLowerInvariant();

        var id = dataset.Document.Identification;
        var sb = new StringBuilder();
        sb.Append("ds:").Append(contentHash);
        sb.Append("|name:").Append(id.DatasetName);
        sb.Append("|ed:").Append(id.ProductSpecificationEdition);
        sb.Append("|tol:").Append(MapsuiDisplayListRenderer.PatternClipSimplifyToleranceMetres.ToString("R", c));
        sb.Append("|gate:").Append(MapsuiDisplayListRenderer.MinPointsToSimplifyForClip.ToString(c));
        sb.Append("|crs:EPSG:3857");
        sb.Append("|fmt:").Append(DiskPatternClipCache.FormatVersion.ToString(c));
        return sb.ToString();
    }

    /// <summary>
    /// Computes (once, then memoizes) the strong content hash that prefixes the
    /// cross-load instruction-cache key and strengthens the pattern-clip key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hash must change whenever <em>anything</em> that can alter the
    /// post-pipeline drawing-instruction list changes, so a persisted entry is
    /// never reused incorrectly. It hashes <em>actual resolved content</em>
    /// rather than declared version strings (which an override can change without
    /// bumping). It folds in:
    /// </para>
    /// <list type="bullet">
    /// <item>the per-dataset scope (<see cref="_patternClipScope"/>): SHA-256 of
    /// the dataset bytes plus name / edition / CRS;</item>
    /// <item>the Feature Catalogue content hash
    /// (<see cref="ICatalogueProvider{T}.GetCatalogueHashAsync"/>) — the raw FC
    /// XML bytes, capturing CLI / settings FC overrides;</item>
    /// <item>the Portrayal Catalogue content hash — the PC XML plus every
    /// referenced rule and asset file. This subsumes the catalogue's structural
    /// metadata (rule files, viewing groups, display modes / planes,
    /// context-parameter defaults — all present in the PC XML) and the Lua rule
    /// sources (the rule files themselves), so no catalogue structure is
    /// re-derived here;</item>
    /// <item>the module version ids of every assembly whose code turns those
    /// bytes into the instruction list (pipeline, Portrayals, Features, the
    /// S-101 executor, this processor, and the Lua engine) — captures
    /// behavioural changes even when the catalogue files are byte-identical;</item>
    /// <item>the serializer and content format-version stamps.</item>
    /// </list>
    /// <para>
    /// This assumes the S-101 portrayal rules are Lua-only (true for the bundled
    /// catalogue): the instruction list is then independent of palette and
    /// symbol / text scale, which the renderer applies afterwards. An XSLT rule
    /// would make the list palette-dependent; the PC content hash would change
    /// for such a catalogue, but if an XSLT S-101 catalogue is ever introduced
    /// the palette must also be added to the per-render key (bump
    /// <see cref="PortrayalContentFormatVersion"/>).
    /// </para>
    /// <para>
    /// The catalogue hashes are memoized once-per-spec by their managers, so a
    /// second processor for the same spec recomputes this hash cheaply. The
    /// result is memoized for this processor's lifetime.
    /// </para>
    /// </remarks>
    private async ValueTask<string> GetPortrayalContentHashAsync(CancellationToken cancellationToken)
    {
        if (_portrayalContentHash is not null)
            return _portrayalContentHash;

        var c = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("pcf:").Append(PortrayalContentFormatVersion.ToString(c));
        sb.Append("|dlfmt:").Append(DrawingInstructionSerializer.FormatVersion.ToString(c));
        sb.Append("|scope:").Append(_patternClipScope);
        sb.Append("|fc:").Append(
            await _featureCatalogueManager.GetCatalogueHashAsync(Spec, cancellationToken).ConfigureAwait(false) ?? "none");
        sb.Append("|pc:").Append(
            await _catalogueManager.GetCatalogueHashAsync(Spec, cancellationToken).ConfigureAwait(false) ?? "none");

        sb.Append("|asm:");
        AppendModuleVersion(sb, typeof(PortrayalPipeline).Assembly);          // Core (pipeline)
        AppendModuleVersion(sb, typeof(PortrayalCatalogue).Assembly);         // Portrayals (PC parse / asset load)
        AppendModuleVersion(sb, typeof(FeatureCatalogue).Assembly);           // Features (FC decode)
        AppendModuleVersion(sb, typeof(S101LuaRuleExecutor).Assembly);        // Datasets.S101 (executor)
        AppendModuleVersion(sb, typeof(S101DatasetProcessor).Assembly);       // Datasets.Pipelines (this)
        AppendModuleVersion(sb, _luaEngine.GetType().Assembly);               // Lua engine (MoonSharp)

        _portrayalContentHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
        return _portrayalContentHash;
    }

    /// <summary>
    /// Appends a deterministic per-assembly content stamp. Uses the module
    /// version id (a build-input hash under deterministic builds, so it changes
    /// when the assembly's compiled content changes), falling back to the
    /// assembly's full name when the MVID is unavailable.
    /// </summary>
    private static void AppendModuleVersion(StringBuilder sb, System.Reflection.Assembly assembly)
    {
        try
        {
            sb.Append(assembly.ManifestModule.ModuleVersionId.ToString("N")).Append(',');
        }
        catch
        {
            sb.Append(assembly.FullName ?? "unknown").Append(',');
        }
    }

    public async Task<DatasetResult> RenderAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Serialize against re-entrant renders that share the long-lived
        // catalogue state and the portrayal-instruction cache.
        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RenderCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>
    /// Renders this S-101 cell to a standalone <see cref="SKBitmap"/> through the
    /// headless, backend-agnostic Skia vector core
    /// (<see cref="VectorSceneBuilder"/> → <see cref="SkiaDisplayListRenderer"/>),
    /// bypassing Mapsui entirely. This is the vector analogue of the direct-Skia
    /// coverage renderer and the basis for a headless tile-serving API.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, symbol/text scale, ECDIS display, mariner settings).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    /// <remarks>
    /// Pattern area-fills are not yet represented in the shared IR, so areas with
    /// an area-fill reference (e.g. shallow-water diamonds, quality-of-bathymetry
    /// overlays) are omitted here; the dominant solid depth-area colour fills,
    /// lines, soundings/symbols, and text are rendered. Use <see cref="RenderAsync"/>
    /// (the Mapsui path) for full pattern-fill fidelity. Unlike that path, this
    /// produces a single bitmap (no S-102 interleave split) and draw order follows
    /// the shared core's S-100 Part 9 ordering.
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
        // resolution: the providers close over the mutable catalogue palette /
        // ECDIS state, so a concurrent render must not mutate it mid-build.
        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mariner = context?.Mariner ?? MarinerSettings.Default;
            var fc = _featureCatalogueManager.GetCatalogue("S-101")
                ?? throw new InvalidOperationException(
                    "S-101 feature catalogue is required to render the dataset but none was provided.");

            var s101Cat = _catalogue;
            s101Cat.SwitchPalette(context?.Palette ?? PaletteType.Day);
            (context?.EcdisDisplay ?? UnfilteredEcdisDisplay).ApplyTo(s101Cat);
            var palette = s101Cat.ActivePalette;

            var executor = new S101LuaRuleExecutor(_luaEngine, _dataset, s101Cat, fc);
            var featureSource = new S101FeatureXmlSource(_dataset);
            var pipeline = new PortrayalPipeline(executor);
            var portrayalLayer = await pipeline
                .ProcessAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var prepared = ((IVectorLayer)portrayalLayer).Instructions;

            var geometryProvider = new S101FeatureGeometryProvider(_dataset);

            return HeadlessVectorRenderer.Render(
                prepared,
                geometryProvider,
                palette,
                symbolProvider: name =>
                {
                    try { return s101Cat.GetSymbol(name).SvgContent; }
                    catch { return null; }
                },
                lineStyleProvider: name =>
                {
                    try { return s101Cat.GetLineStyle(name); }
                    catch { return null; }
                },
                symbolScale: context?.SymbolScale ?? 1.0,
                textScale: context?.TextScale ?? 1.0,
                widthPixels: widthPixels,
                heightPixels: heightPixels,
                background: background ?? new RgbaColor(255, 255, 255, 255));
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private async Task<DatasetResult> RenderCoreAsync(RenderContext? context, CancellationToken cancellationToken)
    {
        var mariner = context?.Mariner ?? MarinerSettings.Default;

        var fc = _featureCatalogueManager.GetCatalogue("S-101")
            ?? throw new InvalidOperationException(
                "S-101 feature catalogue is required to render the dataset but none was provided.");

        Console.WriteLine("[S101] Starting Part 9 vector portrayal pipeline...");

        // Build the S-101 portrayal catalogue and switch palette before the
        // pipeline runs so XSLT rules (if any) see the active colour profile.
        var s101Cat = _catalogue;
        var paletteType = context?.Palette ?? PaletteType.Day;
        s101Cat.SwitchPalette(paletteType);

        // Normalize null ECDIS display to the canonical unfiltered state
        // and always apply it, so the catalogue's display-mode / viewing-
        // group / display-plane state is a deterministic function of the
        // settings value — a precondition for keying the cache on it.
        var ecdisSettings = context?.EcdisDisplay ?? UnfilteredEcdisDisplay;
        ecdisSettings.ApplyTo(s101Cat);
        var palette = s101Cat.ActivePalette;
        Console.WriteLine($"[S101] Loaded {paletteType} palette with {palette.Colors.Count} colors");

        // Recommendation #1: the Lua drawing-instruction list depends only
        // on (mariner, ECDIS display state) — NOT on palette or symbol /
        // text scale, which are applied later in the renderer. Reuse the
        // cached list when neither changed (e.g. a Day/Dusk/Night palette
        // switch, the dominant re-render trigger), skipping the Lua
        // pipeline. Guarded by _renderGate (held for this whole render).
        var cacheKey = BuildPortrayalCacheKey(mariner, ecdisSettings);
        IReadOnlyList<DrawingInstruction> prepared;
        if (_cachedPortrayalInstructions is not null
            && string.Equals(_cachedPortrayalKey, cacheKey, StringComparison.Ordinal))
        {
            prepared = _cachedPortrayalInstructions;
            PortrayalCacheHits++;
            Console.WriteLine($"[S101] Reusing {prepared.Count} cached drawing instructions (portrayal cache hit)");
        }
        else
        {
            // Cross-load cache: a fresh processor for a previously-portrayed
            // cell can reuse the persisted post-pipeline instruction list and
            // skip the multi-second MoonSharp Part 9A Lua run. The key folds the
            // portrayal-content hash (dataset + FC/PC content + assemblies) into
            // the per-render cacheKey so it self-invalidates on any change.
            var instructionKey = $"{await GetPortrayalContentHashAsync(cancellationToken).ConfigureAwait(false)}|{cacheKey}";
            prepared = _instructionCache.GetOrCompute(instructionKey, () =>
            {
                // Drive the unified VectorPipeline with the S-101 Lua rule
                // executor (Part 9A). XSLT rules in the S-101 catalogue (if
                // any) are also honoured by the pipeline. Run synchronously
                // inside the (synchronous) cache factory: the render gate
                // already serializes this work and no UI sync context is
                // captured on this path (the pipeline uses ConfigureAwait(false)
                // throughout).
                var executor = new S101LuaRuleExecutor(_luaEngine, _dataset, s101Cat, fc);
                var featureSource = new S101FeatureXmlSource(_dataset);
                var pipeline = new PortrayalPipeline(executor);
                var portrayalLayer = pipeline
                    .ProcessAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: cancellationToken)
                    .GetAwaiter().GetResult();
                return ((IVectorLayer)portrayalLayer).Instructions;
            });
            _cachedPortrayalKey = cacheKey;
            _cachedPortrayalInstructions = prepared;
            PortrayalCacheMisses++;
            Console.WriteLine($"[S101] Pipeline produced {prepared.Count} drawing instructions");
        }

        // S-98 R-101-102-A (Annex A §A-6.9.1): S-102 must render between
        // S-101 area fills and S-101 line work / points / text. We split
        // the S-101 display list along the AreaInstruction boundary into
        // two Mapsui layers so the LayerStackBuilder can interleave S-102.
        // PR-L0 TBD-3 resolved: split in the processor (double pipeline
        // pass / type pre-filter) rather than the renderer. The double
        // render is small per cell (< 5% per design note §4.2.1); a
        // future v2 mitigation could be a single-pass dual-sink renderer
        // if profiling on large datasets shows it matters.
        var areaInstructions = prepared.Where(i => i is AreaInstruction).ToList();
        var otherInstructions = prepared.Where(i => i is not AreaInstruction).ToList();

        var geometryProvider = new S101FeatureGeometryProvider(_dataset);

        // Fully qualify the clip-cache key with the per-dataset scope so a
        // process-global disk cache cannot collide across cells. The portrayal-
        // content hash is folded in so a rule / catalogue change (which changes
        // the instructions and therefore the clip geometry) invalidates the
        // persisted clip. The in-memory fallback is unaffected (its scope is
        // constant within this processor).
        var patternClipKey = $"{_patternClipScope}|{await GetPortrayalContentHashAsync(cancellationToken).ConfigureAwait(false)}|{cacheKey}";

        var areaLayer = CreateRenderer(s101Cat, palette, context, suffix: "areas", patternClipCacheKey: patternClipKey)
            .Render(areaInstructions, geometryProvider);
        var lineLayer = CreateRenderer(s101Cat, palette, context, suffix: "lines", patternClipCacheKey: null)
            .Render(otherInstructions, geometryProvider);

        // PR-L2 R-101-102-B: tag every Mapsui IFeature with its S-101
        // feature-type code and (for DepthContour) its VALDCO depth
        // value, so the S-98 rule engine can filter without re-running
        // portrayal. See S98DefaultRules.SuppressS101DepthFeatures.
        TagMapsuiFeaturesWithFeatureType(areaLayer);
        TagMapsuiFeaturesWithFeatureType(lineLayer);

        // Out-of-scale-band declutter: when the viewport is zoomed out past
        // the cell's intended display scale band (S-101 DataCoverage /
        // minimumDisplayScale, FC §3.1.1), the point/line/text display
        // collapses into an unreadable mass of overlapping symbology. Cap
        // those detail features' maximum visible resolution so they drop out
        // when zoomed too far out, while leaving the area fills uncapped so
        // the cell's land / depth-area silhouette stays visible as a
        // coverage footprint. Honour the mariner's IgnoreScaleMinimum
        // override (S-101 PC context parameter) so "show everything
        // regardless of scale" disables the cap, consistent with SCAMIN.
        if (!mariner.IgnoreScaleMinimum)
        {
            _featureIndex ??= BuildFeatureIndex();
            var bandMaxResolution = ResolveOutOfBandMaxResolution(_featureIndex.Values);
            if (bandMaxResolution is double cap && lineLayer is MemoryLayer lineMemoryLayer)
                ApplyOutOfScaleBandCap(lineMemoryLayer.Features, cap);
        }

        // Union the two layer extents (each is in EPSG:3857). Mapsui
        // returns a zero-extent rect when a layer has no features, so
        // skip such layers in the union.
        var areaExtent = areaLayer.Extent;
        var lineExtent = lineLayer.Extent;
        var layerExtent = areaExtent is null
            ? (lineExtent ?? new MRect(0, 0, 0, 0))
            : (lineExtent is null ? areaExtent : areaExtent.Join(lineExtent));

        Console.WriteLine($"[S101-Lua] Rendered {areaInstructions.Count} area + {otherInstructions.Count} non-area instructions");

        var info = $"{_dataset.DatasetName} — {_dataset.FeatureCount} features, " +
                   $"{prepared.Count} instructions";

        var layers = new ILayer[] { areaLayer, lineLayer };

        return new DatasetResult
        {
            Layers = layers,
            Extent = layerExtent,
            Info = info,
            Spec = new SpecRef("S-101", default),
            // Sub-layer keys so the viewer's per-sub-layer disclosure
            // can toggle areas vs line work independently.
            LayerNames = new[] { "s101.areas", "s101.linework" },
            StackEntries = new[]
            {
                // Area fills land on the deepest base-chart plane so
                // S-102 (Bathymetry, 10) can sit on top of them.
                new LayerStackEntry(
                    Layer: areaLayer,
                    Plane: S98DisplayPlane.BaseChartUnder,
                    WithinPlanePriority: 0,
                    SourceDatasetId: _fileName,
                    SourceFeatureType: "area"),
                // Line work, points, symbols, and text remain on the
                // base-chart "over" plane (above Bathymetry).
                new LayerStackEntry(
                    Layer: lineLayer,
                    Plane: S98DisplayPlane.BaseChartOver,
                    WithinPlanePriority: 0,
                    SourceDatasetId: _fileName,
                    SourceFeatureType: "linework"),
            },
        };
    }

    /// <summary>
    /// Builds the cache key for the portrayal-instruction list. The key
    /// captures exactly the inputs that change the emitted instructions:
    /// every <see cref="MarinerSettings"/> field (S-101 PC context
    /// parameters fed to the Part 9A Lua rules, incl. NationalLanguage)
    /// plus the effective ECDIS display state applied to the catalogue
    /// (display category and the S-101 hidden viewing groups / hidden
    /// display planes that drive VectorPipeline stage-6 filtering).
    /// </summary>
    /// <remarks>
    /// The key deliberately excludes palette and symbol/text scale: the
    /// Lua rules emit colour tokens and nominal sizes that the renderer
    /// resolves afterwards, so those never alter the instruction list.
    /// Two ECDIS categories that resolve to the same display mode merely
    /// over-invalidate (an extra cache miss), never under-invalidate.
    /// </remarks>
    internal static string BuildPortrayalCacheKey(MarinerSettings mariner, EcdisDisplaySettings ecdis)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        ArgumentNullException.ThrowIfNull(ecdis);

        var c = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("m:");
        sb.Append(mariner.SafetyContour.ToString(c)).Append('|');
        sb.Append(mariner.SafetyDepth.ToString(c)).Append('|');
        sb.Append(mariner.ShallowContour.ToString(c)).Append('|');
        sb.Append(mariner.DeepContour.ToString(c)).Append('|');
        sb.Append((int)mariner.DepthUnit).Append('|');
        sb.Append(mariner.FourShades ? '1' : '0');
        sb.Append(mariner.ShallowWaterDangers ? '1' : '0');
        sb.Append(mariner.PlainBoundaries ? '1' : '0');
        sb.Append(mariner.SimplifiedSymbols ? '1' : '0');
        sb.Append(mariner.FullLightLines ? '1' : '0');
        sb.Append(mariner.RadarOverlay ? '1' : '0');
        sb.Append(mariner.IgnoreScaleMinimum ? '1' : '0');
        sb.Append('|').Append(mariner.NationalLanguage);

        sb.Append(";e:").Append((int)ecdis.Category);

        // Hidden S-101 viewing groups (sorted for order-independence).
        // "S-101" is this processor's catalogue Spec.Name, matching the
        // key EcdisDisplayExtensions.ApplyTo reads.
        sb.Append(";vg:");
        if (ecdis.HiddenViewingGroups.TryGetValue("S-101", out var hiddenVg))
        {
            foreach (var id in hiddenVg.OrderBy(static x => x))
                sb.Append(id).Append(',');
        }

        sb.Append(";dp:");
        foreach (var plane in ecdis.HiddenDisplayPlanes.Select(static p => (int)p).OrderBy(static x => x))
            sb.Append(plane).Append(',');

        return sb.ToString();
    }

    /// <summary>
    /// Resolves the cell's out-of-scale-band cutoff as a Mapsui maximum
    /// visible resolution (m/px in EPSG:3857) from the
    /// <c>DataCoverage</c> feature's <c>minimumDisplayScale</c> attribute
    /// (S-101 FC §3.1.1 — the smallest scale, i.e. largest denominator, at
    /// which the cell is intended to be displayed). Detail features become
    /// invisible once the viewport resolution exceeds this value (i.e. the
    /// chart is zoomed out beyond its compilation scale band).
    /// </summary>
    /// <param name="features">The dataset's vector features.</param>
    /// <returns>
    /// The cutoff resolution (<c>minimumDisplayScale × DenomToResolutionMetres</c>),
    /// or <see langword="null"/> when no <c>DataCoverage</c> feature carries a
    /// usable <c>minimumDisplayScale</c>. When several <c>DataCoverage</c>
    /// features declare different bands, the most permissive (largest
    /// denominator) is used so detail stays visible wherever any coverage
    /// region still permits it.
    /// </returns>
    internal static double? ResolveOutOfBandMaxResolution(
        IEnumerable<EncDotNet.S100.Pipelines.Vector.Feature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        int? minDisplayScale = null;
        foreach (var feature in features)
        {
            if (!string.Equals(feature.FeatureType, "DataCoverage", StringComparison.Ordinal))
                continue;
            if (!feature.Attributes.TryGetValue("minimumDisplayScale", out var raw) || raw is null)
                continue;
            if (!int.TryParse(raw.ToString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var denom) || denom <= 0)
                continue;

            minDisplayScale = minDisplayScale is null ? denom : Math.Max(minDisplayScale.Value, denom);
        }

        return minDisplayScale is null
            ? null
            : minDisplayScale.Value * MapsuiDisplayListRenderer.DenomToResolutionMetres;
    }

    /// <summary>
    /// Clamps the maximum visible resolution (zoomed-out limit) of every
    /// style on <paramref name="features"/> to <paramref name="maxResolution"/>,
    /// suppressing point/line/text detail when the viewport is zoomed out
    /// past the cell's intended scale band. The clamp only ever
    /// <em>reduces</em> visibility: a tighter SCAMIN-derived limit already
    /// applied by the renderer is preserved, and any style with a
    /// non-positive (sentinel) limit is left untouched.
    /// </summary>
    /// <param name="features">The Mapsui features to cap (point/line/text).</param>
    /// <param name="maxResolution">The band cutoff resolution (m/px).</param>
    internal static void ApplyOutOfScaleBandCap(IEnumerable<IFeature> features, double maxResolution)
    {
        ArgumentNullException.ThrowIfNull(features);

        foreach (var mapFeature in features)
        {
            foreach (var style in mapFeature.Styles)
            {
                if (style is null) continue;
                // Only tighten: default MaxVisible (double.MaxValue) and any
                // looser SCAMIN limit collapse to the band cap; a tighter
                // SCAMIN limit wins; non-positive sentinels are preserved.
                if (style.MaxVisible > 0)
                    style.MaxVisible = Math.Min(style.MaxVisible, maxResolution);
            }
        }
    }

    /// <summary>
    /// Tags every Mapsui feature on <paramref name="layer"/> with the
    /// <see cref="EncDotNet.S100.Datasets.Pipelines.Interoperability.FeatureTagKeys.FeatureType"/>
    /// (and, for <c>DepthContour</c>, the numeric depth value under
    /// <see cref="EncDotNet.S100.Datasets.Pipelines.Interoperability.FeatureTagKeys.DepthContourValue"/>).
    /// </summary>
    /// <remarks>
    /// The Mapsui renderer stamps each <c>IFeature</c> with the
    /// originating S-100 feature reference under
    /// <see cref="MapsuiDisplayListRenderer.FeatureRefKey"/>. We read
    /// that, look up the originating <see cref="Pipelines.Vector.Feature"/>
    /// in the lazily-built feature index, and copy the feature-type
    /// code plus the safety-contour exception payload (VALDCO /
    /// <c>valueOfDepthContour</c>, S-101 FC §3.1.1) onto the Mapsui
    /// feature. This is the data the PR-L2 R-101-102-B rule consumes
    /// to suppress depth area / contour features while preserving the
    /// safety contour (MSC.232(82) §5.8).
    /// </remarks>
    private void TagMapsuiFeaturesWithFeatureType(ILayer layer)
    {
        if (layer is not MemoryLayer memoryLayer) return;

        _featureIndex ??= BuildFeatureIndex();

        foreach (var mapFeature in memoryLayer.Features)
        {
            if (mapFeature[MapsuiDisplayListRenderer.FeatureRefKey] is not string featureRef)
                continue;

            if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var id))
                continue;

            if (!_featureIndex.TryGetValue(id, out var feature))
                continue;

            mapFeature[Interoperability.FeatureTagKeys.FeatureType] = feature.FeatureType;

            if (string.Equals(feature.FeatureType, "DepthContour", StringComparison.Ordinal) &&
                feature.Attributes.TryGetValue("valueOfDepthContour", out var depthRaw) &&
                depthRaw is not null)
            {
                mapFeature[Interoperability.FeatureTagKeys.DepthContourValue] = depthRaw;
            }
        }
    }

    private MapsuiDisplayListRenderer CreateRenderer(
        S101PortrayalCatalogue catalogue,
        ColorPalette palette,
        RenderContext? context,
        string suffix,
        string? patternClipCacheKey)
    {
        return new MapsuiDisplayListRenderer
        {
            LayerName = $"S-101 ({suffix}): {_fileName}",
            Product = "S-101",
            Palette = palette,
            AssetCache = _renderAssetCache,
            // Only the area renderer carries pattern fills, so wire the clip
            // cache there; the line renderer has no pattern fills to clip.
            PatternClipCache = patternClipCacheKey is not null ? _patternClipCache : null,
            PatternClipCacheKey = patternClipCacheKey,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = symbolName =>
            {
                try
                {
                    var svg = catalogue.GetSymbol(symbolName);
                    return svg.SvgContent;
                }
                catch
                {
                    return null;
                }
            },
            AreaFillProvider = fillName =>
            {
                try
                {
                    return catalogue.GetAreaFill(fillName);
                }
                catch
                {
                    return null;
                }
            },
            LineStyleProvider = name =>
            {
                try { return catalogue.GetLineStyle(name); }
                catch { return null; }
            },
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var featureId))
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
        // Dictionary preserves insertion order; the ordinal matches
        // EnumerateFeatures' enumeration position.
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

    /// <summary>
    /// Runs the V-4 S-101 validation rule pack
    /// (<see cref="S101DatasetRules.Default"/>) against the parsed
    /// document, returning the resulting <see cref="ValidationReport"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the processor integration shape defined by
    /// <c>docs/design/non-gml-validation.md</c> §9.3: the document is
    /// projected through the spec-vocabulary
    /// <see cref="S101DatasetView"/> façade (design §3.1, option (b))
    /// using the bundled <see cref="FeatureCatalogueDecoder"/> for
    /// FC-conformance rules, then handed to the cached default rule
    /// set. The report is cached on first call; subsequent calls
    /// return the same instance (design §9.4).
    /// </para>
    /// <para>
    /// When no S-101 Feature Catalogue is available the façade is
    /// built without a decoder; rules requiring catalogue lookup
    /// (<c>S101-R-1.2</c>, <c>S101-R-4.1</c>) degrade to no-ops per
    /// design §8.1. Reader-level parse failures occur in the
    /// constructor and never reach this method; the
    /// <c>S101-PROJ-PARSE</c> rule is a documented placeholder for
    /// future reader diagnostics (design §5.2 Stance A).
    /// </para>
    /// </remarks>
    public ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            EnsureDecoder();
            var view = S101DatasetView.From(_dataset.Document, _decoder);
            _validationReport = S101DatasetRules.Default.Run(view);
            _validationCached = true;
        }
        return _validationReport;
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
        var vectorSource = new S101VectorSource(_dataset);
        var features = vectorSource.GetFeatures();
        var index = new Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>(features.Count);
        foreach (var f in features)
            index[f.Id] = f;
        return index;
    }
}
