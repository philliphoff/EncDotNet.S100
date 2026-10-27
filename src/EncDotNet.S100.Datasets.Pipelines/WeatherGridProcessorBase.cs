using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Sampling;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The format-neutral part of a weather grid processor: everything after the
/// reader has produced a <see cref="WeatherGridSet"/>. The NetCDF-CF processor
/// (<see cref="NetCdfDatasetProcessor"/>) and the GRIB2 processor
/// (<see cref="GribDatasetProcessor"/>) derive from it and only open their
/// files (design note weather-sources.md §3.3).
/// </summary>
/// <remarks>
/// <para>What it provides:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Drawing.</b> One <see cref="WeatherCoverageSource"/> per parameter group
/// (<see cref="WeatherCoverageSource.CreateForGroups"/>), drawn with the
/// code-defined weather styles that apply to it and are on by default (the
/// wave height fill and the wind barbs; the wind speed fill and wave arrows
/// are off by default, design note §10.1) through
/// <see cref="WeatherPortrayal.BuildAsync"/>. Each style is one layer stack
/// entry, on the plane and priority of design note §5. The overview level of
/// each source is chosen from the viewport's cells per pixel, as S-102 does.
/// </description></item>
/// <item><description>
/// <b>Time.</b> <see cref="ITimeAwareDatasetProcessor"/>: the union of the
/// parameters' valid times, forecast steps (analysis steps when the data has no
/// reference time) drawn from the nearest step within the typical step, and
/// the model run (<see cref="ForecastRun"/>).
/// </description></item>
/// <item><description>
/// <b>Sampling and picking.</b> <see cref="ICoverageSampler"/> returns
/// <see cref="CoverageFieldsSample"/>s with each field's quantity and unit;
/// <see cref="GetCoverageInfo"/> leads with
/// <see cref="NotForNavigationNotice"/>.
/// </description></item>
/// <item><description>
/// <b>Headless rendering</b> (<see cref="IHeadlessImageRenderer"/>) through the
/// <see cref="HeadlessCompositor"/>, and the catalog payload
/// (<see cref="WeatherGridDatasetData"/>) through
/// <see cref="ILoadedDatasetProjection"/>.
/// </description></item>
/// </list>
/// <para>
/// It lives in <c>EncDotNet.S100.Datasets.Pipelines</c> rather than
/// <c>EncDotNet.S100.Datasets.Weather</c> because the capabilities it
/// implements (<see cref="IHeadlessImageRenderer"/>,
/// <see cref="ILoadedDatasetProjection"/>, <see cref="HeadlessCompositor"/>)
/// are defined here, and this project already references every dataset
/// project; the format readers don't reference it, or each other.
/// </para>
/// </remarks>
public abstract class WeatherGridProcessorBase :
    IDatasetProcessor,
    ICoveragePortrayalSource,
    IHeadlessImageRenderer,
    ITimeAwareDatasetProcessor,
    ICoverageSampler,
    ILoadedDatasetProjection,
    IDisposable
{
    /// <summary>The first line of every pick report on weather data (design note §3.1).</summary>
    public const string NotForNavigationNotice = "Weather model data, not for navigation.";

    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly (WeatherCoverageSource Source, WeatherCoverageStyle[] Styles)[] _layers;
    private readonly WeatherGridSampler _sampler;
    private readonly string? _contentHash;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private DatasetMetadata? _metadata;
    private WeatherDatasetContent[]? _contents;
    private bool _disposed;

    /// <summary>Creates the processor over a grid set a reader produced.</summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="spec">The file format and version, for example <c>NetCDF/4.0.0</c>.</param>
    /// <param name="fileName">The dataset's file name, its stable id.</param>
    /// <param name="referenceTime">The model run's reference time (UTC), or <see langword="null"/> when the data doesn't give one.</param>
    /// <param name="crsTransformFactory">Creates the transforms the headless compositor needs.</param>
    /// <param name="contentHash">A hash of the file's bytes for <see cref="CoveragePortrayalResult.ContentScope"/>, or <see langword="null"/>.</param>
    protected WeatherGridProcessorBase(
        WeatherGridSet gridSet,
        SpecRef spec,
        string fileName,
        DateTime? referenceTime,
        ICrsTransformFactory crsTransformFactory,
        string? contentHash = null)
    {
        ArgumentNullException.ThrowIfNull(gridSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(crsTransformFactory);

        GridSet = gridSet;
        Spec = spec;
        FileName = fileName;
        ReferenceTime = referenceTime is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null;
        _crsTransformFactory = crsTransformFactory;
        _contentHash = contentHash;

        Sources = WeatherCoverageSource.CreateForGroups(gridSet, spec);
        _layers = Sources
            .Select(source => (source, WeatherCoverageStyle.CreateFor(source).Where(s => s.IsVisibleByDefault).ToArray()))
            .ToArray();
        _sampler = new WeatherGridSampler(gridSet, ReferenceTime);
        AvailableTimes = _sampler.Times;
        ForecastRun = new DatasetForecastRun(gridSet.Origin.Model, ReferenceTime);
        TimeSemantics = new TimeSemantics(
            ReferenceTime is null ? TimeStepKind.Analysis : TimeStepKind.Forecast,
            TimeStepSelection.Nearest);
    }

    /// <inheritdoc />
    public SpecRef Spec { get; }

    /// <summary>The grid set the reader produced.</summary>
    public WeatherGridSet GridSet { get; }

    /// <summary>The dataset's file name.</summary>
    public string FileName { get; }

    /// <summary>The model run's reference time (UTC), or <see langword="null"/> when the data doesn't give one.</summary>
    public DateTime? ReferenceTime { get; }

    /// <summary>One coverage source per parameter group, in the order the groups first appear.</summary>
    public IReadOnlyList<WeatherCoverageSource> Sources { get; }

    /// <summary>The layer keys drawn, one per style that applies and is on by default, in stack order.</summary>
    public IReadOnlyList<string> LayerKeys => _layers.SelectMany(l => l.Styles).Select(s => s.LayerKey).ToArray();

    /// <summary>
    /// What the file holds, for <c>info</c> output: every parameter of the grid
    /// set (the ones the weather table knows are used; unknown ones aren't
    /// drawn or sampled, with why), then what the reader left out of the grid
    /// set altogether, with why (for GRIB2, fields with an unsupported
    /// template such as CCSDS; for NetCDF, variables on other dimensions).
    /// </summary>
    public IReadOnlyList<WeatherDatasetContent> Contents => _contents ??= DescribeContents().ToArray();

    /// <summary>
    /// Lists <see cref="Contents"/>. The default lists the grid set's
    /// parameters, unknown ones as not in the weather parameter table; readers
    /// add what they left out.
    /// </summary>
    protected virtual IEnumerable<WeatherDatasetContent> DescribeContents() =>
        GridSet.Parameters.Select(p => Describe(p, null));

    /// <summary>Describes one parameter of the grid set for <see cref="Contents"/>.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="reason">Why an unknown parameter isn't used, or <see langword="null"/> for the default.</param>
    protected static WeatherDatasetContent Describe(WeatherParameter parameter, string? reason)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var name = parameter.SourceName is { } source && source != parameter.Id
            ? $"{parameter.Id} ({source})"
            : parameter.Id;
        return new WeatherDatasetContent(
            name,
            parameter.LevelDescription,
            parameter.Steps.Count,
            parameter.IsKnown ? null : reason ?? "The weather parameter table doesn't have this parameter.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Derived once from the grid: the cells' WGS-84 extent (half a cell
    /// beyond the outer nodes), EPSG:4326, and the first to last valid time.
    /// </remarks>
    public DatasetMetadata Metadata => _metadata ??= BuildMetadata(GridSet, Spec, AvailableTimes);

    /// <inheritdoc />
    public IReadOnlyList<DateTime> AvailableTimes { get; }

    /// <inheritdoc />
    public TimeSemantics TimeSemantics { get; }

    /// <summary>
    /// The model run: the model the file names (CF <c>source</c>,
    /// <c>institution</c> or <c>title</c>; for GRIB2 the generating process,
    /// file name or centre, see <see cref="Grib.GribWeatherDataset.Model"/>) and its
    /// reference time when the data gives one.
    /// </summary>
    public DatasetForecastRun ForecastRun { get; }

    /// <summary>
    /// Builds the dataset metadata of a weather grid: the cells' WGS-84 extent
    /// (half a cell beyond the outer nodes), EPSG:4326 and the time coverage.
    /// Shared by the processors and the registrations' metadata readers.
    /// </summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="spec">The file format and version.</param>
    /// <param name="times">The valid times, in any order.</param>
    public static DatasetMetadata BuildMetadata(WeatherGridSet gridSet, SpecRef spec, IReadOnlyList<DateTime> times)
    {
        ArgumentNullException.ThrowIfNull(gridSet);
        ArgumentNullException.ThrowIfNull(times);
        var grid = gridSet.Grid;
        double halfLat = grid.SpacingLatitudinal / 2, halfLon = grid.SpacingLongitudinal / 2;
        return new DatasetMetadata
        {
            Spec = spec,
            Extent = new BoundingBox(
                Math.Max(-90, grid.OriginLatitude - halfLat),
                grid.OriginLongitude - halfLon,
                Math.Min(90, grid.OriginLatitude + (grid.Rows - 1) * grid.SpacingLatitudinal + halfLat),
                grid.OriginLongitude + (grid.Columns - 1) * grid.SpacingLongitudinal + halfLon),
            HorizontalCrsEpsg = 4326,
            TimeCoverage = times.Count == 0 ? null : new TimeCoverage(times.Min(), times.Max()),
        };
    }

    /// <inheritdoc />
    public async Task<CoveragePortrayalResult> BuildCoveragePortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var subLayers = new List<CoverageSubLayerBase>();
            var info = new List<string>();
            foreach (var (source, styles) in _layers)
            {
                if (styles.Length == 0)
                    continue;

                SelectOverviewLevel(source, context?.Viewport);
                var result = await WeatherPortrayal.BuildAsync(source, styles, FileName, context, _contentHash, cancellationToken)
                    .ConfigureAwait(false);
                subLayers.AddRange(result.SubLayers);
                info.Add(result.Info);
            }

            var extent = Metadata.Extent!;
            return new CoveragePortrayalResult
            {
                SubLayers = subLayers,
                Spec = Spec,
                SourceDatasetId = FileName,
                Info = info.Count > 0
                    ? string.Join(" ", info)
                    : $"{GridSet.Origin.Model}: no parameter this viewer draws. {NotForNavigationNotice}",
                ContentScope = _contentHash is null ? null : $"ds:{_contentHash}|{WeatherCoverageStyle.ContentScope}",
                LayerNames = subLayers.Select(l => l.LayerKey).ToArray(),
                CoverageExtent = new GeographicBounds(extent.WestLongitude, extent.SouthLatitude, extent.EastLongitude, extent.NorthLatitude),
            };
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>
    /// Renders the weather layers to a bitmap through the headless compositor,
    /// framed on the grid (or on <see cref="RenderContext.Viewport"/>), at the
    /// context's time step (the first step when unset).
    /// </summary>
    /// <param name="widthPixels">Output width in pixels.</param>
    /// <param name="heightPixels">Output height in pixels.</param>
    /// <param name="context">Palette, time step, symbol scale and viewport.</param>
    /// <param name="background">Background fill; opaque white by default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new bitmap owned by the caller.</returns>
    public async Task<SKBitmap> RenderHeadlessAsync(
        int widthPixels,
        int heightPixels,
        RenderContext? context = null,
        RgbaColor? background = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        var portrayal = await BuildCoveragePortrayalAsync(context, cancellationToken).ConfigureAwait(false);

        // Frame the grid here when the caller gives no viewport: the
        // compositor's own fit snaps longitudes to whole degrees (for its seam
        // handling), which leaves a regional grid a small patch in the middle.
        var viewport = context?.Viewport;
        if (viewport is null)
        {
            var extent = Metadata.Extent!;
            var (minX, minY) = WebMercator.FromLonLat(extent.WestLongitude, extent.SouthLatitude);
            var (maxX, maxY) = WebMercator.FromLonLat(extent.EastLongitude, extent.NorthLatitude);
            viewport = PointGlyphHeadlessAdapter.FitViewport(
                new MercatorBounds(minX, minY, maxX, maxY), widthPixels, heightPixels, padding: 0.05);
        }

        return new HeadlessCompositor(_crsTransformFactory).Render(
            [HeadlessCompositeInput.ForCoverage(portrayal)],
            new HeadlessCompositeOptions
            {
                Width = widthPixels,
                Height = heightPixels,
                Background = background ?? new RgbaColor(255, 255, 255, 255),
                Viewport = viewport,
                Mariner = context?.Mariner,
                Basemap = context?.Basemap ?? BasemapKind.None,
            });
    }

    /// <summary>Always <see langword="null"/>: a weather grid has no discrete features; see <see cref="GetCoverageInfo"/>.</summary>
    /// <param name="featureRef">Ignored.</param>
    public FeatureInfo? GetFeatureInfo(string featureRef) => null;

    /// <summary>
    /// Samples every parameter at the grid node nearest the point and the step
    /// nearest <paramref name="time"/> (the first step when <see langword="null"/>).
    /// The first attribute is <see cref="NotForNavigationNotice"/>, then the
    /// model run and valid time, then each value with its unit: wave heights
    /// in metres, wind speed in knots (raw value in m/s), directions in
    /// degrees ("from"), periods in seconds.
    /// </summary>
    /// <param name="latitude">Latitude, WGS-84 degrees.</param>
    /// <param name="longitude">Longitude, WGS-84 degrees.</param>
    /// <param name="time">The selected time step, or <see langword="null"/>.</param>
    /// <returns>The pick, or <see langword="null"/> outside the grid or where no parameter has data.</returns>
    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time)
    {
        if (AvailableTimes.Count == 0 || _sampler.FindNode(latitude, longitude) is not { } node)
            return null;

        DateTimeOffset? requested = time is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;
        int step = CoverageSampling.SelectTimeStep(AvailableTimes, requested);
        if (_sampler.Read(node, step, requested) is not { } sample)
            return null;

        var validTime = sample.SampleTime ?? AvailableTimes[step];
        var attributes = new List<PickAttribute>
        {
            new() { Code = "notice", Name = "Notice", RawValue = NotForNavigationNotice, DisplayValue = NotForNavigationNotice },
            new()
            {
                Code = "model",
                Name = "Model",
                RawValue = GridSet.Origin.Model,
                DisplayValue = ReferenceTime is { } run
                    ? string.Create(CultureInfo.InvariantCulture, $"{GridSet.Origin.Model}, run {run:yyyy-MM-dd HH}Z")
                    : GridSet.Origin.Model,
            },
            new()
            {
                Code = "timePoint",
                Name = "Valid time",
                RawValue = validTime.ToString("u", CultureInfo.InvariantCulture),
                DateTimeValue = validTime,
            },
        };

        foreach (var field in sample.Fields)
        {
            if (Pick(field) is { } attribute)
                attributes.Add(attribute);
        }

        return new FeatureInfo
        {
            FeatureRef = string.Create(CultureInfo.InvariantCulture, $"({node.Row},{node.Col})"),
            FeatureType = "WeatherGridPoint",
            FeatureTypeName = "Weather grid point",
            Attributes = attributes,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every known parameter at the grid node nearest the point, at the step
    /// nearest the requested time; see <see cref="WeatherGridDatasetData.FindSites"/>.
    /// </remarks>
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => _sampler.FindSites(query);

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() => new WeatherGridDatasetData(GridSet, Metadata, _sampler);

    /// <summary>Releases the file and the render gate.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(disposing: true);
        _renderGate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the reader's file. Called once, from <see cref="Dispose()"/>.</summary>
    /// <param name="disposing">Always <see langword="true"/>.</param>
    protected abstract void Dispose(bool disposing);

    // The coarsest overview level whose spacing doesn't exceed one cell per
    // pixel at the viewport's resolution (the S-102 rule; issue #486).
    private static void SelectOverviewLevel(WeatherCoverageSource source, Viewport? viewport)
    {
        source.SelectOverviewLevel(0);
        if (viewport is null || viewport.WidthPixels <= 0 || viewport.HeightPixels <= 0)
            return;

        var region = GridRegion.FromViewport(viewport, source.Metadata.GridMetadata);
        int cellsPerPixel = Math.Min(region.RowStride, region.ColStride);
        int level = cellsPerPixel <= 1 ? 0 : (int)Math.Floor(Math.Log2(cellsPerPixel));
        source.SelectOverviewLevel(Math.Clamp(level, 0, source.AvailableOverviewLevels.Count - 1));
    }

    private static PickAttribute? Pick(SampledField field)
    {
        string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        string raw = field.Value is { } v ? Number(v, "0.##########") : "NoData";
        string? Display(Func<double, string> format) => field.Value is { } value ? format(value) : "—";

        return field.Name switch
        {
            WeatherParameters.WaveHeight => new() { Code = field.Name, Name = "Significant wave height", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0.0#")} m") },
            WeatherParameters.WaveDirection => new() { Code = field.Name, Name = "Wave direction (from)", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0")}°") },
            WeatherParameters.WavePeriod => new() { Code = field.Name, Name = "Wave period", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0.#")} s") },
            WeatherParameters.WindSeaHeight => new() { Code = field.Name, Name = "Wind-sea height", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0.0#")} m") },
            WeatherParameters.WindSeaDirection => new() { Code = field.Name, Name = "Wind-sea direction (from)", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0")}°") },
            WeatherParameters.SwellHeight => new() { Code = field.Name, Name = "Swell height", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0.0#")} m") },
            WeatherParameters.WindSpeed => new() { Code = field.Name, Name = "Wind speed", RawValue = raw, DisplayValue = Display(x => $"{Number(Speed.FromMetresPerSecond(x).TotalKnots, "0.#")} kn") },
            WeatherParameters.WindFromDirection => new() { Code = field.Name, Name = "Wind direction (from)", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0")}°") },
            WeatherParameters.WindGust => new() { Code = field.Name, Name = "Wind gust", RawValue = raw, DisplayValue = Display(x => $"{Number(Speed.FromMetresPerSecond(x).TotalKnots, "0.#")} kn") },
            WeatherParameters.WindU => new() { Code = field.Name, Name = "Wind, eastward component", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0.0#")} m/s") },
            WeatherParameters.WindV => new() { Code = field.Name, Name = "Wind, northward component", RawValue = raw, DisplayValue = Display(x => $"{Number(x, "0.0#")} m/s") },
            WeatherParameters.PressureMsl => new() { Code = field.Name, Name = "Mean sea level pressure", RawValue = raw, DisplayValue = Display(x => $"{Number(x / 100, "0.#")} hPa") },
            // The knots copy of the wind speed is already in the speed's display value.
            _ => null,
        };
    }
}
