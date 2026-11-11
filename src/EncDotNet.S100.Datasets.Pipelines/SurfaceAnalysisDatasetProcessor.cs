using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for the WPC coded surface bulletins (design note
/// <c>docs/design/vector-weather.md</c> §5, §8.3): the coded surface analysis
/// (<c>CODSUS</c>, standard or high resolution) and the coded forecast
/// bulletins, read by <see cref="CodedSurfaceBulletinReader"/> into
/// <see cref="SurfaceAnalysis"/> models. Not an S-100 product and not for
/// navigation.
/// </summary>
/// <remarks>
/// <para>What it provides:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Features.</b> Every front, trough, high and low of every analysis in the
/// bulletin (<see cref="EnumerateFeatures"/>). A front's id is its code and
/// its number among the fronts of that kind, as the bulletin lists them
/// (<c>COLD 3</c>); a centre's is <c>HIGH 2</c> or <c>LOW 5</c>. Features of
/// a forecast after the first analysis are prefixed with its lead time
/// (<c>+24h COLD 1</c>).
/// </description></item>
/// <item><description>
/// <b>Drawing.</b> The analysis at the context's
/// <see cref="RenderContext.TimeStep"/> (the first without one), with
/// <see cref="SurfaceAnalysisStyle"/>: one sub-layer,
/// <see cref="SurfaceAnalysisStyle.LayerKey"/>, on the Other chart overlays
/// plane at priority 92, above the isobars (90), with its legend. Fronts are
/// smoothed for drawing (<see cref="SurfaceAnalysisStyle.Smooth"/>) and are
/// picked on the line drawn.
/// </description></item>
/// <item><description>
/// <b>Time (V30, #1301).</b> Each analysis or forecast is a time step
/// (<see cref="AvailableTimes"/>), drawn at or before the view time for
/// <see cref="DrawWindow"/>, and stale after <see cref="StaleAfter"/>: drawn at
/// half opacity, with the pressure centres saying their age
/// (<see cref="RenderContext.TimeStepAge"/>). Every coded surface bulletin
/// shares one <see cref="TimeSemantics.Supersession"/> key, so of the loaded
/// bulletins the one valid nearest before the view time draws (an analysis
/// before a forecast valid at the same time).
/// </description></item>
/// <item><description>
/// <b>Picking (V24).</b> The notice, the analysis time, the time of the next
/// analysis, the intensity when the bulletin gives one, and the bulletin's
/// own points (<see cref="GetFeatureInfoAt"/>). Only the drawn analysis is
/// hit.
/// </description></item>
/// </list>
/// <para>
/// The file is read whole at construction, which fails with a
/// <see cref="CodedSurfaceBulletinFormatException"/> naming the file when it
/// can't be decoded.
/// </para>
/// </remarks>
public sealed class SurfaceAnalysisDatasetProcessor :
    IDatasetProcessor,
    ITimeAwareDatasetProcessor,
    IVectorPortrayalSource,
    IHeadlessImageRenderer,
    ILoadedDatasetProjection
{
    /// <summary>The spec name the coded surface bulletins are registered under.</summary>
    public const string SpecName = Core.SpecName.Codsus;

    /// <summary>The feature type of every front and trough.</summary>
    public const string FrontFeatureType = "SurfaceFront";

    /// <summary>The feature type of every high and low.</summary>
    public const string PressureCentreFeatureType = "PressureCentre";

    /// <summary>The first line of every pick report on a front or pressure centre (V24).</summary>
    public const string NotForNavigationNotice = "Weather analysis, not for navigation.";

    /// <summary>
    /// How often WPC issues a surface analysis: every 3 hours, so the next
    /// analysis is due 3 hours after the one drawn.
    /// </summary>
    public static TimeSpan AnalysisInterval { get; } = TimeSpan.FromHours(3);

    /// <summary>
    /// How long after its valid time an analysis or forecast stays current: 6
    /// hours, two missed analyses (V28). Older, it is drawn as stale.
    /// </summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromHours(6);

    /// <summary>
    /// How long after its valid time an analysis or forecast is drawn at all:
    /// 12 hours, stale for its second half. Beyond it nothing is drawn and the
    /// host says there is no analysis at the view time.
    /// </summary>
    public static TimeSpan DrawWindow { get; } = TimeSpan.FromHours(12);

    /// <summary>The <see cref="TimeSemantics.Supersession"/> key every coded surface bulletin shares: <c>"surface-analysis"</c>.</summary>
    public const string SupersessionKey = "surface-analysis";

    /// <summary>The model a forecast bulletin's <see cref="ForecastRun"/> names: <c>"WPC"</c>.</summary>
    public const string ForecastModel = "WPC";

    /// <summary>The spec every coded surface bulletin reports: <c>CODSUS</c>, with no edition (the format has none).</summary>
    public static SpecRef CodsusSpec { get; } = new(SpecName, default);

    private readonly SurfaceAnalysisFeature[] _features;
    private readonly SurfaceAnalysisFeature[][] _byAnalysis;
    private readonly DatasetMetadata _metadata;
    private readonly string? _contentHash;
    private volatile Drawn _current;

    /// <summary>
    /// Reads the bulletin at <paramref name="path"/>. A bulletin without an
    /// issuance line is dated from the file's last-write time.
    /// </summary>
    /// <param name="path">The file's path.</param>
    public SurfaceAnalysisDatasetProcessor(string path)
        : this(ReadFile(path), Path.GetFileName(path))
    {
    }

    private SurfaceAnalysisDatasetProcessor((CodedSurfaceBulletin Bulletin, string Hash) read, string fileName)
        : this(read.Bulletin, fileName)
    {
        _contentHash = read.Hash;
    }

    /// <summary>
    /// Reads the bulletin <paramref name="relativePath"/> from
    /// <paramref name="source"/>. A bulletin without an issuance line can't be
    /// dated from an asset source, so it must have one.
    /// </summary>
    /// <param name="source">The asset source holding the file.</param>
    /// <param name="relativePath">The file's source-relative path.</param>
    public SurfaceAnalysisDatasetProcessor(IAssetSource source, string relativePath)
        : this(ReadSource(source, relativePath), AssetSourceHelpers.GetFileName(relativePath))
    {
    }

    /// <summary>Wraps a bulletin already read.</summary>
    /// <param name="bulletin">The bulletin.</param>
    /// <param name="fileName">The file's name, the dataset's id.</param>
    public SurfaceAnalysisDatasetProcessor(CodedSurfaceBulletin bulletin, string fileName)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Bulletin = bulletin;
        FileName = fileName;
        _features = BuildFeatures(bulletin);
        _metadata = BuildMetadata(bulletin);
        _byAnalysis = [.. bulletin.Analyses.Select(a => _features.Where(f => ReferenceEquals(f.Analysis, a)).ToArray())];
        AvailableTimes = [.. bulletin.Analyses.Select(a => a.ValidTime.UtcDateTime).Distinct().Order()];
        TimeSemantics = new TimeSemantics(
            bulletin.Kind == CodedSurfaceBulletinKind.Forecast ? TimeStepKind.Forecast : TimeStepKind.Analysis,
            TimeStepSelection.AtOrBefore,
            DrawWindow,
            StaleAfter,
            SupersessionKey);
        _current = Draw(bulletin.Analyses.Count > 0 ? 0 : -1, age: null);
    }

    /// <inheritdoc />
    public SpecRef Spec => CodsusSpec;

    /// <summary>The dataset's file name.</summary>
    public string FileName { get; }

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() => new SurfaceAnalysisDatasetData(Bulletin, _metadata);

    /// <summary>The bulletin, with its analyses.</summary>
    public CodedSurfaceBulletin Bulletin { get; }

    /// <summary>
    /// The analysis last drawn, and hit: the one at the last portrayal's time
    /// step, the bulletin's first before any (the analysis, or a forecast
    /// bulletin's earliest forecast); <see langword="null"/> for a bulletin
    /// with none.
    /// </summary>
    public SurfaceAnalysis? DrawnAnalysis => _current.Analysis;

    /// <summary>How old <see cref="DrawnAnalysis"/> was when last drawn, once stale; null while current.</summary>
    public TimeSpan? DrawnAge => _current.Age;

    /// <inheritdoc />
    /// <remarks>The valid time of every analysis or forecast in the bulletin.</remarks>
    public IReadOnlyList<DateTime> AvailableTimes { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Analysis steps for the analysis, forecast steps for a forecast
    /// bulletin; at or before the view time within <see cref="DrawWindow"/>,
    /// stale after <see cref="StaleAfter"/>, sharing <see cref="SupersessionKey"/>.
    /// </remarks>
    public TimeSemantics TimeSemantics { get; }

    /// <inheritdoc />
    /// <remarks>When the bulletin was issued, from its issuance line.</remarks>
    public DateTime? IssueTime => Bulletin.Header.IssuedAt?.UtcDateTime;

    /// <inheritdoc />
    /// <remarks>
    /// For a forecast bulletin, WPC's, from the time its lead times count from
    /// (the first forecast's valid time less its lead time); null for an analysis.
    /// </remarks>
    public DatasetForecastRun? ForecastRun =>
        Bulletin.Kind == CodedSurfaceBulletinKind.Forecast
            ? new DatasetForecastRun(
                ForecastModel,
                Bulletin.Analyses.FirstOrDefault(a => a.ForecastHours is not null) is { } first
                    ? first.ValidTime.UtcDateTime.AddHours(-first.ForecastHours!.Value)
                    : null)
            : null;

    /// <summary>
    /// The analysis drawn at <paramref name="timeStep"/>: the latest valid at
    /// or before it (a host passes a valid time); the first for
    /// <see langword="null"/>; null when none is.
    /// </summary>
    /// <param name="timeStep">The time step, UTC.</param>
    public SurfaceAnalysis? AnalysisAt(DateTime? timeStep) => IndexAt(timeStep) is var i and >= 0 ? Bulletin.Analyses[i] : null;

    /// <inheritdoc />
    /// <remarks>WPC's domain as the extent (V36), WGS-84, and the first to the last valid time.</remarks>
    public DatasetMetadata Metadata => _metadata;

    /// <summary>Reads the metadata of the bulletin at <paramref name="path"/>: its spec, WPC's domain and its valid times.</summary>
    /// <param name="path">The file's path.</param>
    public static DatasetMetadata ReadMetadata(string path) => BuildMetadata(ReadFile(path).Bulletin);

    /// <summary>Builds the dataset metadata of a bulletin.</summary>
    /// <param name="bulletin">The bulletin.</param>
    public static DatasetMetadata BuildMetadata(CodedSurfaceBulletin bulletin)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        var times = bulletin.Analyses.Select(a => a.ValidTime.UtcDateTime).ToArray();
        return new DatasetMetadata
        {
            Spec = CodsusSpec,
            Extent = bulletin.Domain,
            HorizontalCrsEpsg = 4326,
            TimeCoverage = times.Length == 0 ? null : new TimeCoverage(times.Min(), times.Max()),
        };
    }

    /// <inheritdoc />
    public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var palette = context?.Palette ?? PaletteType.Day;
        var drawn = Select(context);
        var subLayer = new VectorSubLayer
        {
            LayerKey = SurfaceAnalysisStyle.LayerKey,
            LayerName = $"Surface analysis: {FileName}",
            Instructions = drawn.Instructions,
            Opacity = drawn.Age is null ? 1.0 : SurfaceAnalysisStyle.StaleOpacity,
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = SurfaceAnalysisStyle.WithinPlanePriority,
            SourceFeatureType = SurfaceAnalysisStyle.LayerKey,
            Legend = SurfaceAnalysisStyle.BuildLegend(palette),
        };

        var info = drawn.Analysis is { } analysis
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Surface analysis {FileName}: {analysis.Fronts.Count} fronts and troughs, {analysis.PressureCentres.Count} pressure centres, valid {analysis.ValidTime:yyyy-MM-dd HH:mm}Z{(drawn.Age is { } age ? $", {SurfaceAnalysisStyle.FormatAge(age)} (stale)" : string.Empty)}. {NotForNavigationNotice}")
            : $"Surface analysis {FileName}: no analysis. {NotForNavigationNotice}";

        return Task.FromResult(new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = WeatherPalette.Palette.GetColorPalette(palette),
            GeometryProvider = new FeatureGeometryProvider<SurfaceAnalysisFeature>(drawn.Features),
            Product = SpecName,
            Spec = Spec,
            SourceDatasetId = FileName,
            ContentScope = _contentHash is null
                ? null
                : string.Create(CultureInfo.InvariantCulture, $"ds:{_contentHash}|{SurfaceAnalysisStyle.ContentScope}|a{drawn.Index}|age{drawn.Age?.TotalHours ?? -1}"),
            Info = info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = WeatherCoverageStyle.Symbols.Provider,
            LineStyleProvider = SurfaceAnalysisStyle.LineStyles.Provider,
            LayerNames = [SurfaceAnalysisStyle.LayerKey],
            GeographicExtent = DrawnExtent(drawn.Features),
            PreferLiveDrawing = true,
        });
    }

    /// <summary>
    /// Renders the analysis at the context's time step (as
    /// <see cref="BuildVectorPortrayalAsync"/> picks it) to a bitmap, framed on
    /// everything drawn (or on <see cref="RenderContext.Viewport"/>). A stale
    /// analysis is drawn with its age but at full opacity.
    /// </summary>
    /// <param name="widthPixels">Output width in pixels.</param>
    /// <param name="heightPixels">Output height in pixels.</param>
    /// <param name="context">Palette, scales and viewport.</param>
    /// <param name="background">Background fill; opaque white by default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new bitmap owned by the caller.</returns>
    public Task<SKBitmap> RenderHeadlessAsync(
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
        var drawn = Select(context);
        var viewport = context?.Viewport;
        if (viewport is null && drawn.Features.Length > 0)
        {
            var extent = DrawnExtent(drawn.Features);
            var (minX, minY) = WebMercator.FromLonLat(extent.MinLongitude, extent.MinLatitude);
            var (maxX, maxY) = WebMercator.FromLonLat(extent.MaxLongitude, extent.MaxLatitude);
            viewport = PointGlyphHeadlessAdapter.FitViewport(
                new MercatorBounds(minX, minY, maxX, maxY), widthPixels, heightPixels, padding: 0.05);
        }

        if (viewport is null)
            return Task.FromResult(HeadlessVectorRenderer.RenderBlank(widthPixels, heightPixels, bg));

        var palette = context?.Palette ?? PaletteType.Day;
        return Task.FromResult(HeadlessVectorRenderer.Render(
            drawn.Instructions,
            new FeatureGeometryProvider<SurfaceAnalysisFeature>(drawn.Features),
            WeatherPalette.Palette.GetColorPalette(palette),
            symbolProvider: WeatherCoverageStyle.Symbols.Provider,
            lineStyleProvider: SurfaceAnalysisStyle.LineStyles.Provider,
            symbolScale: context?.SymbolScale ?? 1.0,
            textScale: context?.TextScale ?? 1.0,
            widthPixels: widthPixels,
            heightPixels: heightPixels,
            background: bg,
            hiddenCategories: context?.HiddenInstructionCategories ?? DrawingInstructionCategory.None,
            basemap: context?.Basemap ?? BasemapKind.None,
            viewport: viewport));
    }

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);
        var index = Array.FindIndex(_features, f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : GetFeatureInfoAt(index);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The attributes (V24), in order: the notice; for an analysis its time
    /// (<c>analysis</c>) and the time the next is due (<c>nextAnalysis</c>,
    /// <see cref="AnalysisInterval"/> later), or for a forecast its valid time
    /// (<c>validTime</c>) and lead time (<c>forecastHours</c>); then for a
    /// front its intensity when the bulletin gives one (<c>strength</c>) and
    /// its points (<c>points</c>: shown as the count and the two ends, the raw
    /// value every point in the bulletin's order), or for a pressure centre
    /// its pressure and position.
    /// </remarks>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _features.Length)
            return null;
        var feature = _features[ordinal];
        var attributes = Describe(feature);
        if (_current is { Age: { } age } current && ReferenceEquals(feature.Analysis, current.Analysis))
            attributes.Insert(1, PickAttribute.StaleNotice(StaleText(feature.Analysis, age)));
        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = feature.Name,
            Attributes = attributes,
        };
    }

    /// <inheritdoc />
    public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _features.Length)
            return null;
        var feature = _features[ordinal];
        return feature.Centre is not null
            ? new S100FeatureGeometry { Primitive = S100GeometryType.Point, Points = feature.Points }
            : new S100FeatureGeometry { Primitive = S100GeometryType.Curve, Curves = feature.Curves };
    }

    /// <inheritdoc />
    /// <remarks>Only the features of <see cref="DrawnAnalysis"/>, on the lines as drawn (smoothed).</remarks>
    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(double latitude, double longitude, double radiusMeters) =>
        FeatureHitTester.HitTest(_current.Features, latitude, longitude, radiusMeters);

    /// <inheritdoc />
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        for (var i = 0; i < _features.Length; i++)
        {
            yield return new FeatureSummary
            {
                FeatureRef = _features[i].Id,
                Ordinal = i,
                FeatureType = _features[i].FeatureType,
                FeatureTypeName = _features[i].Name,
            };
        }
    }

    /// <summary>
    /// The analysis of <paramref name="bulletin"/> drawn at
    /// <paramref name="timeStep"/>: the latest valid at or before it within
    /// <see cref="DrawWindow"/>, or its first for <see langword="null"/>;
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="bulletin">The bulletin.</param>
    /// <param name="timeStep">The time step (UTC), or null for none.</param>
    public static SurfaceAnalysis? DrawnAnalysisOf(CodedSurfaceBulletin bulletin, DateTime? timeStep)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        var index = IndexAt(bulletin, timeStep);
        return index >= 0 ? bulletin.Analyses[index] : null;
    }

    /// <summary>
    /// The features of the analysis drawn at <paramref name="timeStep"/>, as
    /// the processor draws and hits them: for the catalog's identify and pick
    /// (<see cref="Spec.FeatureAccessor"/>).
    /// </summary>
    internal static IEnumerable<SurfaceAnalysisFeature> DrawnFeatures(CodedSurfaceBulletin bulletin, DateTime? timeStep)
    {
        var drawn = DrawnAnalysisOf(bulletin, timeStep);
        return BuildFeatures(bulletin).Where(f => ReferenceEquals(f.Analysis, drawn));
    }

    /// <summary>
    /// "This analysis is 9 h old. A newer one is likely." (or "This forecast
    /// …"), for the pick report's stale line. It names no time, so a host shows
    /// it as it is whatever its time zone; the analysis time is its own row.
    /// </summary>
    /// <param name="analysis">The stale analysis or forecast.</param>
    /// <param name="age">Its age at the view time.</param>
    public static string StaleText(SurfaceAnalysis analysis, TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"This {(analysis.ForecastHours is null ? "analysis" : "forecast")} is {SurfaceAnalysisStyle.FormatAge(age)}. A newer one is likely.");
    }

    private int IndexAt(DateTime? timeStep) => IndexAt(Bulletin, timeStep);

    private static int IndexAt(CodedSurfaceBulletin bulletin, DateTime? timeStep)
    {
        if (bulletin.Analyses.Count == 0)
            return -1;
        if (timeStep is not { } t)
            return 0;
        var utc = t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;
        var best = -1;
        for (var i = 0; i < bulletin.Analyses.Count; i++)
        {
            var valid = bulletin.Analyses[i].ValidTime.UtcDateTime;
            if (valid <= utc && utc - valid <= DrawWindow && (best < 0 || valid > bulletin.Analyses[best].ValidTime.UtcDateTime))
                best = i;
        }

        return best;
    }

    // What the last portrayal drew: picked by the context's time step, with
    // the age the host passes once stale.
    private Drawn Select(RenderContext? context)
    {
        var index = IndexAt(context?.TimeStep);
        var age = index >= 0 ? context?.TimeStepAge : null;
        var current = _current;
        if (current.Index == index && current.Age == age)
            return current;
        var drawn = Draw(index, age);
        _current = drawn;
        return drawn;
    }

    private Drawn Draw(int index, TimeSpan? age)
    {
        var features = index >= 0 ? _byAnalysis[index] : [];
        return new Drawn(index, index >= 0 ? Bulletin.Analyses[index] : null, age, features, BuildInstructions(features, age));
    }

    private sealed record Drawn(
        int Index,
        SurfaceAnalysis? Analysis,
        TimeSpan? Age,
        SurfaceAnalysisFeature[] Features,
        IReadOnlyList<DrawingInstruction> Instructions);

    /// <summary>A front kind's name, for example "Cold front" or "Trough".</summary>
    /// <param name="type">The kind of front.</param>
    public static string NameOf(SurfaceFrontType type) => type switch
    {
        SurfaceFrontType.Cold => "Cold front",
        SurfaceFrontType.Warm => "Warm front",
        SurfaceFrontType.Occluded => "Occluded front",
        SurfaceFrontType.Stationary => "Stationary front",
        SurfaceFrontType.Trough => "Trough",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static (CodedSurfaceBulletin Bulletin, string Hash) ReadFile(string path)
    {
        try
        {
            Stream stream = File.OpenRead(path);
            try
            {
                var hash = DatasetContentHash.HashAndRewind(ref stream);
                return (CodedSurfaceBulletinReader.Read(stream, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)), hash);
            }
            finally
            {
                stream.Dispose();
            }
        }
        catch (CodedSurfaceBulletinFormatException ex)
        {
            throw new CodedSurfaceBulletinFormatException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    private static (CodedSurfaceBulletin Bulletin, string Hash) ReadSource(IAssetSource source, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(source);
        var stream = AssetSourceHelpers.OpenSeekable(source, relativePath);
        try
        {
            var hash = DatasetContentHash.HashAndRewind(ref stream);
            return (CodedSurfaceBulletinReader.Read(stream, referenceTime: null), hash);
        }
        catch (CodedSurfaceBulletinFormatException ex)
        {
            throw new CodedSurfaceBulletinFormatException($"{AssetSourceHelpers.GetFileName(relativePath)}: {ex.Message}", ex);
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static SurfaceAnalysisFeature[] BuildFeatures(CodedSurfaceBulletin bulletin)
    {
        var features = new List<SurfaceAnalysisFeature>();
        for (int a = 0; a < bulletin.Analyses.Count; a++)
        {
            var analysis = bulletin.Analyses[a];
            string prefix = a == 0
                ? string.Empty
                : analysis.ForecastHours is { } hours
                    ? string.Create(CultureInfo.InvariantCulture, $"+{hours}h ")
                    : string.Create(CultureInfo.InvariantCulture, $"#{a + 1} ");

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            string NextId(string code)
            {
                int n = counts.GetValueOrDefault(code) + 1;
                counts[code] = n;
                return string.Create(CultureInfo.InvariantCulture, $"{prefix}{code} {n}");
            }

            foreach (var front in analysis.Fronts)
                features.Add(SurfaceAnalysisFeature.ForFront(NextId(front.Code), analysis, front));

            foreach (var centre in analysis.PressureCentres)
                features.Add(SurfaceAnalysisFeature.ForCentre(NextId(centre.Kind == PressureCentreKind.High ? "HIGH" : "LOW"), analysis, centre));
        }

        return [.. features];
    }

    private static IReadOnlyList<DrawingInstruction> BuildInstructions(IEnumerable<SurfaceAnalysisFeature> features, TimeSpan? age)
    {
        var instructions = new List<DrawingInstruction>();
        foreach (var feature in features)
        {
            if (feature.Front is { } front)
            {
                if (feature.Curves.Count > 0 && feature.Curves[0].Count >= 2)
                    instructions.AddRange(SurfaceAnalysisStyle.BuildFront(feature.Id, front.Type));
            }
            else if (feature.Centre is { } centre)
            {
                instructions.AddRange(SurfaceAnalysisStyle.BuildCentre(feature.Id, centre, age));
            }
        }

        return instructions;
    }

    internal static List<PickAttribute> Describe(SurfaceAnalysisFeature feature)
    {
        var analysis = feature.Analysis;
        var attributes = new List<PickAttribute> { PickAttribute.NavigationNotice(NotForNavigationNotice) };
        if (analysis.ForecastHours is { } hours)
        {
            attributes.Add(Time("validTime", "Valid", analysis.ValidTime));
            attributes.Add(Text("forecastHours", "Forecast", string.Create(CultureInfo.InvariantCulture, $"{hours} h")));
        }
        else
        {
            attributes.Add(Time("analysis", "Analysis", analysis.ValidTime));
            attributes.Add(Time("nextAnalysis", "Next analysis", analysis.ValidTime + AnalysisInterval));
        }

        if (feature.Front is { } front)
        {
            if (front.Strength != SurfaceFrontStrength.Unspecified)
                attributes.Add(Text("strength", "Strength", front.Strength.ToString()));
            var points = front.Points;
            attributes.Add(new PickAttribute
            {
                Code = "points",
                Name = "Points",
                RawValue = string.Join(", ", points.Select(FormatPosition)),
                DisplayValue = points.Count == 0
                    ? "0"
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"{points.Count}, from {FormatPosition(points[0])} to {FormatPosition(points[^1])}"),
            });
        }
        else if (feature.Centre is { } centre)
        {
            attributes.Add(Text("pressure", "Pressure", string.Create(CultureInfo.InvariantCulture, $"{centre.PressureHectopascals} hPa")));
            attributes.Add(Text("position", "Position", FormatPosition(centre.Position)));
        }

        return attributes;
    }

    private static PickAttribute Text(string code, string name, string value) =>
        new() { Code = code, Name = name, RawValue = value };

    private static PickAttribute Time(string code, string name, DateTimeOffset time) => new()
    {
        Code = code,
        Name = name,
        RawValue = time.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture),
        DateTimeValue = time.UtcDateTime,
    };

    private static string FormatPosition(GeoPosition position) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Abs(position.Latitude):0.0}°{(position.Latitude < 0 ? 'S' : 'N')} {Math.Abs(position.Longitude):0.0}°{(position.Longitude < 0 ? 'W' : 'E')}");

    // The bounds of everything drawn, padded a little; WPC's domain when
    // nothing is.
    private GeographicBounds DrawnExtent(SurfaceAnalysisFeature[] drawn)
    {
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        foreach (var p in drawn.SelectMany(f => f.Points.Concat(f.Curves.SelectMany(c => c))))
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }

        if (south > north)
        {
            var d = Bulletin.Domain;
            return new GeographicBounds(d.WestLongitude, d.SouthLatitude, d.EastLongitude, d.NorthLatitude);
        }

        const double pad = 1.0;
        return new GeographicBounds(
            Math.Max(-180, west - pad), Math.Max(-90, south - pad), Math.Min(180, east + pad), Math.Min(90, north + pad));
    }
}

/// <summary>A front or centre as a feature: the smoothed line or the centre point, for drawing, picking and hit tests.</summary>
internal sealed class SurfaceAnalysisFeature : IS100Feature
{
    private SurfaceAnalysisFeature(string id, string featureType, string name, SurfaceAnalysis analysis, SurfaceFront? front, PressureCentre? centre)
    {
        Id = id;
        FeatureType = featureType;
        Name = name;
        Analysis = analysis;
        Front = front;
        Centre = centre;
        Points = centre is null ? [] : [centre.Position];
        Curves = front is null
            ? []
            : [SurfaceAnalysisStyle.Smooth(front.Points, SurfaceAnalysisStyle.SmoothingPasses)];
        Attributes = SurfaceAnalysisDatasetProcessor.Describe(this).ToDictionary(a => a.Code, a => a.RawValue, StringComparer.Ordinal);
    }

    public static SurfaceAnalysisFeature ForFront(string id, SurfaceAnalysis analysis, SurfaceFront front) =>
        new(id, SurfaceAnalysisDatasetProcessor.FrontFeatureType, SurfaceAnalysisDatasetProcessor.NameOf(front.Type), analysis, front, null);

    public static SurfaceAnalysisFeature ForCentre(string id, SurfaceAnalysis analysis, PressureCentre centre) =>
        new(
            id,
            SurfaceAnalysisDatasetProcessor.PressureCentreFeatureType,
            string.Create(CultureInfo.InvariantCulture, $"{(centre.Kind == PressureCentreKind.High ? "High" : "Low")} {centre.PressureHectopascals} hPa"),
            analysis,
            null,
            centre);

    public string Id { get; }

    public string FeatureType { get; }

    public string Name { get; }

    public SurfaceAnalysis Analysis { get; }

    public SurfaceFront? Front { get; }

    public PressureCentre? Centre { get; }

    public S100GeometryType GeometryType => Centre is null ? S100GeometryType.Curve : S100GeometryType.Point;

    public IReadOnlyList<GeoPosition> Points { get; }

    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; }

    public IReadOnlyList<GeoPosition> ExteriorRing => [];

    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings => [];

    public IReadOnlyDictionary<string, string> Attributes { get; }

    public IReadOnlyList<IS100ComplexAttribute> ComplexAttributes => [];
}
