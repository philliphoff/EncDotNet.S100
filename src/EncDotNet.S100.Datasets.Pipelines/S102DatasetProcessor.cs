using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S102.Validation;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S102DatasetProcessor : IDatasetProcessor, ICoveragePortrayalSource, IHeadlessImageRenderer, IDisposable
{
    private readonly S102Dataset _dataset;
    private readonly S102CoverageSource _source;
    private readonly S102PortrayalCatalogue _catalogue;
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly string _fileName;
    private readonly PortrayalPipeline _pipeline;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private ValidationReport? _validationReport;
    private bool _validationCached;

    public SpecRef Spec { get; }

    private DatasetMetadata? _metadata;

    /// <inheritdoc/>
    /// <remarks>
    /// Derived from the coverage source's already-read georeferencing
    /// metadata (root attributes + coverage extent) and the dataset's
    /// horizontal CRS — no HDF5 payload is re-read (issue #467, WS1).
    /// </remarks>
    public DatasetMetadata Metadata => _metadata ??= BuildMetadata();

    private DatasetMetadata BuildMetadata()
    {
        var extent = _source.Metadata.Extent;
        return new DatasetMetadata
        {
            Spec = Spec,
            Extent = new BoundingBox(
                extent.SouthLatitude,
                extent.WestLongitude,
                extent.NorthLatitude,
                extent.EastLongitude),
            HorizontalCrsEpsg = _dataset.HorizontalCRS,
        };
    }

    /// <inheritdoc/>
    public SpecVersionAssessment? VersionAssessment { get; }

    public S102DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, crsTransformFactory)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S102DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/> (e.g. a <c>FileSystemAssetSource</c> or
    /// <c>ZipAssetSource</c>). Used by exchange-set bulk loading where
    /// a dataset's bytes live inside a ZIP archive.
    /// </summary>
    public S102DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            luaEngine,
            crsTransformFactory)
    {
    }

    private S102DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _crsTransformFactory = crsTransformFactory;

        using (datasetStream)
        using (var hdf5 = PureHdfFile.Open(datasetStream))
        {
            try
            {
                _dataset = S102DatasetReader.Read(hdf5);
            }
            catch (S100DatasetSchemaException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
            catch (S100DatasetNotSupportedException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
        }
        _source = new S102CoverageSource(_dataset);

        Spec = HdfDeclaredSpec.Resolve(_dataset.DeclaredProductSpecification, "S-102");

        var provider = catalogueManager.GetProvider("S-102");
        // The viewer overlays S-102 bathymetry on other layers (e.g. an
        // S-101 ENC), so NODATA cells must be transparent rather than
        // painted with the opaque NODTA grey; otherwise the un-surveyed
        // remainder of the rectangular coverage extent obscures the chart
        // beneath. (Standalone S-102 portrayal keeps the default fill.)
        _catalogue = new S102PortrayalCatalogue(luaEngine, provider) { RenderNoDataFill = false };

        // Hoist pipeline to a field: BuildCoveragePortrayalAsync is invoked
        // many times (each redraw) but the pipeline holds no per-render state,
        // so a single instance is safe and avoids repeated allocation on the
        // hot path. The Mapsui coverage renderer lives in the renderer package.
        _pipeline = new PortrayalPipeline();

        Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
        VersionAssessment = SupportedSpecEditions.Assess(Spec, _catalogue.CatalogueRef);
    }

    public void Dispose()
    {
        // PortrayalPipeline is not currently disposable, but keep Dispose
        // explicit so future allocations to these fields can be cleaned up
        // here without further plumbing.
        _renderGate.Dispose();
    }

    /// <inheritdoc/>
    public async Task<CoveragePortrayalResult> BuildCoveragePortrayalAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);
            var metadata = _source.Metadata;

            var viewport = new EncDotNet.S100.Pipelines.Viewport
            {
                MinLatitude = metadata.Extent.SouthLatitude,
                MaxLatitude = metadata.Extent.NorthLatitude,
                MinLongitude = metadata.Extent.WestLongitude,
                MaxLongitude = metadata.Extent.EastLongitude,
                WidthPixels = metadata.GridMetadata.NumColumns,
                HeightPixels = metadata.GridMetadata.NumRows,
                ScaleDenominator = 50_000,
            };

            var pipeline = _pipeline;
            var layer = await pipeline.ProcessAsync(_source, _catalogue, context?.Mariner ?? MarinerSettings.Default, cancellationToken)
                .ConfigureAwait(false);
            var styledLayer = (StyledCoverageLayer)layer;

            int crs = _dataset.HorizontalCRS ?? 4326;
            var geoId = _dataset.GeographicIdentifier ?? _fileName;
            var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}";

            return new CoveragePortrayalResult
            {
                // S-102 → S98DisplayPlane.Bathymetry. S-98 Annex A §A-6.9.1
                // ("gridded bathymetry replaces depth area and depth
                // contours"). S-102 always emits a single coverage layer;
                // PR-L1 leaves WithinPlanePriority at 0.
                SubLayers = new CoverageSubLayerBase[]
                {
                    new GridCoverageSubLayer
                    {
                        LayerKey = "s102.surface",
                        LayerName = $"S-102: {_fileName}",
                        Plane = S98DisplayPlane.Bathymetry,
                        WithinPlanePriority = 0,
                        Coverage = styledLayer,
                        Viewport = viewport,
                    },
                },
                Spec = new SpecRef("S-102", default),
                SourceDatasetId = _fileName,
                Info = info,
            };
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public FeatureInfo? GetFeatureInfo(string featureRef) => null;

    /// <summary>
    /// Renders the bathymetric surface to a standalone <see cref="SKBitmap"/>
    /// through the headless, Mapsui-free Skia coverage core
    /// (<see cref="CoverageHeadlessRenderer"/> → <see cref="SkiaCoverageRenderer"/>).
    /// The depth-band colour fill is fitted to the requested pixel rectangle,
    /// preserving aspect against <paramref name="background"/>.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, mariner settings).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
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

        await _catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        var styledLayer = (StyledCoverageLayer)await _pipeline
            .ProcessAsync(_source, _catalogue, context?.Mariner ?? MarinerSettings.Default, cancellationToken)
            .ConfigureAwait(false);

        var extent = _source.Metadata.Extent;

        // The grid extent is expressed in the dataset's native CRS, which for
        // S-102 may be a projected UTM zone (EPSG:326xx/327xx) — typical of
        // Edition 2.1 cells. CoverageHeadlessRenderer expects WGS84 lon/lat
        // (it projects through Web Mercator), so reproject the native extent
        // corners to WGS84 first; the transform is identity for geographic
        // (EPSG:4326) grids. ProjNet uses lon-first/lat-second ordering, matching
        // CoveragePickHelper. (See issue #239.)
        int crs = _dataset.HorizontalCRS ?? 4326;
        var nativeToWgs84 = _crsTransformFactory.Create($"EPSG:{crs}", "EPSG:4326");
        var (west, south) = nativeToWgs84.IsIdentity
            ? (extent.WestLongitude, extent.SouthLatitude)
            : nativeToWgs84.Transform(extent.WestLongitude, extent.SouthLatitude);
        var (east, north) = nativeToWgs84.IsIdentity
            ? (extent.EastLongitude, extent.NorthLatitude)
            : nativeToWgs84.Transform(extent.EastLongitude, extent.NorthLatitude);

        var renderer = new CoverageHeadlessRenderer
        {
            Background = background ?? new RgbaColor(255, 255, 255, 255),
            NativeToWgs84 = nativeToWgs84,
        };

        return renderer.Render(
            styledLayer,
            west,
            east,
            south,
            north,
            widthPixels,
            heightPixels,
            context?.Basemap ?? BasemapKind.None);
    }

    /// <summary>
    /// Samples the bathymetric surface at the supplied geographic
    /// position. Returns a synthetic feature carrying depth and
    /// uncertainty pick attributes; the <paramref name="time"/> argument
    /// is ignored because S-102 surfaces are time-invariant.
    /// </summary>
    /// <remarks>
    /// NoData cells (S-100 Part 10c §11; S-102 sentinel
    /// <c>1_000_000f</c>) yield <c>"—"</c> for the affected attribute
    /// rather than the raw fill value. Out-of-extent clicks return
    /// <c>null</c>.
    /// </remarks>
    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time)
    {
        var sample = CoveragePickHelper.Sample(_source, _crsTransformFactory, latitude, longitude);
        if (sample is null)
            return null;

        var depth = sample.Values.TryGetValue("depth", out var d) ? d : sample.NoDataValue;
        var uncertainty = sample.Values.TryGetValue("uncertainty", out var u) ? u : sample.NoDataValue;
        var attrs = new List<PickAttribute>
        {
            new()
            {
                Code = "depth",
                Name = "Depth",
                RawValue = FormatFloat(depth, sample.NoDataValue),
                DisplayValue = depth == sample.NoDataValue ? "—" : $"{depth.ToString("0.##", CultureInfo.InvariantCulture)} m",
                // Surface the metres value so the viewer can re-format it
                // through the mariner's DepthUnit (S-100 Part 9 §4.2).
                // NoData cells keep DepthMetresValue null and remain rendered
                // as the localised em-dash placeholder.
                DepthMetresValue = depth == sample.NoDataValue ? null : (double?)depth,
            },
            new()
            {
                Code = "uncertainty",
                Name = "Uncertainty",
                RawValue = FormatFloat(uncertainty, sample.NoDataValue),
                DisplayValue = uncertainty == sample.NoDataValue ? "—" : $"{uncertainty.ToString("0.##", CultureInfo.InvariantCulture)} m",
                DepthMetresValue = uncertainty == sample.NoDataValue ? null : (double?)uncertainty,
            },
        };

        return new FeatureInfo
        {
            FeatureRef = $"({sample.Row},{sample.Col})",
            FeatureType = "BathymetryCoverage",
            FeatureTypeName = "Bathymetry Coverage",
            Attributes = attrs,
        };
    }

    private static string FormatFloat(float value, float noData)
        => value == noData
            ? "NoData"
            : value.ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs the S-102 normative rule pack
    /// (<see cref="S102DatasetRules.Default"/>) against the parsed
    /// dataset and returns the cached report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per <c>docs/design/non-gml-validation.md</c> §5.1, this override
    /// defensively wraps the rule-pack call in a try/catch for
    /// <see cref="S100DatasetSchemaException"/>. The realistic failure
    /// mode is the reader throwing inside the constructor (in which
    /// case the processor never exists and this method is never
    /// reached); the wrapper is therefore forward-compatible only,
    /// surfacing a single <c>S102-PROJ-SCHEMA</c> finding carrying
    /// the exception's <c>GroupPath</c>, <c>AttributeOrDataset</c>,
    /// and <c>SpecReference</c> per design §5.3.
    /// </para>
    /// <para>
    /// Validation is a pure function of the parsed dataset; the
    /// report is cached after the first call (mirroring the GML
    /// processors' pattern, see <c>S124DatasetProcessor</c>).
    /// </para>
    /// </remarks>
    public ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            try
            {
                _validationReport = S102DatasetRules.Default.Run(_dataset);
            }
            catch (S100DatasetSchemaException ex)
            {
                _validationReport = BuildSchemaSurrogateReport(ex);
            }
            _validationCached = true;
        }
        return _validationReport;
    }

    private static ValidationReport BuildSchemaSurrogateReport(S100DatasetSchemaException ex)
    {
        var details = new List<string>();
        details.Add($"GroupPath='{ex.GroupPath}'");
        if (!string.IsNullOrEmpty(ex.AttributeOrDataset))
            details.Add($"AttributeOrDataset='{ex.AttributeOrDataset}'");
        if (!string.IsNullOrEmpty(ex.SpecReference))
            details.Add($"SpecReference='{ex.SpecReference}'");

        var finding = new ValidationFinding
        {
            RuleId = "S102-PROJ-SCHEMA",
            Severity = ValidationSeverity.Error,
            Message = $"S102 reader raised S100DatasetSchemaException: {ex.Message} ({string.Join(", ", details)}).",
            RelatedFeatureId = ex.GroupPath,
        };

        return new ValidationReport(
            [finding],
            RulesEvaluated: 1,
            RulesWithFindings: 1);
    }
}
