using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IPickService"/> implementation: locates the dataset
/// entry and processor that own each hit feature via
/// <see cref="IDatasetLoaderService"/> and pushes the resolved feature list
/// into <see cref="PickReportViewModel"/>. Failed reference navigations are
/// surfaced as a transient notification.
/// </summary>
/// <remarks>
/// A click is picked through the map session's engine pick
/// (<see cref="IDatasetLoaderService.PickAsync"/>): each drawn dataset's own
/// spatial index, ranked topmost-first by the S-98 stack, not one Mapsui
/// feature per drawn op (map rendering slice&#160;8). This service resolves
/// each pick into a <see cref="PickHit"/>, deduplicates by (processor,
/// feature ref), and presents the list to the panel.
///
/// Depends only on small, leaf-level singletons — never on the root
/// <c>MainViewModel</c> — so the dependency graph stays acyclic.
/// </remarks>
internal sealed class PickService : IPickService
{
    private readonly IDatasetLoaderService _loader;
    private readonly PickReportViewModel _pickReport;
    private readonly INotificationService _notifications;
    private readonly GlobalTimeService? _globalTime;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly IThemeService? _themeService;
    private readonly IMarinerSettingsProvider? _marinerSettings;
    private readonly EncDotNet.S100.Viewer.Services.Depth.LocationDepthProbe _depthProbe = new();
    private readonly ViewerDatasetCatalog? _catalog;
    private readonly EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache? _routeSources;
    private readonly EncDotNet.S100.Viewer.Services.Depth.IWaterLevelAdjustmentSettings? _waterLevelSettings;

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        INotificationService notifications)
        : this(loader, pickReport, notifications, globalTime: null, timeFormat: null, themeService: null)
    {
    }

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        INotificationService notifications,
        GlobalTimeService? globalTime)
        : this(loader, pickReport, notifications, globalTime, timeFormat: null, themeService: null)
    {
    }

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        INotificationService notifications,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat)
        : this(loader, pickReport, notifications, globalTime, timeFormat, themeService: null)
    {
    }

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        INotificationService notifications,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat,
        IThemeService? themeService,
        IMarinerSettingsProvider? marinerSettings = null,
        ViewerDatasetCatalog? catalog = null,
        EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache? routeSources = null,
        EncDotNet.S100.Viewer.Services.Depth.IWaterLevelAdjustmentSettings? waterLevelSettings = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(notifications);
        _loader = loader;
        _pickReport = pickReport;
        _notifications = notifications;
        _globalTime = globalTime;
        _timeFormat = timeFormat;
        _themeService = themeService;
        _marinerSettings = marinerSettings;
        _catalog = catalog;
        _routeSources = routeSources;
        _waterLevelSettings = waterLevelSettings;

        // The pick-report VM raises NavigateRequested when the user
        // clicks a row in the References list. Failures surface as a
        // transient notification so the user knows the click registered
        // but the target is missing.
        _pickReport.NavigateRequested += (_, reference) =>
        {
            if (!NavigateToReference(reference))
            {
                _notifications.Create(Strings.Toast_Warning)
                    .WithSeverity(NotificationSeverity.Warning)
                    .WithContent(string.Format(
                        Strings.Status_FeatureRefNotFound,
                        reference.TargetRef))
                    .Show();
            }
        };
    }

    public async Task HandlePickAsync(MPoint? worldPosition, double resolution, IReadOnlyList<DynamicPickHit>? dynamicHits = null)
    {
        using var __cmd = ViewerObservability.BeginCommand("pick");

        var dynamic = dynamicHits ?? Array.Empty<DynamicPickHit>();

        if (worldPosition is null)
        {
            // Even without a position a pick may still carry dynamic hits;
            // honour them if present, otherwise clear.
            if (dynamic.Count > 0)
            {
                _pickReport.SetPicks(Array.Empty<PickHit>(), dynamic);
                return;
            }
            _pickReport.Clear();
            return;
        }

        // SphericalMercator → WGS84. Mapsui returns (lon, lat).
        var (lon, lat) = SphericalMercator.ToLonLat(worldPosition.X, worldPosition.Y);
        var location = new PickLocation(lat, lon);
        double? pickResolution = double.IsFinite(resolution) && resolution > 0 ? resolution : null;
        var picks = await _loader.PickAsync(new GeographicPickQuery
        {
            Latitude = lat,
            Longitude = lon,
            // The click tolerance on screen, as ground metres at the pick.
            RadiusMeters = pickResolution is { } r
                ? PickTolerancePx * r * Math.Cos(lat * Math.PI / 180.0)
                : EncDotNet.S100.Renderers.Mapsui.Avalonia.AvaloniaMapsuiMapAdapter.DefaultPickRadiusMeters,
            Resolution = pickResolution,
        }).ConfigureAwait(true);

        var hits = ResolveHits(picks, out var coverageHit);
        if (hits.Count == 0 && dynamic.Count == 0)
        {
            // No vector hit. Try a coverage pick before clearing — S-102,
            // S-104 and S-111 processors sample their grid at the point
            // (GetCoverageInfo) and return a synthesised feature.
            if (coverageHit is null && TryCoveragePick(lat, lon, out var sampled))
                coverageHit = sampled;

            if (coverageHit is not null)
            {
                var coverageDepth = BuildLocationDepth(new[] { coverageHit }, lat, lon);
                _pickReport.SetPicks(new[] { coverageHit }, Array.Empty<DynamicPickHit>(), location, coverageDepth);
                return;
            }

            // An empty-map tap, or a hit on a dataset no loaded entry owns.
            // Either way, hide the panel so it doesn't keep showing stale state.
            _pickReport.Clear();
            return;
        }

        var vectorDepth = BuildLocationDepth(hits, lat, lon);
        _pickReport.SetPicks(hits, dynamic, location, vectorDepth);
    }

    /// <summary>The on-screen pick tolerance, in DIP, around the click.</summary>
    internal const double PickTolerancePx = 6.0;

    /// <summary>
    /// Publishes a pre-resolved geographic pick (the MCP <c>pick_features</c>
    /// path). Each feature is re-queried against its owning dataset's
    /// processor so the panel shows full FC-decoded attributes; entries that
    /// no longer resolve are skipped. The pick highlight follows because it
    /// observes the same <see cref="PickReportViewModel"/>.
    /// </summary>
    public void PresentGeographicPick(
        double latitude,
        double longitude,
        IReadOnlyList<GeographicPickFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        using var __cmd = ViewerObservability.BeginCommand("pick.geographic");

        var hits = new List<PickHit>();
        using var processors = _loader.AcquireProcessors();
        foreach (var feature in features)
        {
            if (!TryResolveProcessorByDisplayName(
                    processors,
                    feature.DatasetDisplayName,
                    out var entry,
                    out var processor))
                continue;

            var info = processor.GetFeatureInfo(feature.FeatureRef);
            if (info is null)
                continue;

            hits.Add(BuildHit(info, entry, processor));
        }

        if (hits.Count == 0)
        {
            _pickReport.Clear();
            return;
        }

        var geoDepth = BuildLocationDepth(hits, latitude, longitude);
        _pickReport.SetPicks(hits, Array.Empty<DynamicPickHit>(), new PickLocation(latitude, longitude), geoDepth);
    }

    private bool TryResolveProcessorByDisplayName(
        IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> processors,
        string displayName,
        out DatasetEntry owningEntry,
        out IDatasetProcessor processor)
    {
        foreach (var (entry, proc) in processors)
        {
            if (string.Equals(entry.DisplayName, displayName, StringComparison.Ordinal))
            {
                owningEntry = entry;
                processor = proc;
                return true;
            }
        }

        owningEntry = null!;
        processor = null!;
        return false;
    }

    /// <summary>
    /// Resolves the engine's picks, topmost-first by the S-98 stack, into
    /// pick hits for the loaded entries that own them, deduplicated by
    /// (processor, feature ref) and ordered by drawing priority within each
    /// dataset. The first coverage pick, if any, comes back separately: it
    /// shows only when nothing else is hit.
    /// </summary>
    private List<PickHit> ResolveHits(IReadOnlyList<S100Pick> picks, out PickHit? coverageHit)
    {
        coverageHit = null;
        var hits = new List<PickHit>();
        var seen = new HashSet<(IDatasetProcessor processor, string featureRef)>();
        using var processors = _loader.AcquireProcessors();

        foreach (var pick in picks)
        {
            if (!TryResolveOwner(processors, pick.DatasetId, out var owningEntry, out var processor))
                continue;

            if (pick.IsCoverage)
            {
                coverageHit ??= BuildHit(pick.Info, owningEntry, processor);
                continue;
            }

            var featureRef = pick.Info.FeatureRef;
            if (string.IsNullOrEmpty(featureRef) || !seen.Add((processor, featureRef)))
                continue;

            hits.Add(BuildHit(pick.Info, owningEntry, processor));
        }

        return OrderByDrawingPriority(hits);
    }

    /// <summary>
    /// Orders each dataset's hits as S-98 15.1 asks (S-164
    /// FeatureInformation2): by drawing priority, highest first, then points,
    /// curves and surfaces (see <see cref="PickOrder"/>). Datasets keep the
    /// order of the layer stack, and hits that report no priority keep their
    /// order after the dataset's drawn ones.
    /// </summary>
    internal static List<PickHit> OrderByDrawingPriority(List<PickHit> hits)
    {
        var datasetOrder = new Dictionary<object, int>();
        int DatasetIndex(PickHit hit)
        {
            object key = (object?)hit.OwningProcessor ?? hit.DatasetFileName ?? string.Empty;
            if (!datasetOrder.TryGetValue(key, out var index))
                datasetOrder[key] = index = datasetOrder.Count;
            return index;
        }

        // OrderBy is stable, so equal hits keep the stack order.
        return hits
            .Select(hit => (Hit: hit, Dataset: DatasetIndex(hit)))
            .OrderBy(h => h.Dataset)
            .ThenBy(h => h.Hit, Comparer<PickHit>.Create((a, b) =>
                a.DrawingPriority is null && b.DrawingPriority is null
                    ? 0
                    : PickOrder.Compare(a.DrawingPriority, a.Geometry, b.DrawingPriority, b.Geometry)))
            .Select(h => h.Hit)
            .ToList();
    }

    /// <summary>
    /// Wraps a processor <see cref="FeatureInfo"/> into a <see cref="PickHit"/>,
    /// including a viewer-side <see cref="StationTimeSeriesViewModel"/> when
    /// the underlying info carries a station snapshot.
    /// </summary>
    private PickHit BuildHit(FeatureInfo info, DatasetEntry entry, IDatasetProcessor processor) =>
        BuildHit(info, entry.DisplayName, processor, entry.NotForNavigation, entry.Credit);

    // A feature opened by dataset name (the warnings panel, reference
    // navigation) finds its entry by name for the not-for-navigation flag.
    private PickHit BuildHit(FeatureInfo info, string? datasetFileName, IDatasetProcessor processor) =>
        BuildHit(
            info,
            datasetFileName,
            processor,
            _loader.EntryLayers.Keys.Any(e => e.NotForNavigation
                && string.Equals(e.DisplayName, datasetFileName, StringComparison.Ordinal)));

    private PickHit BuildHit(FeatureInfo info, string? datasetFileName, IDatasetProcessor processor, bool notForNavigation, string? credit = null)
    {
        return new PickHit
        {
            FeatureType = info.FeatureType,
            FeatureTypeName = info.FeatureTypeName,
            FeatureRef = info.FeatureRef,
            DatasetFileName = datasetFileName,
            ProductSpec = processor.Spec.Name,
            Attributes = info.Attributes,
            References = info.References,
            DrawingPriority = info.DrawingPriority,
            Geometry = info.Geometry,
            OwningProcessor = processor,
            StationSeries = BuildStationSeriesViewModel(info, processor),
            EggCode = info.EggCode,
            NavigationalWarning = info.NavigationalWarning,
            NotForNavigation = notForNavigation,
            Credit = credit,
        };
    }

    private StationTimeSeriesViewModel? BuildStationSeriesViewModel(FeatureInfo info, IDatasetProcessor processor)
    {
        if (info.StationSeries is not { } snapshot)
            return null;

        // Per-product viewmodel selection mirrors the pipeline shape: S-104
        // exposes a single height channel; S-111 exposes speed + direction.
        return processor.Spec.Name switch
        {
            "S-104" => new S104StationTimeSeriesViewModel(snapshot, _globalTime, _timeFormat, _themeService),
            "S-111" => new S111StationTimeSeriesViewModel(snapshot, _globalTime, _timeFormat, _themeService),
            _ => null,
        };
    }

    public bool NavigateToReference(FeatureReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        using var __cmd = ViewerObservability.BeginCommand("reference.navigate");

        // Resolve the owning processor of the currently selected hit and
        // re-query it for the target ref. Cross-dataset hops are out of
        // scope for milestone 3 — they slot into the search milestone
        // where a global feature snapshot already exists.
        var selected = _pickReport.SelectedHit;
        if (selected?.OwningProcessor is not { } processor)
        {
            __cmd.SetStatus(false, "no selected hit owns a processor");
            return false;
        }

        __cmd.SetTag("s100.viewer.product_spec", processor.Spec.Name);
        __cmd.SetTag("s100.viewer.reference.role", reference.Role);

        var ok = OpenFeature(processor, reference.TargetRef, selected.DatasetFileName ?? string.Empty);
        if (!ok)
        {
            __cmd.SetStatus(false, "target ref not found");
        }
        return ok;
    }

    public bool OpenFeature(IDatasetProcessor processor, string featureRef, string datasetFileName)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(featureRef);
        ArgumentNullException.ThrowIfNull(datasetFileName);

        var info = processor.GetFeatureInfo(featureRef);
        return PublishFeatureInfo(processor, info, datasetFileName);
    }

    public bool OpenFeatureAt(IDatasetProcessor processor, int ordinal, string datasetFileName)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(datasetFileName);

        var info = processor.GetFeatureInfoAt(ordinal);
        return PublishFeatureInfo(processor, info, datasetFileName);
    }

    private bool PublishFeatureInfo(IDatasetProcessor processor, FeatureInfo? info, string datasetFileName)
    {
        if (info is null)
            return false;

        _pickReport.SetPicks(new[] { BuildHit(info, datasetFileName, processor) });
        return true;
    }

    private static bool TryResolveOwner(
        IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> processors,
        MapDatasetId datasetId,
        out DatasetEntry owningEntry,
        out IDatasetProcessor processor)
    {
        foreach (var (entry, proc) in processors)
        {
            if (entry.Id == datasetId)
            {
                owningEntry = entry;
                processor = proc;
                return true;
            }
        }

        owningEntry = null!;
        processor = null!;
        return false;
    }

    private bool TryCoveragePick(double lat, double lon, out PickHit hit)
    {
        hit = null!;
        var time = _globalTime?.CurrentTime;

        using var processors = _loader.AcquireProcessors();
        foreach (var (entry, processor) in processors)
        {
            FeatureInfo? info;
            try
            {
                info = processor.GetCoverageInfo(lat, lon, time);
            }
            catch
            {
                // Coverage sampling can throw on malformed grids; treat
                // failures as "no coverage hit here" rather than killing
                // the click.
                continue;
            }

            if (info is null)
                continue;

            hit = BuildHit(info, entry, processor);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the location depth-assimilation card for a pick when the point is
    /// classified as water and a base depth resolves. Returns <c>null</c> on
    /// land, off-coverage, or when no depth source overlaps — in which cases the
    /// pick report simply omits the card.
    /// </summary>
    private EncDotNet.S100.Viewer.ViewModels.DepthOverTimeViewModel? BuildLocationDepth(
        IReadOnlyList<PickHit> hits,
        double latitude,
        double longitude)
    {
        using var processors = _loader.AcquireProcessors();
        // The water level comes from the passage check's source for the same
        // loaded datasets, so the card and Check passage give one answer (#1132).
        var waterLevel = _catalog is not null && _routeSources is not null
            ? _routeSources.Get(_catalog.Datasets).WaterLevel
            : null;
        // The "Apply anyway when datum is not declared" override applies to the
        // card as it does to the chart, so the two agree (#1132 Q2).
        var probe = _depthProbe.Evaluate(
            processors, hits, latitude, longitude, waterLevel,
            _waterLevelSettings?.WaterLevelApplyUndeclaredDatum ?? false);
        if (probe.Class != EncDotNet.S100.Viewer.Services.Depth.WaterLandClass.Water || probe.Depth is not { } result)
            return null;

        var settings = _marinerSettings?.Current;
        var depthUnit = settings?.DepthUnit ?? EncDotNet.S100.Pipelines.DepthUnit.Metres;
        double? safetyDepthMetres = settings?.SafetyDepth.TotalMetres;
        var label = LatLonFormatter.FormatDecimal(latitude, longitude);

        return new EncDotNet.S100.Viewer.ViewModels.DepthOverTimeViewModel(
            result,
            label,
            latitude,
            longitude,
            depthUnit,
            safetyDepthMetres,
            _globalTime,
            _timeFormat,
            _themeService);
    }

    private static bool Contains<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Equals(list[i], item))
                return true;
        }
        return false;
    }
}
