using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S57;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Opens a NATO Additional Military Layers (AML) cell: an S-57 encoded
/// military overlay (<c>DSID</c> <c>PRSP</c> 51–57) that is <em>not</em> a
/// chart. The cell is read as it is, never translated to S-101, and drawn by
/// an <see cref="IAmlPortrayal"/>; with <see cref="AmlNotDrawnPortrayal"/> it is
/// "recognised, not drawn".
/// </summary>
/// <remarks>
/// <para>
/// Its features keep their AML object classes and attributes (named by the
/// cell's AML 1.0, 2.1 or 3.0 catalogue) and stay pickable whether or not
/// they're drawn. <see cref="Info"/> holds the AML facts hosts report:
/// product, version, title and protective marking.
/// </para>
/// <para>
/// AML never feeds depth answers or route checks: this processor is not an
/// <see cref="IChartDepthCell"/>, and its catalog payload
/// (<see cref="AmlDatasetData"/>) isn't the S-101 payload the route checks read.
/// </para>
/// </remarks>
public sealed class AmlDatasetProcessor : IDatasetProcessor, IVectorPortrayalSource, ILoadedDatasetProjection
{
    /// <summary>The first line of every pick report on an AML feature, and of its dataset info.</summary>
    public const string NotAChartNotice = "Additional Military Layers, not a chart.";

    private readonly S57Document _document;
    private readonly IAmlPortrayal _portrayal;
    private readonly string _fileName;
    private readonly string _contentScope;
    private readonly Lazy<CellContent> _content;
    private DatasetMetadata? _metadata;
    private volatile IReadOnlyDictionary<string, int>? _drawingPriorities;

    /// <summary>Reads the AML cell at <paramref name="path"/>.</summary>
    /// <param name="path">Path to the base cell (<c>….000</c>).</param>
    /// <param name="portrayal">Draws the cell; <see cref="AmlNotDrawnPortrayal.Instance"/> draws nothing.</param>
    /// <exception cref="ArgumentException">The file isn't an AML cell.</exception>
    public AmlDatasetProcessor(string path, IAmlPortrayal portrayal)
        : this(OpenFromFile(path), Path.GetFileName(path), portrayal)
    {
    }

    /// <summary>Reads the AML cell <paramref name="relativePath"/> from <paramref name="source"/>.</summary>
    /// <param name="source">The asset source (exchange-set folder or ZIP).</param>
    /// <param name="relativePath">The cell's source-relative path.</param>
    /// <param name="portrayal">Draws the cell; <see cref="AmlNotDrawnPortrayal.Instance"/> draws nothing.</param>
    /// <exception cref="ArgumentException">The file isn't an AML cell.</exception>
    public AmlDatasetProcessor(IAssetSource source, string relativePath, IAmlPortrayal portrayal)
        : this(OpenFromSource(source, relativePath, []), AssetSourceHelpers.GetFileName(relativePath), portrayal)
    {
    }

    /// <summary>
    /// Reads the AML base cell <paramref name="baseRelativePath"/> from
    /// <paramref name="source"/> and applies its sequential updates.
    /// </summary>
    /// <param name="source">The asset source (exchange-set folder or ZIP).</param>
    /// <param name="baseRelativePath">The base cell's source-relative path.</param>
    /// <param name="updateRelativePaths">The update files' source-relative paths, in update-number order.</param>
    /// <param name="portrayal">Draws the cell; <see cref="AmlNotDrawnPortrayal.Instance"/> draws nothing.</param>
    /// <exception cref="ArgumentException">The file isn't an AML cell.</exception>
    public AmlDatasetProcessor(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths,
        IAmlPortrayal portrayal)
        : this(OpenFromSource(source, baseRelativePath, updateRelativePaths), AssetSourceHelpers.GetFileName(baseRelativePath), portrayal)
    {
    }

    private AmlDatasetProcessor((S57Dataset Dataset, string ContentHash) opened, string fileName, IAmlPortrayal portrayal)
    {
        ArgumentNullException.ThrowIfNull(portrayal);
        _document = opened.Dataset.Document;
        Info = AmlDatasetInfo.FromDocument(_document, fileName)
            ?? throw new ArgumentException($"'{fileName}' isn't an AML cell (DSID PRSP 51–57).", nameof(opened));
        UpdateReport = opened.Dataset.UpdateReport;
        _portrayal = portrayal;
        _fileName = fileName;
        _contentScope = $"ds:{opened.ContentHash}|aml";
        _content = new Lazy<CellContent>(ReadContent, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The cell's AML facts: product, version, title, protective marking, scale band.</summary>
    public AmlDatasetInfo Info { get; }

    /// <summary>What happened to the cell's updates, when it was opened with them; otherwise <see langword="null"/>.</summary>
    public S57UpdateReport? UpdateReport { get; }

    /// <summary>Whether the cell is drawn: <see langword="false"/> while AML is "recognised, not drawn".</summary>
    public bool Drawn => _portrayal.Draws;

    /// <summary>
    /// The state hosts report: <c>"drawn"</c>, or <c>"recognised-not-drawn"</c>
    /// while <see cref="Drawn"/> is <see langword="false"/>.
    /// </summary>
    public string Status => Drawn ? "drawn" : "recognised-not-drawn";

    /// <summary>
    /// The cell's coverage: its <c>M_COVR</c> areas with <c>CATCOV</c> = 1, or
    /// its bounding box when it has none (<see cref="CoverageIsBoundingBox"/>).
    /// </summary>
    public IReadOnlyList<CoverageArea> Coverage => _content.Value.Coverage;

    /// <summary>Whether <see cref="Coverage"/> is the bounding-box fallback.</summary>
    public bool CoverageIsBoundingBox => _content.Value.CoverageIsBoundingBox;

    /// <summary>The cell's features, as read by <see cref="S57ChartFeatures.Read"/>.</summary>
    public IReadOnlyList<Feature> Features => _content.Value.Features;

    /// <inheritdoc />
    /// <remarks><c>AML</c> with the cell's product specification edition (<c>1.0</c>, <c>3.0</c>).</remarks>
    public SpecRef Spec => SpecOf(Info);

    /// <inheritdoc />
    /// <remarks>
    /// The extent of <see cref="Coverage"/>. There's no display-scale window
    /// yet, so the cell's outline shows at every scale; the AML scale bands
    /// (<see cref="AmlScaleBands"/>) are for the portrayal.
    /// </remarks>
    public DatasetMetadata Metadata => _metadata ??= new DatasetMetadata
    {
        Spec = Spec,
        Extent = ExtentOf(Coverage),
        HorizontalCrsEpsg = null,
    };

    /// <summary>The cell's features with the catalogue that names them, for Object Information.</summary>
    public AmlCellFeatures Cell => _content.Value.Cell;

    /// <summary>
    /// Whether the feature <paramref name="featureRef"/> was drawn by the
    /// last portrayal: <see langword="false"/> for every feature while the
    /// cell is "recognised, not drawn", and for the features a drawing
    /// portrayal leaves out.
    /// </summary>
    /// <param name="featureRef">The feature reference (the decimal record id).</param>
    public bool IsFeatureDrawn(string featureRef) => DrawingPriority(featureRef) is not null;

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() => new AmlDatasetData(Info, Metadata)
    {
        Cell = Cell,
        DrawingPriority = DrawingPriority,
    };

    /// <summary>
    /// Reads an AML cell's catalog payload from <paramref name="stream"/>
    /// without a processor.
    /// </summary>
    /// <param name="stream">The base cell's bytes.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentException">The stream isn't an AML cell.</exception>
    public static AmlDatasetData ReadLoadedData(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = S57Dataset.Open(stream).Document;
        var info = AmlDatasetInfo.FromDocument(document)
            ?? throw new ArgumentException("The dataset isn't an AML cell (DSID PRSP 51–57).", nameof(stream));
        return CreateLoadedData(info, document);
    }

    /// <summary>Builds the catalog payload of an AML cell already read.</summary>
    /// <param name="info">The cell's AML facts.</param>
    /// <param name="document">The parsed cell.</param>
    /// <returns>The payload.</returns>
    public static AmlDatasetData CreateLoadedData(AmlDatasetInfo info, S57Document document)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(document);
        var catalogue = document.GetObjectCatalogue();
        var features = S57ChartFeatures.Read(S57Chart.FromDocument(document), catalogue);
        var coverage = S57ChartFeatures.ResolveCoverage(features, out _);
        return new AmlDatasetData(info, new DatasetMetadata
        {
            Spec = SpecOf(info),
            Extent = ExtentOf(coverage),
        })
        {
            Cell = new AmlCellFeatures(info, catalogue, features),
        };
    }

    private static SpecRef SpecOf(AmlDatasetInfo info) => new(AmlDatasetInfo.SpecName,
        SpecVersion.TryParse(info.Version, out var version) ? version : default);

    /// <inheritdoc />
    /// <remarks>Records which features the result draws, for <see cref="IsFeatureDrawn"/>.</remarks>
    public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        var content = _content.Value;
        var result = await _portrayal.PortrayAsync(
            new AmlPortrayalInput
            {
                Info = Info,
                Document = _document,
                Catalogue = content.Catalogue,
                Features = content.Features,
                Coverage = content.Coverage,
                Spec = Spec,
                FileName = _fileName,
                ContentScope = _contentScope,
            },
            context,
            cancellationToken).ConfigureAwait(false);
        _drawingPriorities = S101DatasetProcessor.DrawingPriorities(result.SubLayers.SelectMany(l => l.Instructions));
        return result;
    }

    // The feature's drawing priority in the last portrayal, or null when it
    // isn't drawn (or the cell hasn't been portrayed).
    private int? DrawingPriority(string featureRef) =>
        _drawingPriorities is { } priorities && priorities.TryGetValue(featureRef, out var priority) ? priority : null;

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfo(string featureRef) =>
        Cell.Find(featureRef) is { } feature ? BuildFeatureInfo(feature) : null;

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        var features = _content.Value.Features;
        return ordinal >= 0 && ordinal < features.Count ? BuildFeatureInfo(features[ordinal]) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every feature with geometry is pickable, drawn or not, except the meta
    /// features (<see cref="AmlCellFeatures.PickableFeatures"/>).
    /// </remarks>
    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(double latitude, double longitude, double radiusMeters) =>
        FeatureHitTester.HitTest(Cell.PickableFeatures, latitude, longitude, radiusMeters);

    /// <inheritdoc />
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        var content = _content.Value;
        var ordinal = 0;
        foreach (var feature in content.Features)
        {
            yield return new FeatureSummary
            {
                FeatureRef = feature.Id.ToString(CultureInfo.InvariantCulture),
                Ordinal = ordinal++,
                FeatureType = feature.FeatureType,
                FeatureTypeName = content.ClassName(feature.FeatureType),
            };
        }
    }

    private FeatureInfo BuildFeatureInfo(Feature feature) =>
        Cell.BuildFeatureInfo(feature, DrawingPriority(feature.Id.ToString(CultureInfo.InvariantCulture)));

    private CellContent ReadContent()
    {
        var chart = S57Chart.FromDocument(_document);
        var catalogue = _document.GetObjectCatalogue();
        var features = S57ChartFeatures.Read(chart, catalogue);
        var coverage = S57ChartFeatures.ResolveCoverage(features, out var isBoundingBox);
        return new CellContent(new AmlCellFeatures(Info, catalogue, features), coverage, isBoundingBox);
    }

    private static BoundingBox? ExtentOf(IReadOnlyList<CoverageArea> coverage)
    {
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        var any = false;
        foreach (var area in coverage)
        {
            foreach (var p in area.ExteriorRing)
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
                any = true;
            }
        }

        return any ? new BoundingBox(south, west, north, east) : null;
    }

    private static (S57Dataset, string) OpenFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Stream stream = File.OpenRead(path);
        var contentHash = DatasetContentHash.HashAndRewind(ref stream);
        using (stream)
            return (S57Dataset.Open(stream), contentHash);
    }

    private static (S57Dataset, string) OpenFromSource(IAssetSource source, string baseRelativePath, IReadOnlyList<string> updateRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(baseRelativePath);
        ArgumentNullException.ThrowIfNull(updateRelativePaths);

        var updateStreams = new List<Stream>(updateRelativePaths.Count);
        var baseStream = AssetSourceHelpers.OpenSeekable(source, baseRelativePath);
        try
        {
            foreach (var updatePath in updateRelativePaths)
                updateStreams.Add(AssetSourceHelpers.OpenSeekable(source, updatePath));

            var contentHash = DatasetContentHash.HashAndRewind([baseStream, .. updateStreams]);
            return (updateStreams.Count == 0 ? S57Dataset.Open(baseStream) : S57Dataset.Open(baseStream, updateStreams), contentHash);
        }
        finally
        {
            baseStream.Dispose();
            foreach (var stream in updateStreams)
                stream.Dispose();
        }
    }

    private sealed class CellContent(
        AmlCellFeatures cell,
        IReadOnlyList<CoverageArea> coverage,
        bool coverageIsBoundingBox)
    {
        public AmlCellFeatures Cell { get; } = cell;

        public S57ObjectCatalogue Catalogue => Cell.Catalogue;

        public IReadOnlyList<Feature> Features => Cell.Features;

        public IReadOnlyList<CoverageArea> Coverage { get; } = coverage;

        public bool CoverageIsBoundingBox { get; } = coverageIsBoundingBox;

        public string? ClassName(string acronym) => Cell.ClassName(acronym);
    }
}
