using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Diagnostics;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Abstract base class for GML-based dataset processors that share the
/// standard S-100 Part 9 vector portrayal pipeline template: parse →
/// catalogue setup → FeatureXML projection → XSLT pipeline → Mapsui
/// display-list rendering.
/// </summary>
/// <remarks>
/// Subclasses provide the spec-specific pieces (dataset parsing, catalogue
/// creation, feature XML source) via abstract/virtual members. The base
/// handles the shared pipeline orchestration, feature-info construction,
/// enumeration, and extent computation.
/// </remarks>
/// <typeparam name="TFeature">
/// The concrete feature type constrained to <see cref="IS100Feature"/>.
/// </typeparam>
public abstract class GmlDatasetProcessorBase<TFeature> : IDatasetProcessor, IVectorPortrayalSource, IHeadlessImageRenderer, IDisplayModeAwareDatasetProcessor
    where TFeature : IS100Feature
{
    private readonly GmlPortrayalCatalogueBase _catalogue;
    private readonly FeatureCatalogueDecoder? _decoder;
    private readonly string _fileName;
    private readonly IDisplayPlaneAuthorityProvider _authorityProvider;

    // Serializes portrayal builds for this processor. The build mutates
    // shared catalogue state (palette switch, ECDIS apply) and the asset
    // pre-warm, so concurrent renders (e.g. Day/Night while a tile request
    // is in flight) must not interleave. Mirrors the S-101 render gate.
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    /// <summary>
    /// Initializes the shared processor state. Called by subclass constructors
    /// after parsing the dataset and creating the catalogue.
    /// </summary>
    /// <param name="catalogue">Portrayal catalogue used by the XSLT pipeline.</param>
    /// <param name="decoder">Optional feature-catalogue decoder for attribute decoding.</param>
    /// <param name="fileName">Dataset file name (used in feature info).</param>
    /// <param name="authorityProvider">
    /// Resolves the default S-98 display plane for this dataset's content.
    /// Required — processors receive the provider via DI rather than reaching
    /// for a static singleton. The Mapsui-typed cross-dataset sort authority
    /// lives in the renderer package; the processor only needs the plane.
    /// </param>
    protected GmlDatasetProcessorBase(
        GmlPortrayalCatalogueBase catalogue,
        FeatureCatalogueDecoder? decoder,
        string fileName,
        IDisplayPlaneAuthorityProvider authorityProvider,
        string specName)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(authorityProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(specName);
        _catalogue = catalogue;
        _decoder = decoder;
        _fileName = fileName;
        _authorityProvider = authorityProvider;

        // Seed the spec with its canonical name and an unknown edition; the
        // declared edition is refined by SetDeclaredEdition once the subclass
        // has parsed the dataset. Catalogue-resolution telemetry is emitted
        // there too, so it reflects the declared edition.
        Spec = new SpecRef(specName, default);
    }

    /// <inheritdoc/>
    public SpecRef Spec { get; private set; }

    /// <inheritdoc/>
    public SpecVersionAssessment? VersionAssessment { get; private set; }

    /// <summary>
    /// Records the dataset's declared product-spec edition (parsed from the
    /// GML <c>productEdition</c> / application-schema namespace) and computes
    /// the <see cref="VersionAssessment"/> against the edition this build
    /// implements. Subclasses call this once, from their constructor, after
    /// parsing the dataset. Also emits the one-shot catalogue-resolution
    /// telemetry for this processor instance.
    /// </summary>
    /// <param name="declaredEdition">
    /// The declared edition string (e.g. <c>"2.0.0"</c>), or <c>null</c> when
    /// the dataset declares none.
    /// </param>
    protected void SetDeclaredEdition(string? declaredEdition)
    {
        if (!string.IsNullOrWhiteSpace(declaredEdition)
            && SpecVersion.TryParse(declaredEdition, out var edition))
        {
            Spec = new SpecRef(Spec.Name, edition);
        }

        VersionAssessment = SupportedSpecEditions.Assess(Spec, _catalogue.CatalogueRef);
        CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
    }

    /// <summary>
    /// Human-readable product description for info strings (e.g.
    /// "Navigational Warnings", "Marine Aids to Navigation").
    /// </summary>
    protected abstract string ProductDescription { get; }

    /// <summary>The parsed features from the dataset.</summary>
    protected abstract IReadOnlyList<TFeature> Features { get; }

    /// <summary>Creates the spec-appropriate feature XML source.</summary>
    protected abstract IFeatureXmlSource CreateFeatureXmlSource();

    /// <summary>
    /// Minimum extent padding in degrees. Default is 0.01; override for
    /// specs that need wider padding (e.g. S-421 uses 0.05).
    /// </summary>
    protected virtual double MinExtentPadding => 0.01;

    /// <summary>
    /// Builds feature references for the pick UI. Override to expose
    /// xlink/information references as navigable links.
    /// </summary>
    protected virtual IReadOnlyList<FeatureReference> BuildFeatureReferences(TFeature feature) => [];

    /// <summary>
    /// Called before the pipeline runs. Return a non-null info string to
    /// suppress rendering (the dataset contributes no portrayal for this
    /// context, e.g. S-411 hides when the time slider is before the issue
    /// date); the returned text becomes the dataset's status line.
    /// </summary>
    protected virtual string? GetSuppressionInfo(RenderContext? context) => null;

    /// <summary>
    /// Post-processes drawing instructions after the pipeline runs. Override
    /// to apply fallback fills or other transformations (e.g. S-129 area
    /// fill fallback).
    /// </summary>
    protected virtual IReadOnlyList<DrawingInstruction> PostProcessInstructions(
        IReadOnlyList<DrawingInstruction> instructions) => instructions;

    /// <summary>
    /// Appends spec-specific lines to the info string. Override to add
    /// counts like "Information types: N".
    /// </summary>
    protected virtual string BuildInfoSuffix() => string.Empty;

    /// <summary>The portrayal catalogue for this processor.</summary>
    protected GmlPortrayalCatalogueBase Catalogue => _catalogue;

    /// <summary>The feature catalogue decoder, if available.</summary>
    protected FeatureCatalogueDecoder? Decoder => _decoder;

    /// <summary>The dataset file name.</summary>
    protected string FileName => _fileName;

    /// <inheritdoc/>
    public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var suppressedInfo = GetSuppressionInfo(context);
            if (suppressedInfo is not null)
            {
                return new VectorPortrayalResult
                {
                    SubLayers = Array.Empty<VectorSubLayer>(),
                    Palette = _catalogue.ActivePalette,
                    GeometryProvider = new FeatureGeometryProvider<TFeature>(Features),
                    Product = Spec.Name,
                    Spec = Spec,
                    SourceDatasetId = _fileName,
                    Info = suppressedInfo,
                    GeographicExtent = ComputeGeographicExtent(),
                };
            }

            var catalogue = _catalogue;
            context?.EcdisDisplay?.ApplyTo(catalogue);
            ApplyDisplayMode(catalogue, context);
            await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

            var featureSource = CreateFeatureXmlSource();
            var pipeline = new PortrayalPipeline();
            var portrayalLayer = await pipeline.ProcessAsync(featureSource, catalogue, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var instructions = PostProcessInstructions(((IVectorLayer)portrayalLayer).Instructions);

            Console.WriteLine($"[{Spec.Name.Replace("-", "")}] {_fileName}: {Features.Count} features, "
                + $"{instructions.Count} drawing instructions");

            var prewarm = await CataloguePreWarm.ForInstructionsAsync(catalogue, instructions, cancellationToken).ConfigureAwait(false);

            var featureTypes = featureSource.FeatureTypesPresent;
            var suffix = BuildInfoSuffix();
            var info = $"{Spec.Name} {ProductDescription} — {_fileName}\n"
                + $"Features: {Features.Count} ({string.Join(", ", featureTypes)})\n"
                + (suffix.Length > 0 ? suffix + "\n" : "")
                + $"Drawing instructions: {instructions.Count}";

            var subLayer = new VectorSubLayer
            {
                LayerKey = Spec.Name,
                LayerName = $"{Spec.Name}: {_fileName}",
                Instructions = instructions,
                // S-98 cross-dataset plane assignment per design note §3 / §4.2.
                // PR-L1 ships default planes only — no IC override, no per-feature
                // filter (TBD-5). The plane is resolved through the active
                // default-plane authority via the injected provider.
                Plane = _authorityProvider.Current.GetDefaultPlane(Spec.Name),
                WithinPlanePriority = 0,
            };

            return new VectorPortrayalResult
            {
                SubLayers = new[] { subLayer },
                Palette = catalogue.ActivePalette,
                GeometryProvider = new FeatureGeometryProvider<TFeature>(Features),
                Product = Spec.Name,
                Spec = Spec,
                SourceDatasetId = _fileName,
                Info = info,
                SymbolScale = context?.SymbolScale ?? 1.0,
                TextScale = context?.TextScale ?? 1.0,
                SymbolProvider = name => prewarm.ResolveSymbolSvg(name),
                AreaFillProvider = name => prewarm.ResolveAreaFill(name),
                LineStyleProvider = name => prewarm.ResolveLineStyle(name),
                GeographicExtent = ComputeGeographicExtent(),
            };
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>
    /// Renders this dataset to a standalone <see cref="SKBitmap"/> through the
    /// headless, backend-agnostic Skia vector core
    /// (<see cref="VectorSceneBuilder"/> → <see cref="SkiaDisplayListRenderer"/>),
    /// bypassing Mapsui entirely. This is the vector analogue of the direct-Skia
    /// coverage renderer and the basis for a headless tile-serving API.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, symbol/text scale, ECDIS display settings).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    /// <remarks>
    /// Tiled-symbol pattern area-fills are rasterised through
    /// <see cref="SkiaSvgRasterizer.RasterizePatternTile"/> and tiled across
    /// the polygon, anchored to a global world-space origin so adjacent
    /// polygons sharing a pattern align seamlessly. Unlike the Mapsui path,
    /// the headless renderer does not perform NetTopologySuite
    /// priority-clipping of overlapping patterns or land-occlusion, so
    /// patterns may visibly bleed across opaque overlay fills. The viewport
    /// is auto-fitted to the dataset extent and padded to the output aspect
    /// ratio.
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
        cancellationToken.ThrowIfCancellationRequested();

        var bg = background ?? new RgbaColor(255, 255, 255, 255);

        // Honour the same pre-render gate as the Mapsui path (e.g. S-411
        // time-window suppression). When the gate fires, the dataset
        // contributes no portrayal for this context, so emit a blank
        // background-filled bitmap rather than rendering stale content.
        if (GetSuppressionInfo(context) is not null)
            return HeadlessVectorRenderer.RenderBlank(widthPixels, heightPixels, bg);

        var catalogue = _catalogue;
        context?.EcdisDisplay?.ApplyTo(catalogue);
        ApplyDisplayMode(catalogue, context);
        await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        var featureSource = CreateFeatureXmlSource();
        var pipeline = new PortrayalPipeline();
        var portrayalLayer = await pipeline.ProcessAsync(featureSource, catalogue, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var instructions = PostProcessInstructions(((IVectorLayer)portrayalLayer).Instructions);

        var prewarm = await CataloguePreWarm.ForInstructionsAsync(catalogue, instructions, cancellationToken).ConfigureAwait(false);

        var geometryProvider = new FeatureGeometryProvider<TFeature>(Features);

        return HeadlessVectorRenderer.Render(
            instructions,
            geometryProvider,
            catalogue.ActivePalette,
            symbolProvider: name => prewarm.ResolveSymbolSvg(name),
            lineStyleProvider: name => prewarm.ResolveLineStyle(name),
            symbolScale: context?.SymbolScale ?? 1.0,
            textScale: context?.TextScale ?? 1.0,
            widthPixels: widthPixels,
            heightPixels: heightPixels,
            background: bg,
            areaFillProvider: name => prewarm.ResolveAreaFill(name),
            hiddenCategories: context?.HiddenInstructionCategories
                ?? DrawingInstructionCategory.None,
            basemap: context?.Basemap ?? BasemapKind.None);
    }

    /// <summary>
    /// Applies the context's explicit S-100 Part 9 §11.7 display-mode
    /// selection to the catalogue, when set and declared. Called after
    /// <see cref="EcdisDisplayExtensions.ApplyTo"/> so an explicit spec-native
    /// mode id (e.g. an S-411 concentration / stage-of-development /
    /// navigational selection) wins over the ECDIS-category-derived mode. A
    /// null or undeclared id leaves the catalogue's current mode untouched.
    /// </summary>
    private void ApplyDisplayMode(GmlPortrayalCatalogueBase catalogue, RenderContext? context)
    {
        var modeId = context?.DisplayModeId;
        if (string.IsNullOrEmpty(modeId))
            return;

        if (catalogue.DisplayModes.DeclaredModeIds.Contains(modeId))
            catalogue.DisplayModes.SetActive(modeId);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> DeclaredDisplayModeIds => _catalogue.DisplayModes.DeclaredModeIds.ToList();

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = Features.FirstOrDefault(f =>
            string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return feature is null ? null : BuildFeatureInfo(feature);
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= Features.Count)
            return null;
        return BuildFeatureInfo(Features[ordinal]);
    }

    /// <inheritdoc/>
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        for (int i = 0; i < Features.Count; i++)
        {
            var feature = Features[i];
            yield return new FeatureSummary
            {
                FeatureRef = feature.Id,
                Ordinal = i,
                FeatureType = feature.FeatureType,
                FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            };
        }
    }

    /// <summary>
    /// Hook for spec-specific subclasses to run their normative
    /// validation rule pack against the parsed dataset. Returns
    /// <c>null</c> by default, signalling that no rule pack is
    /// defined for this product.
    /// </summary>
    /// <remarks>
    /// Overrides should typically delegate to
    /// <c>ValidationRunner.Run(...)</c> so that an
    /// <see cref="System.InvalidOperationException"/> from the typed
    /// projection (e.g. an empty dataset) surfaces as an empty
    /// report rather than propagating out of <see cref="Validate"/>.
    /// Overrides should also cache the result so repeated calls are
    /// cheap.
    /// </remarks>
    public virtual ValidationReport? Validate() => null;

    private FeatureInfo BuildFeatureInfo(TFeature feature)
    {
        var attributes = FeatureInfoBuilder.Build(
            feature.Attributes,
            feature.ComplexAttributes.Select(c =>
                new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
            _decoder);

        var references = BuildFeatureReferences(feature);

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
            References = references,
        };
    }

    /// <summary>
    /// Computes the padded geographic extent (lat / lon degrees) of all
    /// features. The Mapsui renderer projects this to Spherical Mercator;
    /// keeping it Mapsui-free lets the headless path share the same bounds.
    /// </summary>
    protected GeographicBounds ComputeGeographicExtent()
    {
        double minLon = double.MaxValue, minLat = double.MaxValue;
        double maxLon = double.MinValue, maxLat = double.MinValue;
        bool any = false;

        void Expand(double lat, double lon)
        {
            any = true;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        foreach (var feature in Features)
        {
            foreach (var (lat, lon) in feature.Points) Expand(lat, lon);
            foreach (var curve in feature.Curves)
                foreach (var (lat, lon) in curve) Expand(lat, lon);
            foreach (var (lat, lon) in feature.ExteriorRing) Expand(lat, lon);
        }

        if (!any) return new GeographicBounds(0, 0, 0, 0);

        var pad = MinExtentPadding;
        var latPad = Math.Max(pad, (maxLat - minLat) * 0.1);
        var lonPad = Math.Max(pad, (maxLon - minLon) * 0.1);
        return new GeographicBounds(
            minLon - lonPad,
            minLat - latPad,
            maxLon + lonPad,
            maxLat + latPad);
    }
}
