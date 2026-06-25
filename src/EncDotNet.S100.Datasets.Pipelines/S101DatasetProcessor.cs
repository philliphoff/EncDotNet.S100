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
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S101DatasetProcessor : IDatasetProcessor, IVectorPortrayalSource, IHeadlessImageRenderer
{
    private readonly S101Dataset _dataset;
    private readonly S101UpdateReport? _updateReport;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S101PortrayalCatalogue _catalogue;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly ILuaEngine _luaEngine;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;

    // Resolves the textual content of external text files named by
    // `fileReference` attributes (S-101 FC, alias TXTDSC / NTXTDS) from the
    // dataset's exchange-set asset source, so the pick / object-info path can
    // surface the referenced text (e.g. Caution Area, Tidal Stream Panel
    // Data). Null when the dataset was opened from a bare stream with no
    // resolvable support-file location.
    private readonly Func<string, string?>? _externalTextResolver;

    // Deterministic per-dataset scope prepended to the portrayal cache key when
    // forming the (Mapsui-side) pattern-clip cache key. Encodes dataset content
    // (SHA-256) + name + edition + CRS so the resulting clip key is globally
    // unique and self-invalidating. The Mapsui renderer appends its own
    // algorithm / serialization-format qualifiers. Constant for the lifetime of
    // this processor.
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

    public SpecRef Spec { get; }

    /// <inheritdoc/>
    public SpecVersionAssessment? VersionAssessment { get; }

    /// <summary>
    /// Outcome of applying sequential updates when this processor was constructed
    /// for a base cell plus in-set update files; otherwise <see langword="null"/>.
    /// </summary>
    public S101UpdateReport? UpdateReport => _updateReport;

    public S101DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPortrayalInstructionCache? sharedInstructionCache = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, featureCatalogueManager, sharedInstructionCache, CreateFileSystemResolver(path))
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
        IPortrayalInstructionCache? sharedInstructionCache = null,
        IReadOnlyDictionary<string, string>? supportFiles = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            luaEngine,
            featureCatalogueManager,
            sharedInstructionCache,
            new ExternalTextFileResolver(source, relativePath, supportFiles).AsDelegate())
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S101DatasetProcessor"/> for a base cell at
    /// <paramref name="baseRelativePath"/> with the sequential update files at
    /// <paramref name="updateRelativePaths"/> applied (best-effort) before
    /// portrayal. Used by exchange-set bulk loading to collapse a cell and its
    /// in-set updates into a single up-to-date dataset; the apply outcome is
    /// exposed via <see cref="UpdateReport"/>. S-101 / S-100 Part 10a.
    /// </summary>
    public S101DatasetProcessor(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPortrayalInstructionCache? sharedInstructionCache = null,
        IReadOnlyDictionary<string, string>? supportFiles = null)
        : this(
            PrepareWithUpdates(source, baseRelativePath, updateRelativePaths),
            AssetSourceHelpers.GetFileName(baseRelativePath),
            catalogueManager,
            luaEngine,
            featureCatalogueManager,
            sharedInstructionCache,
            new ExternalTextFileResolver(source, baseRelativePath, supportFiles).AsDelegate())
    {
    }

    private S101DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPortrayalInstructionCache? sharedInstructionCache,
        Func<string, string?>? externalTextResolver = null)
        : this(
            PrepareFromStream(datasetStream),
            fileName,
            catalogueManager,
            luaEngine,
            featureCatalogueManager,
            sharedInstructionCache,
            externalTextResolver)
    {
    }

    private S101DatasetProcessor(
        PreparedDataset prepared,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager,
        IPortrayalInstructionCache? sharedInstructionCache,
        Func<string, string?>? externalTextResolver = null)
    {
        _fileName = fileName;
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-101");
        _catalogueManager = catalogueManager;
        _catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);
        _externalTextResolver = externalTextResolver;

        _dataset = prepared.Dataset;
        _updateReport = prepared.Report;

        // S-101 declares its product-spec edition in the ISO 8211 dataset
        // identification (PRED subfield); surface it so the pipeline can warn
        // on a version mismatch with the edition this build implements.
        var declaredEdition = _dataset.Document.Identification?.ProductSpecificationEdition;
        Spec = !string.IsNullOrWhiteSpace(declaredEdition)
            && SpecVersion.TryParse(declaredEdition, out var s101Edition)
            ? new SpecRef("S-101", s101Edition)
            : new SpecRef("S-101", default);

        _patternClipScope = BuildDatasetScopeKey(prepared.ScopeBytes, _dataset);

        // When a shared instruction cache is injected use it (persistent,
        // cross-cell, process-global); otherwise fall back to a bounded
        // per-processor in-memory LRU so the cross-load behaviour is exercised
        // even without a host-supplied disk cache.
        _instructionCache = sharedInstructionCache ?? new InMemoryPortrayalInstructionCache();

        _featureCatalogueManager = featureCatalogueManager;

        Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
        VersionAssessment = SupportedSpecEditions.Assess(Spec, _catalogue.CatalogueRef);
    }

    /// <summary>A parsed dataset paired with the bytes used for the cache scope key and an optional update report.</summary>
    private readonly record struct PreparedDataset(S101Dataset Dataset, byte[] ScopeBytes, S101UpdateReport? Report);

    /// <summary>
    /// Buffers and parses a single dataset stream (no updates). S-101 cells are
    /// small enough that buffering the raw bytes for content hashing is cheap.
    /// </summary>
    private static PreparedDataset PrepareFromStream(Stream datasetStream)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        byte[] datasetBytes;
        using (datasetStream)
        {
            datasetBytes = ReadAllBytes(datasetStream);
        }
        var dataset = S101Dataset.Open(new MemoryStream(datasetBytes, writable: false));
        return new PreparedDataset(dataset, datasetBytes, Report: null);
    }

    /// <summary>
    /// Reads a base cell plus its update files from <paramref name="source"/> and
    /// applies them (best-effort) into a single up-to-date dataset. A file that
    /// fails to read, or an invalid / non-contiguous update, is recorded in the
    /// returned report and never aborts the load.
    /// </summary>
    private static PreparedDataset PrepareWithUpdates(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(baseRelativePath);
        ArgumentNullException.ThrowIfNull(updateRelativePaths);

        var baseBytes = ReadAsset(source, baseRelativePath);
        var baseDocument = S101Dataset.Open(new MemoryStream(baseBytes, writable: false)).Document;

        var scopeStream = new MemoryStream();
        scopeStream.Write(baseBytes, 0, baseBytes.Length);

        var readMessages = new List<S101UpdateMessage>();
        var updates = new List<S101Document>(updateRelativePaths.Count);
        foreach (var updatePath in updateRelativePaths)
        {
            try
            {
                var updateBytes = ReadAsset(source, updatePath);
                scopeStream.Write(updateBytes, 0, updateBytes.Length);
                updates.Add(S101Dataset.Open(new MemoryStream(updateBytes, writable: false)).Document);
            }
            catch (Exception ex)
            {
                readMessages.Add(new S101UpdateMessage(
                    S101UpdateSeverity.Error,
                    $"Failed to read update '{AssetSourceHelpers.GetFileName(updatePath)}': {ex.Message}."));
            }
        }

        updates.Sort((a, b) => a.Identification.UpdateNumber.CompareTo(b.Identification.UpdateNumber));

        var merged = S101UpdateApplicator.Apply(baseDocument, updates, out var report);

        if (readMessages.Count > 0)
        {
            report = new S101UpdateReport
            {
                BaseUpdateNumber = report.BaseUpdateNumber,
                AppliedThroughUpdateNumber = report.AppliedThroughUpdateNumber,
                Inserted = report.Inserted,
                Deleted = report.Deleted,
                Modified = report.Modified,
                Messages = [.. readMessages, .. report.Messages],
            };
        }

        return new PreparedDataset(S101Dataset.FromDocument(merged), scopeStream.ToArray(), report);
    }

    private static byte[] ReadAsset(IAssetSource source, string relativePath)
    {
        using var stream = AssetSourceHelpers.OpenSeekable(source, relativePath);
        return ReadAllBytes(stream);
    }

    /// <summary>
    /// Builds an external-text resolver for a loose dataset file on the local
    /// file system, rooted at the dataset's directory so co-located support
    /// text files named by <c>fileReference</c> attributes resolve. Returns
    /// <c>null</c> when the path has no directory component.
    /// </summary>
    private static Func<string, string?>? CreateFileSystemResolver(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory))
            return null;

        var source = FileSystemAssetSource.Create(directory);
        return new ExternalTextFileResolver(source, Path.GetFileName(path)).AsDelegate();
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
    /// portrayal cache key, qualifies the (Mapsui-side) pattern-clip cache key
    /// with this dataset's content identity so it is unique per cell and
    /// self-invalidating on content change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern-fill priority clip geometry is a pure function of the dataset
    /// geometry and the clip parameters (it is palette-independent — the palette
    /// only recolours tiles applied after clipping). This scope encodes the
    /// content-identity portion that the renderer cannot know:
    /// </para>
    /// <list type="bullet">
    /// <item>the SHA-256 of the raw dataset bytes (content identity);</item>
    /// <item>the S-101 product-specification edition and dataset name from the
    /// DSID record (belt-and-suspenders alongside the content hash);</item>
    /// <item>the rendering CRS (EPSG:3857, the renderer's fixed projection).</item>
    /// </list>
    /// <para>
    /// The Mapsui renderer appends the clip algorithm parameters and the
    /// serialization format-version stamp before consulting its pattern-clip
    /// cache, so any change to dataset content, clip parameters, the CRS, or the
    /// cache / serialization format yields a cache miss (recompute) and persisted
    /// geometry can never be reused incorrectly.
    /// </para>
    /// </remarks>
    internal static string BuildDatasetScopeKey(byte[] datasetBytes, S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(datasetBytes);
        ArgumentNullException.ThrowIfNull(dataset);

        var contentHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(datasetBytes)).ToLowerInvariant();

        var id = dataset.Document.Identification;
        var sb = new StringBuilder();
        sb.Append("ds:").Append(contentHash);
        sb.Append("|name:").Append(id.DatasetName);
        sb.Append("|ed:").Append(id.ProductSpecificationEdition);
        sb.Append("|crs:EPSG:3857");
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

    public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Serialize against re-entrant renders that share the long-lived
        // catalogue state and the portrayal-instruction cache. Everything that
        // reads mutable catalogue state (palette switch, ECDIS apply, Lua
        // pipeline, asset pre-warm) is snapshotted here so the returned result
        // is safe to convert to Mapsui layers off the gate.
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
    /// Tiled-symbol pattern area-fills (e.g. shallow-water diamonds,
    /// quality-of-bathymetry overlays) are rasterised through
    /// <see cref="SkiaSvgRasterizer.RasterizePatternTile"/> and tiled across
    /// the polygon, anchored to a global world-space origin so adjacent
    /// polygons sharing a pattern align seamlessly. Unlike the Mapsui path,
    /// the headless renderer does not perform NetTopologySuite
    /// priority-clipping of overlapping patterns or land-occlusion, so
    /// patterns may visibly bleed across opaque overlay fills. Use
    /// <see cref="BuildVectorPortrayalAsync"/> (the Mapsui path) for full pattern-fill
    /// fidelity. Unlike that path, this produces a single bitmap (no S-102
    /// interleave split) and draw order follows the shared core's S-100
    /// Part 9 ordering.
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
            await s101Cat.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);
            (context?.EcdisDisplay ?? UnfilteredEcdisDisplay).ApplyTo(s101Cat);
            var palette = s101Cat.ActivePalette;

            var executor = new S101LuaRuleExecutor(_luaEngine, _dataset, s101Cat, fc);
            var featureSource = new S101FeatureXmlSource(_dataset);
            var pipeline = new PortrayalPipeline(executor);
            var portrayalLayer = await pipeline
                .ProcessAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var prepared = ((IVectorLayer)portrayalLayer).Instructions;

            var geometryProvider = new FeatureGeometryProvider<Feature>(new S101VectorSource(_dataset).GetFeatures());

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

    private async Task<VectorPortrayalResult> BuildVectorPortrayalCoreAsync(RenderContext? context, CancellationToken cancellationToken)
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
        await s101Cat.SwitchPaletteAsync(paletteType, cancellationToken).ConfigureAwait(false);

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
            prepared = await _instructionCache.GetOrComputeAsync(instructionKey, async ct =>
            {
                // Drive the unified VectorPipeline with the S-101 Lua rule
                // executor (Part 9A). XSLT rules in the S-101 catalogue (if
                // any) are also honoured by the pipeline.
                var executor = new S101LuaRuleExecutor(_luaEngine, _dataset, s101Cat, fc);
                var featureSource = new S101FeatureXmlSource(_dataset);
                var pipeline = new PortrayalPipeline(executor);
                var portrayalLayer = await pipeline
                    .ProcessAsync(featureSource, s101Cat, mariner: mariner, cancellationToken: ct)
                    .ConfigureAwait(false);
                return ((IVectorLayer)portrayalLayer).Instructions;
            }, cancellationToken).ConfigureAwait(false);
            _cachedPortrayalKey = cacheKey;
            _cachedPortrayalInstructions = prepared;
            PortrayalCacheMisses++;
            Console.WriteLine($"[S101] Pipeline produced {prepared.Count} drawing instructions");
        }

        // S-98 R-101-102-A (Annex A §A-6.9.1): S-102 must render between
        // S-101 area fills and S-101 line work / points / text. We split
        // the S-101 display list along the AreaInstruction boundary into
        // two sub-layers so the Mapsui renderer's LayerStackBuilder can
        // interleave S-102. Splitting here (in the processor) keeps the
        // type pre-filter Mapsui-free; the renderer rasterises each slice.
        var areaInstructions = prepared.Where(i => i is AreaInstruction).ToList();
        var otherInstructions = prepared.Where(i => i is not AreaInstruction).ToList();

        var geometryProvider = new FeatureGeometryProvider<Feature>(new S101VectorSource(_dataset).GetFeatures());

        // Mapsui-free pattern-clip cache identity: the per-dataset scope
        // (content hash + name + edition + CRS) qualified by the portrayal-
        // content hash and per-render cache key. The Mapsui renderer appends
        // its own clip-algorithm / serialization-format qualifiers before
        // consulting the actual pattern-clip cache it owns.
        var patternClipKey = $"{_patternClipScope}|{await GetPortrayalContentHashAsync(cancellationToken).ConfigureAwait(false)}|{cacheKey}";

        var prewarm = await CataloguePreWarm.ForInstructionsAsync(s101Cat, prepared, cancellationToken).ConfigureAwait(false);

        // PR-L2 R-101-102-B: feature tags (S-101 feature-type code and, for
        // DepthContour, the VALDCO depth value) the Mapsui renderer copies onto
        // each built IFeature so the S-98 rule engine can suppress depth
        // features without re-running portrayal. See S98DefaultRules.
        _featureIndex ??= BuildFeatureIndex();
        var featureTags = BuildFeatureTags(_featureIndex.Values);

        // Out-of-scale-band declutter cutoff (S-101 DataCoverage /
        // minimumDisplayScale, FC §3.1.1): the most-permissive denominator the
        // Mapsui renderer multiplies into a maximum visible resolution and
        // clamps onto the line-work styles. Honour the mariner's
        // IgnoreScaleMinimum override (disables the cap, consistent with SCAMIN).
        var outOfBandMinDisplayScale = mariner.IgnoreScaleMinimum
            ? (int?)null
            : ResolveOutOfBandMinDisplayScale(_featureIndex.Values);

        Console.WriteLine($"[S101-Lua] Prepared {areaInstructions.Count} area + {otherInstructions.Count} non-area instructions");

        var info = $"{_dataset.DatasetName} — {_dataset.FeatureCount} features, " +
                   $"{prepared.Count} instructions";

        return new VectorPortrayalResult
        {
            SubLayers = new[]
            {
                // Area fills land on the deepest base-chart plane so
                // S-102 (Bathymetry, 10) can sit on top of them. Pattern
                // fills live here, so this sub-layer carries the clip key.
                new VectorSubLayer
                {
                    LayerKey = "s101.areas",
                    LayerName = $"S-101 (areas): {_fileName}",
                    Instructions = areaInstructions,
                    Plane = S98DisplayPlane.BaseChartUnder,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "area",
                    PatternClipCacheKey = patternClipKey,
                    ApplyOutOfBandCap = false,
                },
                // Line work, points, symbols, and text remain on the
                // base-chart "over" plane (above Bathymetry); these carry
                // the out-of-scale-band declutter cap and no pattern fills.
                new VectorSubLayer
                {
                    LayerKey = "s101.linework",
                    LayerName = $"S-101 (lines): {_fileName}",
                    Instructions = otherInstructions,
                    Plane = S98DisplayPlane.BaseChartOver,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "linework",
                    PatternClipCacheKey = null,
                    ApplyOutOfBandCap = true,
                },
            },
            Palette = palette,
            GeometryProvider = geometryProvider,
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = _fileName,
            Info = info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = name => prewarm.ResolveSymbolSvg(name),
            AreaFillProvider = name => prewarm.ResolveAreaFill(name),
            LineStyleProvider = name => prewarm.ResolveLineStyle(name),
            LayerNames = new[] { "s101.areas", "s101.linework" },
            FeatureTags = featureTags,
            OutOfBandMinDisplayScale = outOfBandMinDisplayScale,
        };
    }

    /// <summary>
    /// Builds the Mapsui-free feature-tag map (feature id → tag) the renderer
    /// copies onto built features for S-98 depth-feature suppression. Carries
    /// the feature-type code and, for <c>DepthContour</c>, the numeric VALDCO
    /// depth value (preserving the MSC.232(82) §5.8 safety-contour exception).
    /// </summary>
    private static IReadOnlyDictionary<long, VectorFeatureTag> BuildFeatureTags(
        IEnumerable<EncDotNet.S100.Pipelines.Vector.Feature> features)
    {
        var tags = new Dictionary<long, VectorFeatureTag>();
        foreach (var feature in features)
        {
            object? depthValue = null;
            if (string.Equals(feature.FeatureType, "DepthContour", StringComparison.Ordinal) &&
                feature.Attributes.TryGetValue("valueOfDepthContour", out var depthRaw) &&
                depthRaw is not null)
            {
                depthValue = depthRaw;
            }

            tags[feature.Id] = new VectorFeatureTag(feature.FeatureType, depthValue);
        }

        return tags;
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
    /// Resolves the cell's out-of-scale-band cutoff as the most-permissive
    /// (largest) <c>minimumDisplayScale</c> denominator across the
    /// <c>DataCoverage</c> features (S-101 FC §3.1.1 — the smallest scale, i.e.
    /// largest denominator, at which the cell is intended to be displayed).
    /// Detail features become invisible once the viewport is zoomed out beyond
    /// this band. Mapsui-free: the renderer multiplies this denominator by its
    /// denominator-to-resolution constant to obtain the maximum visible
    /// resolution it clamps onto the styles.
    /// </summary>
    /// <param name="features">The dataset's vector features.</param>
    /// <returns>
    /// The largest usable <c>minimumDisplayScale</c> denominator, or
    /// <see langword="null"/> when no <c>DataCoverage</c> feature carries one.
    /// When several <c>DataCoverage</c> features declare different bands, the
    /// most permissive (largest denominator) is used so detail stays visible
    /// wherever any coverage region still permits it.
    /// </returns>
    internal static int? ResolveOutOfBandMinDisplayScale(
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

        return minDisplayScale;
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

        // Resolve any `fileReference` attribute (S-101 FC; alias TXTDSC /
        // NTXTDS) to the textual content of the external file it names,
        // co-located in the dataset's exchange set, so the pick / object-info
        // path can surface it (e.g. Caution Area, Tidal Stream Panel Data).
        if (_externalTextResolver is not null)
            attributes = FeatureInfoBuilder.ResolveFileReferences(attributes, _externalTextResolver);

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
