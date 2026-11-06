using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.TropicalCyclones;
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
/// Pipeline processor for tropical cyclone advisories read from NHC's GIS
/// shapefile zips by <see cref="NhcAdvisoryReader"/> (design note
/// <c>docs/design/vector-weather.md</c> §8.1): the storm, the advisory, its
/// forecast points, track, cone and wind radii, and the past track. Tropical
/// cyclone advisories aren't S-100 products and aren't for navigation.
/// </summary>
/// <remarks>
/// <para>
/// The dataset is the advisory's forecast zip (<c>{ATCF}_5day_{adv}.zip</c>),
/// read with the wind radii (<c>{ATCF}_fcst_{adv}.zip</c>) and best track
/// (<c>{ATCF}_best_track.zip</c>) zips beside it when they are there, or a
/// folder holding one storm's zips, whose latest advisory is read
/// (<see cref="NhcAdvisoryFiles"/>).
/// </para>
/// <para>What it provides (issue #1296):</para>
/// <list type="bullet">
/// <item><description>
/// <b>Drawing.</b> The advisory in storm ink with <see cref="TropicalCycloneStyle"/>:
/// two sub-layers, the fills (<see cref="TropicalCycloneStyle.AreasLayerKey"/>)
/// on the On-demand surface plane and the lines, symbols and labels
/// (<see cref="TropicalCycloneStyle.TrackLayerKey"/>) on the Other chart
/// overlays plane, each with its legend (design note V1, V25). The cone and
/// the whole forecast are always drawn; the storm and its wind radii are
/// drawn at the advisory's own position (its first forecast point). Moving
/// the storm with the view time is the time slice (#1297).
/// </description></item>
/// <item><description>
/// <b>Features.</b> Each drawn part is a feature (<see cref="EnumerateFeatures"/>):
/// the storm (its id is the ATCF id, such as <c>AL092022</c>), the cone and
/// its two parts, the forecast and past tracks, every forecast and
/// best-track point, and the wind radii; the others' ids are the ATCF id and
/// the part, such as <c>AL092022 +24h</c>.
/// </description></item>
/// <item><description>
/// <b>Picking (V23).</b> Every part reports the storm: the same
/// <see cref="FeatureInfo.FeatureRef"/> (the ATCF id), class and attributes,
/// with the part that was picked as one of them, so a pick shows one storm.
/// <see cref="HitTestFeatures"/> gives one hit per storm, on the part
/// nearest the pick (points before lines before areas).
/// </description></item>
/// </list>
/// </remarks>
public sealed class TropicalCycloneDatasetProcessor :
    IDatasetProcessor,
    IVectorPortrayalSource,
    IHeadlessImageRenderer,
    ILoadedDatasetProjection
{
    /// <summary>The spec name tropical cyclone advisories are registered under.</summary>
    public const string SpecName = Core.SpecName.TropicalCyclone;

    /// <summary>The notice every report on a tropical cyclone advisory leads with (V23).</summary>
    public const string NotForNavigationNotice = "Tropical cyclone forecast, not for navigation. Follow official NHC advisories.";

    /// <summary>The feature type of every part of a storm.</summary>
    public const string FeatureType = "TropicalCyclone";

    /// <summary>
    /// The pick card's stale line (V28, §7): "Advisory 17 is 9 h old. NHC has
    /// probably issued a newer one." Time-free, so it reads the same in Local and UTC.
    /// </summary>
    /// <param name="advisory">The advisory.</param>
    /// <param name="age">How old it is.</param>
    public static string StaleText(TropicalCycloneAdvisory advisory, TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        return string.Create(CultureInfo.InvariantCulture,
            $"Advisory {advisory.Advisory} is {Math.Round(age.TotalHours):0} h old. NHC has probably issued a newer one.");
    }

    /// <summary>The paraphrase of NHC's caution about the cone, the pick card's Cone row.</summary>
    public const string ConeCaution = "The centre stays inside about two-thirds of the time. Winds reach well outside it.";

    private readonly DatasetMetadata _metadata;
    private readonly TropicalCyclonePart[] _parts;

    // How old the advisory was at the last portrayal, once stale (V28), for the
    // pick card's stale line; null while current.
    private TimeSpan? _staleAge;

    /// <summary>Reads the advisory at <paramref name="path"/>: a forecast zip or a folder of one storm's zips.</summary>
    /// <param name="path">The forecast zip or folder.</param>
    /// <exception cref="NhcFormatException">The path isn't a readable NHC advisory.</exception>
    public TropicalCycloneDatasetProcessor(string path)
        : this(ReadNamed(path), Path.GetFileName(Path.TrimEndingDirectorySeparator(path)))
    {
    }

    /// <summary>
    /// Reads the forecast zip <paramref name="relativePath"/> from
    /// <paramref name="source"/>, with its wind radii and best track zips when
    /// the source has them beside it.
    /// </summary>
    /// <param name="source">The asset source holding the zips.</param>
    /// <param name="relativePath">The forecast zip's source-relative path.</param>
    /// <exception cref="NhcFormatException">The file isn't a readable NHC advisory.</exception>
    public TropicalCycloneDatasetProcessor(IAssetSource source, string relativePath)
        : this(ReadFromSource(source, relativePath), AssetSourceHelpers.GetFileName(relativePath))
    {
    }

    /// <summary>Wraps an advisory already read.</summary>
    /// <param name="advisory">The advisory.</param>
    /// <param name="fileName">The name of its forecast zip or folder, the dataset's id.</param>
    public TropicalCycloneDatasetProcessor(TropicalCycloneAdvisory advisory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Advisory = advisory;
        FileName = fileName;
        _metadata = BuildMetadata(advisory);
        _parts = [.. BuildParts(advisory)];
    }

    /// <summary>The spec every advisory reports: <c>TC/1.0.0</c>, this model's version.</summary>
    public static SpecRef TropicalCycloneSpec { get; } = SpecRef.Parse("TC/1.0.0");

    /// <inheritdoc />
    public SpecRef Spec => TropicalCycloneSpec;

    /// <summary>The name of the dataset's forecast zip or folder.</summary>
    public string FileName { get; }

    /// <summary>The advisory.</summary>
    public TropicalCycloneAdvisory Advisory { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The advisory's WGS-84 extent (cone, tracks and forecast radii), and the
    /// time from the first past-track point to the last forecast point.
    /// </remarks>
    public DatasetMetadata Metadata => _metadata;

    /// <summary>Reads the metadata of the advisory at <paramref name="path"/>: its spec, extent and time coverage.</summary>
    /// <param name="path">The forecast zip or folder.</param>
    public static DatasetMetadata ReadMetadata(string path) => BuildMetadata(ReadNamed(path));

    /// <summary>Builds the dataset metadata of an advisory.</summary>
    /// <param name="advisory">The advisory.</param>
    public static DatasetMetadata BuildMetadata(TropicalCycloneAdvisory advisory)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        return new DatasetMetadata
        {
            Spec = TropicalCycloneSpec,
            Extent = advisory.Extent,
            HorizontalCrsEpsg = 4326,
            TimeCoverage = new TimeCoverage(advisory.TrackStart, advisory.ForecastEnd),
        };
    }

    /// <summary>The storm's id, and the <see cref="FeatureInfo.FeatureRef"/> of every pick on it: the ATCF id.</summary>
    public string StormId => Advisory.Storm.AtcfId;

    /// <inheritdoc />
    public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var palette = context?.Palette ?? PaletteType.Day;
        var zone = context?.DisplayTimeZone ?? TimeZoneInfo.Utc;
        _staleAge = Advisory.IsFinal ? null : context?.TimeStepAge;
        var (areas, track) = BuildInstructions(palette, zone, CultureInfo.CurrentCulture, context?.EcdisDisplay, _staleAge);
        VectorSubLayer[] subLayers =
        [
            new VectorSubLayer
            {
                LayerKey = TropicalCycloneStyle.AreasLayerKey,
                LayerName = $"Tropical cyclone areas: {FileName}",
                Instructions = areas,
                Plane = S98DisplayPlane.OnDemandSurface,
                WithinPlanePriority = TropicalCycloneStyle.AreasPriority,
                SourceFeatureType = TropicalCycloneStyle.AreasLayerKey,
                Legend = TropicalCycloneStyle.BuildAreasLegend(palette),
            },
            new VectorSubLayer
            {
                LayerKey = TropicalCycloneStyle.TrackLayerKey,
                LayerName = $"Tropical cyclone track: {FileName}",
                Instructions = track,
                Plane = S98DisplayPlane.OtherChartOverlays,
                WithinPlanePriority = TropicalCycloneStyle.TrackPriority,
                SourceFeatureType = TropicalCycloneStyle.TrackLayerKey,
                Legend = TropicalCycloneStyle.BuildTrackLegend(palette),
            },
        ];

        var current = Advisory.Current;
        var info = string.Create(
            CultureInfo.InvariantCulture,
            $"Tropical cyclone {StormId} {Advisory.Storm.Name}, advisory {Advisory.Advisory} issued {Advisory.IssueTime.UtcDateTime:yyyy-MM-dd HH:mm}Z: {Advisory.Forecast.Count} forecast points{(current?.MaxWindKnots is { } wind ? $", {wind} kt" : string.Empty)}. {NotForNavigationNotice}");

        return Task.FromResult(new VectorPortrayalResult
        {
            SubLayers = subLayers,
            Palette = WeatherPalette.Palette.GetColorPalette(palette),
            GeometryProvider = new FeatureGeometryProvider<TropicalCyclonePart>(_parts),
            Product = SpecName,
            Spec = Spec,
            SourceDatasetId = FileName,
            Info = info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = WeatherCoverageStyle.Symbols.Provider,
            AreaFillProvider = TropicalCycloneStyle.ResolveAreaFill,
            LayerNames = [TropicalCycloneStyle.AreasLayerKey, TropicalCycloneStyle.TrackLayerKey],
            GeographicExtent = DrawnExtent(),
            PreferLiveDrawing = true,
        });
    }

    /// <summary>
    /// Renders the advisory to a bitmap, the fills under the lines, framed on
    /// everything drawn (or on <see cref="RenderContext.Viewport"/>).
    /// </summary>
    /// <param name="widthPixels">Output width in pixels.</param>
    /// <param name="heightPixels">Output height in pixels.</param>
    /// <param name="context">Palette, scales, time zone and viewport.</param>
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
        var viewport = context?.Viewport;
        if (viewport is null && _parts.Length > 0)
        {
            var extent = DrawnExtent();
            var (minX, minY) = WebMercator.FromLonLat(extent.MinLongitude, extent.MinLatitude);
            var (maxX, maxY) = WebMercator.FromLonLat(extent.MaxLongitude, extent.MaxLatitude);
            viewport = PointGlyphHeadlessAdapter.FitViewport(
                new MercatorBounds(minX, minY, maxX, maxY), widthPixels, heightPixels, padding: 0.05);
        }

        if (viewport is null)
            return Task.FromResult(HeadlessVectorRenderer.RenderBlank(widthPixels, heightPixels, bg));

        var palette = context?.Palette ?? PaletteType.Day;
        var (areas, track) = BuildInstructions(
            palette, context?.DisplayTimeZone ?? TimeZoneInfo.Utc, CultureInfo.CurrentCulture, context?.EcdisDisplay, context?.TimeStepAge);
        return Task.FromResult(HeadlessVectorRenderer.Render(
            [.. areas, .. track],
            new FeatureGeometryProvider<TropicalCyclonePart>(_parts),
            WeatherPalette.Palette.GetColorPalette(palette),
            symbolProvider: WeatherCoverageStyle.Symbols.Provider,
            lineStyleProvider: null,
            symbolScale: context?.SymbolScale ?? 1.0,
            textScale: context?.TextScale ?? 1.0,
            widthPixels: widthPixels,
            heightPixels: heightPixels,
            background: bg,
            areaFillProvider: TropicalCycloneStyle.ResolveAreaFill,
            hiddenCategories: context?.HiddenInstructionCategories ?? DrawingInstructionCategory.None,
            basemap: context?.Basemap ?? BasemapKind.None,
            viewport: viewport));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="featureRef"/> is any part's id; the storm's own id,
    /// <see cref="StormId"/>, is the storm. Every part reports the storm, with
    /// the part as its Picked part attribute.
    /// </remarks>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);
        var index = Array.FindIndex(_parts, p => string.Equals(p.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : GetFeatureInfoAt(index);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The attributes (V23), in order: the notice; once the advisory is stale,
    /// its age (<see cref="PickAttribute.StaleNotice"/>); the storm's name; the
    /// advisory, its issue time and the next advisory due (for a final
    /// advisory, when it was issued); the position, max
    /// wind, pressure and movement at the advisory; the part picked; the 64,
    /// 50 and 34 kn radii by quadrant; and NHC's caution about the cone.
    /// </remarks>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _parts.Length)
            return null;
        var part = _parts[ordinal];
        var attributes = Describe(Advisory, part);
        if (_staleAge is { } age)
            attributes.Insert(1, PickAttribute.StaleNotice(StaleText(Advisory, age)));
        return new FeatureInfo
        {
            FeatureRef = StormId,
            FeatureType = FeatureType,
            FeatureTypeName = StormClassName(Advisory),
            Attributes = attributes,
            DrawingPriority = part.DrawingPriority,
            Geometry = part.GeometryType,
        };
    }

    /// <inheritdoc />
    public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _parts.Length)
            return null;
        var part = _parts[ordinal];
        return part.GeometryType switch
        {
            S100GeometryType.Point => new S100FeatureGeometry { Primitive = S100GeometryType.Point, Points = part.Points },
            S100GeometryType.Curve => new S100FeatureGeometry { Primitive = S100GeometryType.Curve, Curves = part.Curves },
            _ => new S100FeatureGeometry { Primitive = S100GeometryType.Surface, ExteriorRing = part.ExteriorRing, InteriorRings = part.InteriorRings },
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// One hit for the storm at most (V23), on the part nearest the pick:
    /// the storm and the forecast and best-track points first, then the
    /// tracks, then the radii from the 64 kt core out, then the cone.
    /// </remarks>
    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(double latitude, double longitude, double radiusMeters)
    {
        var hits = FeatureHitTester.HitTest(_parts, latitude, longitude, radiusMeters);
        if (hits.Count == 0)
            return hits;
        var best = hits.MinBy(h => (_parts[h.Ordinal].PickRank, h.DistanceMeters))!;
        return [best];
    }

    /// <inheritdoc />
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        var className = StormClassName(Advisory);
        for (var i = 0; i < _parts.Length; i++)
        {
            yield return new FeatureSummary
            {
                FeatureRef = _parts[i].Id,
                Ordinal = i,
                FeatureType = FeatureType,
                FeatureTypeName = className,
            };
        }
    }

    /// <summary>The storm's class for the pick card's pill (V23), such as "Category 4" or "Tropical storm".</summary>
    /// <param name="advisory">The advisory.</param>
    public static string StormClassName(TropicalCycloneAdvisory advisory)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        return advisory.Current is { } current
            ? TropicalCycloneStyle.ClassName(current.Classification, current.SaffirSimpsonCategory)
            : "Tropical cyclone";
    }

    /// <summary>
    /// The storm's name as the chart and the pick card spell it: the class
    /// word and the name, such as <c>HURRICANE IAN</c>.
    /// </summary>
    /// <param name="advisory">The advisory.</param>
    public static string StormName(TropicalCycloneAdvisory advisory)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        var word = TropicalCycloneStyle.ClassWord(advisory.Current?.Classification ?? TropicalCycloneClass.Unknown);
        var name = advisory.Storm.Name.Trim().ToUpperInvariant();
        return name.Length == 0 ? $"{word} {advisory.Storm.AtcfId}" : $"{word} {name}";
    }

    /// <summary>
    /// When NHC's next regular advisory is due after <paramref name="issueTime"/>:
    /// the next of 03, 09, 15 and 21 UTC. Intermediate advisories, every 3
    /// hours under watches or warnings, can come sooner.
    /// </summary>
    /// <param name="issueTime">The advisory's issue time.</param>
    public static DateTimeOffset NextAdvisoryDue(DateTimeOffset issueTime)
    {
        var utc = issueTime.ToUniversalTime();
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        for (int hour = 3; ; hour += 6)
        {
            var due = day.AddHours(hour);
            if (due > utc)
                return due;
        }
    }

    /// <summary>
    /// The parts of the advisory drawn and hit, for the catalog's identify and
    /// pick (<see cref="Spec.FeatureAccessor"/>).
    /// </summary>
    internal static IEnumerable<IS100Feature> DrawnFeatures(TropicalCycloneAdvisory advisory) => BuildParts(advisory);

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() => new TropicalCycloneDatasetData(Advisory, _metadata);

    /// <summary>Reads the advisory zips from a stream, the catalog route: the forecast zip only.</summary>
    internal static TropicalCycloneDatasetData OpenData(Stream stream)
    {
        // A file stream's siblings are on disk beside it.
        var advisory = stream is FileStream { Name: { } path } && NhcAdvisoryFiles.Find(path) is { } files
            ? NhcAdvisoryReader.Read(files)
            : NhcAdvisoryReader.Read(stream, null, null);
        return new TropicalCycloneDatasetData(advisory, BuildMetadata(advisory));
    }

    private static TropicalCycloneAdvisory ReadNamed(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return NhcAdvisoryReader.Read(path);
        }
        catch (NhcFormatException ex) when (!ex.Message.StartsWith(Path.GetFileName(path), StringComparison.Ordinal))
        {
            throw new NhcFormatException($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}: {ex.Message}", ex);
        }
    }

    private static TropicalCycloneAdvisory ReadFromSource(IAssetSource source, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        var fileName = AssetSourceHelpers.GetFileName(relativePath);
        using var forecast = AssetSourceHelpers.OpenSeekable(source, relativePath);
        Stream? radii = null;
        Stream? bestTrack = null;
        try
        {
            if (NhcAdvisoryFiles.TryParseFileName(fileName, out var kind, out var atcfId, out var advisory) && kind == NhcFileKind.Forecast)
            {
                var folder = relativePath[..^fileName.Length];
                radii = TryOpen(source, folder + NhcAdvisoryFiles.WindRadiiZipName(atcfId, advisory));
                bestTrack = TryOpen(source, folder + NhcAdvisoryFiles.BestTrackZipName(atcfId));
                using var final = TryOpen(source, folder + NhcAdvisoryFiles.FinalMarkerName(atcfId));
                if (final is not null)
                    return NhcAdvisoryReader.Read(forecast, radii, bestTrack) with { IsFinal = true };
            }

            return NhcAdvisoryReader.Read(forecast, radii, bestTrack);
        }
        catch (NhcFormatException ex)
        {
            throw new NhcFormatException($"{fileName}: {ex.Message}", ex);
        }
        finally
        {
            radii?.Dispose();
            bestTrack?.Dispose();
        }
    }

    // A sibling zip the source doesn't have is simply not read.
    private static Stream? TryOpen(IAssetSource source, string relativePath)
    {
        try
        {
            return AssetSourceHelpers.OpenSeekable(source, relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static List<TropicalCyclonePart> BuildParts(TropicalCycloneAdvisory advisory)
    {
        var id = advisory.Storm.AtcfId;
        var parts = new List<TropicalCyclonePart>();
        var current = advisory.Current;

        // The storm first, so its id resolves to it.
        if (current is not null)
            parts.Add(TropicalCyclonePart.Point(id, TropicalCyclonePartKind.Storm, "Storm, at the advisory", current.Position, 16, 0) with { Forecast = current });

        // A final advisory draws the storm and its past track only (§7): no
        // forecast, cone or radii.
        var forecast = advisory.IsFinal ? [] : advisory.Forecast;
        foreach (var (point, i) in forecast.Select((p, i) => (p, i)).Where(x => x.p.Tau > 0))
        {
            var wind = point.MaxWindKnots is { } kt ? string.Create(CultureInfo.InvariantCulture, $", {kt} kn") : string.Empty;
            parts.Add(TropicalCyclonePart.Point(
                string.Create(CultureInfo.InvariantCulture, $"{id} +{point.Tau}h"),
                TropicalCyclonePartKind.ForecastPoint,
                string.Create(CultureInfo.InvariantCulture, $"Forecast point +{point.Tau} h{wind}"),
                point.Position,
                15,
                0) with
            { Forecast = point, IsLastForecast = i == forecast.Count - 1 });
        }

        var past = advisory.PastTrack.Where(p => p.Time < advisory.SynopticTime).ToArray();
        for (int i = 0; i < past.Length; i++)
        {
            var wind = past[i].MaxWindKnots is { } kt ? string.Create(CultureInfo.InvariantCulture, $", {kt} kn") : string.Empty;
            parts.Add(TropicalCyclonePart.Point(
                string.Create(CultureInfo.InvariantCulture, $"{id} past {i + 1}"),
                TropicalCyclonePartKind.PastPoint,
                string.Create(CultureInfo.InvariantCulture, $"Best-track point −{(advisory.SynopticTime - past[i].Time).TotalHours:0} h{wind}"),
                past[i].Position,
                13,
                0));
        }

        var forecastTrack = advisory.IsFinal ? []
            : advisory.ForecastTrack.Count >= 2
            ? advisory.ForecastTrack
            : advisory.Forecast.Select(p => p.Position).ToArray();
        if (forecastTrack.Count >= 2)
            parts.Add(TropicalCyclonePart.Curve($"{id} track", TropicalCyclonePartKind.ForecastTrack, "Forecast track", forecastTrack, 14, 1));

        if (past.Length > 0)
        {
            var pastLine = past.Select(p => p.Position).ToList();
            if (current is not null)
                pastLine.Add(current.Position);
            if (pastLine.Count >= 2)
                parts.Add(TropicalCyclonePart.Curve($"{id} past track", TropicalCyclonePartKind.PastTrack, "Past track", pastLine, 13, 1));
        }

        // The radii at the advisory: NHC's polygons, or built from the
        // quadrants when the files had none.
        if (current is not null && !advisory.IsFinal)
        {
            foreach (var radius in current.Radii.Where(r => !r.IsEmpty).OrderByDescending(r => r.ThresholdKnots))
            {
                var surfaces = radius.Surfaces.Count > 0 ? radius.Surfaces : radius.QuadrantSurface(current.Position, 16);
                if (surfaces.Count == 0)
                    continue;
                parts.Add(TropicalCyclonePart.Surface(
                    string.Create(CultureInfo.InvariantCulture, $"{id} {radius.ThresholdKnots} kt"),
                    TropicalCyclonePartKind.Radius,
                    string.Create(CultureInfo.InvariantCulture, $"{radius.ThresholdKnots} kn wind radius"),
                    surfaces,
                    12,
                    2 + (64 - radius.ThresholdKnots) / 15) with
                { ThresholdKnots = radius.ThresholdKnots });
            }
        }

        if (advisory.Cone is { } cone && !advisory.IsFinal)
        {
            if (cone.DaysOneToThree.Count > 0)
                parts.Add(TropicalCyclonePart.Surface($"{id} cone days 1-3", TropicalCyclonePartKind.ConeDaysOneToThree, "Cone, days 1–3", cone.DaysOneToThree, 11, 5));
            if (cone.DaysFourToFive.Count > 0)
                parts.Add(TropicalCyclonePart.Surface($"{id} cone days 4-5", TropicalCyclonePartKind.ConeDaysFourToFive, "Cone, days 4–5", cone.DaysFourToFive, 11, 5));
            if (cone.Surfaces.Count > 0)
                parts.Add(TropicalCyclonePart.Surface($"{id} cone", TropicalCyclonePartKind.Cone, "Cone", cone.Surfaces, 11, 6));
        }

        return [.. parts.Select(p => p with { Advisory = advisory })];
    }

    // The drawing instructions of the parts: the fills, then the lines,
    // symbols and labels. The fills' opacity depends on the palette, the
    // labels' times on the zone and culture, what's drawn on the Weather
    // section's switches (V26) and the storm's label on the advisory's age
    // once it's stale (V29).
    private (IReadOnlyList<DrawingInstruction> Areas, IReadOnlyList<DrawingInstruction> Track) BuildInstructions(
        PaletteType palette, TimeZoneInfo zone, CultureInfo culture, EcdisDisplaySettings? display, TimeSpan? staleAge)
    {
        var areas = new List<DrawingInstruction>();
        var track = new List<DrawingInstruction>();
        var radii = TropicalCycloneStyle.IsShown(display, TropicalCycloneStyle.WindRadiiKey);
        var pastTrack = TropicalCycloneStyle.IsShown(display, TropicalCycloneStyle.PastTrackKey);
        var forecastTimes = TropicalCycloneStyle.IsShown(display, TropicalCycloneStyle.ForecastTimesKey);
        foreach (var part in _parts)
        {
            if ((part.Kind == TropicalCyclonePartKind.Radius && !radii)
                || (part.Kind is TropicalCyclonePartKind.PastTrack or TropicalCyclonePartKind.PastPoint && !pastTrack))
            {
                continue;
            }

            switch (part.Kind)
            {
                case TropicalCyclonePartKind.ConeDaysOneToThree:
                    areas.AddRange(TropicalCycloneStyle.BuildConeFill(part.Id, daysFourToFive: false, palette));
                    break;
                case TropicalCyclonePartKind.ConeDaysFourToFive:
                    areas.AddRange(TropicalCycloneStyle.BuildConeFill(part.Id, daysFourToFive: true, palette));
                    break;
                case TropicalCyclonePartKind.Cone:
                    track.Add(TropicalCycloneStyle.BuildConeOutline(part.Id));
                    break;
                case TropicalCyclonePartKind.Radius:
                    areas.Add(TropicalCycloneStyle.BuildRadiusFill(part.Id, part.ThresholdKnots, palette));
                    track.Add(TropicalCycloneStyle.BuildRadiusOutline(part.Id, part.ThresholdKnots));
                    break;
                case TropicalCyclonePartKind.ForecastTrack:
                    track.Add(TropicalCycloneStyle.BuildForecastTrack(part.Id));
                    break;
                case TropicalCyclonePartKind.PastTrack:
                    track.Add(TropicalCycloneStyle.BuildPastTrack(part.Id));
                    break;
                case TropicalCyclonePartKind.PastPoint:
                    track.Add(TropicalCycloneStyle.BuildPastPoint(part.Id));
                    break;
                case TropicalCyclonePartKind.ForecastPoint when part.Forecast is { } point:
                    track.AddRange(TropicalCycloneStyle.BuildForecastPoint(
                        part.Id,
                        TropicalCycloneStyle.SymbolFor(point.Classification, point.SaffirSimpsonCategory, point.Position.Latitude),
                        forecastTimes && TropicalCycloneStyle.IsLabelled(point, part.IsLastForecast) ? TropicalCycloneStyle.ForecastLabel(point, zone, culture) : null));
                    break;
                case TropicalCyclonePartKind.Storm when part.Forecast is { } point:
                    track.AddRange(TropicalCycloneStyle.BuildStorm(
                        part.Id,
                        Advisory.IsFinal
                            ? TropicalCycloneStyle.SymbolFor(TropicalCycloneClass.PostTropical, null, point.Position.Latitude)
                            : TropicalCycloneStyle.SymbolFor(point.Classification, point.SaffirSimpsonCategory, point.Position.Latitude),
                        estimated: false,
                        TropicalCycloneStyle.StormLabel(Advisory.Storm, point),
                        Advisory.IsFinal ? TropicalCycloneStyle.FinalAdvisoryLabel(Advisory, zone, culture)
                            : staleAge is { } age ? TropicalCycloneStyle.StaleAdvisoryLabel(Advisory, age)
                            : TropicalCycloneStyle.AdvisoryLabel(Advisory, point, zone, culture)));
                    break;
            }
        }

        return (areas, track);
    }

    internal static List<PickAttribute> Describe(TropicalCycloneAdvisory advisory, TropicalCyclonePart part)
    {
        var attributes = new List<PickAttribute>
        {
            PickAttribute.NavigationNotice(NotForNavigationNotice),
            Text("name", "Name", StormName(advisory)),
            Text("advisory", "Advisory", advisory.Advisory),
        };

        // A final advisory (§7) says so in place of when the next is due.
        if (advisory.IsFinal)
        {
            attributes.Add(Time("finalAdvisory", "Final advisory issued", advisory.IssueTime));
        }
        else
        {
            attributes.Add(Time("issued", "Issued", advisory.IssueTime));
            attributes.Add(Time("nextAdvisory", "Next advisory", NextAdvisoryDue(advisory.IssueTime)));
        }

        if (advisory.Current is { } current)
        {
            attributes.Add(Text("position", "Position", FormatPosition(current.Position)));
            if (current.MaxWindKnots is { } wind)
            {
                attributes.Add(Text(
                    "maxWind",
                    "Max wind",
                    current.GustKnots is { } gust
                        ? string.Create(CultureInfo.InvariantCulture, $"{wind} kn, gusts {gust} kn")
                        : string.Create(CultureInfo.InvariantCulture, $"{wind} kn")));
            }

            if (current.PressureHectopascals is { } pressure)
                attributes.Add(Text("pressure", "Pressure", string.Create(CultureInfo.InvariantCulture, $"{pressure} hPa")));
            if (TropicalCycloneStyle.Motion(current) is { } motion)
                attributes.Add(Text("movement", "Movement", motion.Replace(" kt", " kn", StringComparison.Ordinal)));
        }

        attributes.Add(Text("pickedPart", "Picked part", part.Description));

        foreach (var threshold in advisory.IsFinal ? [] : (int[])[64, 50, 34])
        {
            if (advisory.Current?.Radii.FirstOrDefault(r => r.ThresholdKnots == threshold && !r.IsEmpty) is not { } radius)
                continue;
            attributes.Add(Text(
                string.Create(CultureInfo.InvariantCulture, $"radius{threshold}"),
                string.Create(CultureInfo.InvariantCulture, $"{threshold} kn radii"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{radius.NortheastNauticalMiles:0} / {radius.SoutheastNauticalMiles:0} / {radius.SouthwestNauticalMiles:0} / {radius.NorthwestNauticalMiles:0} nm (NE/SE/SW/NW)")));
        }

        if (advisory.Cone is not null && !advisory.IsFinal)
            attributes.Add(Text("cone", "Cone", ConeCaution));

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

    // Degrees and minutes, as the mock-up's card: 26°00′N 083°30′W.
    private static string FormatPosition(GeoPosition position)
    {
        static (int Degrees, int Minutes) Split(double value)
        {
            var minutes = (int)Math.Round(Math.Abs(value) * 60);
            return (minutes / 60, minutes % 60);
        }

        var (latD, latM) = Split(position.Latitude);
        var (lonD, lonM) = Split(position.Longitude);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{latD:00}°{latM:00}′{(position.Latitude < 0 ? 'S' : 'N')} {lonD:000}°{lonM:00}′{(position.Longitude < 0 ? 'W' : 'E')}");
    }

    // The bounds of everything drawn, padded a little.
    private GeographicBounds DrawnExtent()
    {
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        foreach (var p in _parts.SelectMany(f => f.Points.Concat(f.Curves.SelectMany(c => c)).Concat(f.Surfaces.SelectMany(s => s.ExteriorRing))))
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }

        if (south > north)
            return new GeographicBounds(-180, -90, 180, 90);

        const double pad = 0.5;
        return new GeographicBounds(
            Math.Max(-180, west - pad), Math.Max(-90, south - pad), Math.Min(180, east + pad), Math.Min(90, north + pad));
    }
}

/// <summary>What part of a storm a <see cref="TropicalCyclonePart"/> is.</summary>
internal enum TropicalCyclonePartKind
{
    Storm,
    ForecastPoint,
    PastPoint,
    ForecastTrack,
    PastTrack,
    Radius,
    ConeDaysOneToThree,
    ConeDaysFourToFive,
    Cone,
}

/// <summary>
/// One drawn part of a storm as a feature: a point, a line or an area, for
/// drawing, hit tests and the catalog. Every part reports the storm when
/// picked (<see cref="TropicalCycloneDatasetProcessor.GetFeatureInfo"/>).
/// </summary>
internal sealed record TropicalCyclonePart : IS100Feature
{
    private IReadOnlyDictionary<string, string>? _attributes;

    public required string Id { get; init; }

    public required TropicalCyclonePartKind Kind { get; init; }

    /// <summary>The part as the pick card's Picked part row names it.</summary>
    public required string Description { get; init; }

    /// <summary>The advisory the part belongs to, for its attributes in the catalog.</summary>
    public TropicalCycloneAdvisory? Advisory { get; init; }

    /// <summary>The forecast point of a storm or forecast point part.</summary>
    public TropicalCycloneForecastPoint? Forecast { get; init; }

    /// <summary>Whether a forecast point part is the advisory's last.</summary>
    public bool IsLastForecast { get; init; }

    /// <summary>A radius part's threshold, 34, 50 or 64 kt.</summary>
    public int ThresholdKnots { get; init; }

    /// <summary>The highest drawing priority of the part's instructions, for the pick order.</summary>
    public int DrawingPriority { get; init; }

    /// <summary>The part's place in the hit preference: lower is preferred.</summary>
    public int PickRank { get; init; }

    public string FeatureType => TropicalCycloneDatasetProcessor.FeatureType;

    public S100GeometryType GeometryType { get; init; }

    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = [];

    public IReadOnlyList<SurfacePart> Surfaces { get; init; } = [];

    public IReadOnlyList<GeoPosition> ExteriorRing => Surfaces.Count > 0 ? Surfaces[0].ExteriorRing : [];

    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings => Surfaces.Count > 0 ? Surfaces[0].InteriorRings : [];

    public IReadOnlyDictionary<string, string> Attributes => _attributes ??= Advisory is null
        ? new Dictionary<string, string>(StringComparer.Ordinal)
        : TropicalCycloneDatasetProcessor.Describe(Advisory, this).ToDictionary(a => a.Code, a => a.RawValue, StringComparer.Ordinal);

    public IReadOnlyList<IS100ComplexAttribute> ComplexAttributes => [];

    public static TropicalCyclonePart Point(string id, TropicalCyclonePartKind kind, string description, GeoPosition position, int drawingPriority, int pickRank) => new()
    {
        Id = id,
        Kind = kind,
        Description = description,
        GeometryType = S100GeometryType.Point,
        Points = [position],
        DrawingPriority = drawingPriority,
        PickRank = pickRank,
    };

    public static TropicalCyclonePart Curve(string id, TropicalCyclonePartKind kind, string description, IReadOnlyList<GeoPosition> line, int drawingPriority, int pickRank) => new()
    {
        Id = id,
        Kind = kind,
        Description = description,
        GeometryType = S100GeometryType.Curve,
        Curves = [line],
        DrawingPriority = drawingPriority,
        PickRank = pickRank,
    };

    public static TropicalCyclonePart Surface(string id, TropicalCyclonePartKind kind, string description, IReadOnlyList<SurfacePart> surfaces, int drawingPriority, int pickRank) => new()
    {
        Id = id,
        Kind = kind,
        Description = description,
        GeometryType = S100GeometryType.Surface,
        Surfaces = surfaces,
        DrawingPriority = drawingPriority,
        PickRank = pickRank,
    };
}
