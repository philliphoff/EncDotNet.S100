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
/// <see cref="IDatasetLoaderService"/> and publishes the resolved features as
/// a <see cref="PickResult"/> (<see cref="Picked"/>), which Object Information
/// shows. Failed reference navigations are surfaced as a transient
/// notification.
/// </summary>
/// <remarks>
/// A click is picked through the map session's engine pick
/// (<see cref="IDatasetLoaderService.PickAsync"/>): each drawn dataset's own
/// spatial index, ranked topmost-first by the S-98 stack, not one Mapsui
/// feature per drawn op (map rendering slice&#160;8). This service resolves
/// each pick into a <see cref="PickHit"/>, deduplicates by (processor,
/// feature ref), and publishes the list.
///
/// Depends only on small, leaf-level singletons, never on a view model (#1401),
/// so the dependency graph stays acyclic.
/// </remarks>
internal sealed class PickService : IPickService
{
    private readonly IDatasetLoaderService _loader;
    private readonly INotificationService _notifications;
    private readonly ObjectInformationSelection _selection;
    private readonly GlobalTimeService _globalTime;
    private readonly EncDotNet.S100.Viewer.Services.Depth.LocationDepthProbe _depthProbe = new();
    private readonly ViewerDatasetCatalog _catalog;
    private readonly EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache _routeSources;
    private readonly EncDotNet.S100.Viewer.Services.Depth.IWaterLevelAdjustmentSettings _waterLevelSettings;

    /// <summary>Creates the pick service.</summary>
    /// <param name="loader">The loaded datasets, their processors and the engine pick.</param>
    /// <param name="notifications">Shows the "reference not found" toast.</param>
    /// <param name="selection">The hit Object Information shows, whose references are followed.</param>
    /// <param name="globalTime">The Timeline's time, at which coverages are sampled.</param>
    /// <param name="catalog">The loaded datasets the depth card's water level comes from.</param>
    /// <param name="routeSources">The passage check's sources, so the depth card and Check passage agree (#1132).</param>
    /// <param name="waterLevelSettings">Whether water level applies when the datum is not declared.</param>
    public PickService(
        IDatasetLoaderService loader,
        INotificationService notifications,
        ObjectInformationSelection selection,
        GlobalTimeService globalTime,
        ViewerDatasetCatalog catalog,
        EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache routeSources,
        EncDotNet.S100.Viewer.Services.Depth.IWaterLevelAdjustmentSettings waterLevelSettings)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(globalTime);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(routeSources);
        ArgumentNullException.ThrowIfNull(waterLevelSettings);
        _loader = loader;
        _notifications = notifications;
        _selection = selection;
        _globalTime = globalTime;
        _catalog = catalog;
        _routeSources = routeSources;
        _waterLevelSettings = waterLevelSettings;
    }

    /// <inheritdoc />
    public event EventHandler<PickResult>? Picked;

    private void Publish(PickResult result) => Picked?.Invoke(this, result);

    public async Task HandlePickAsync(MPoint? worldPosition, double resolution, IReadOnlyList<DynamicPickHit>? dynamicHits = null)
    {
        using var __cmd = ViewerObservability.BeginCommand("pick");

        var dynamic = dynamicHits ?? Array.Empty<DynamicPickHit>();

        if (worldPosition is null)
        {
            // Even without a position a pick may still carry dynamic hits;
            // honour them if present, otherwise clear.
            Publish(new PickResult([], dynamic));
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
                PickHit[] coverageHits = [coverageHit];
                Publish(new PickResult(coverageHits, [], location, BuildLocationDepth(coverageHits, lat, lon)));
                return;
            }

            // An empty-map tap, or a hit on a dataset no loaded entry owns.
            // Either way, hide the panel so it doesn't keep showing stale state.
            Publish(PickResult.Empty);
            return;
        }

        Publish(new PickResult(hits, dynamic, location, BuildLocationDepth(hits, lat, lon)));
    }

    /// <summary>The on-screen pick tolerance, in DIP, around the click.</summary>
    internal const double PickTolerancePx = 6.0;

    /// <summary>
    /// Publishes a pre-resolved geographic pick (the MCP <c>pick_features</c>
    /// path). Each feature is re-queried against its owning dataset's
    /// processor so the panel shows full FC-decoded attributes; entries that
    /// no longer resolve are skipped. The pick highlight follows because it
    /// observes Object Information, which shows the result.
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
            Publish(PickResult.Empty);
            return;
        }

        Publish(new PickResult(hits, [], new PickLocation(latitude, longitude), BuildLocationDepth(hits, latitude, longitude)));
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
    /// including the station snapshot of an S-104 or S-111 station, which
    /// Object Information charts.
    /// </summary>
    private PickHit BuildHit(FeatureInfo info, DatasetEntry entry, IDatasetProcessor processor) =>
        BuildHit(info, entry.DisplayName, processor, entry.NotForNavigation, entry.Credit, entry.Id.Value);

    // A feature opened by dataset name (the warnings panel, reference
    // navigation) finds its entry by name for the not-for-navigation flag
    // and the id its Legend row is found by (#1410).
    private PickHit BuildHit(FeatureInfo info, string? datasetFileName, IDatasetProcessor processor)
    {
        var named = _loader.EntryLayers.Keys
            .Where(e => string.Equals(e.DisplayName, datasetFileName, StringComparison.Ordinal))
            .ToList();
        return BuildHit(
            info,
            datasetFileName,
            processor,
            named.Any(e => e.NotForNavigation),
            credit: null,
            datasetId: named.Count == 1 ? named[0].Id.Value : null);
    }

    private PickHit BuildHit(FeatureInfo info, string? datasetFileName, IDatasetProcessor processor, bool notForNavigation, string? credit, string? datasetId)
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
            StationSeries = processor.Spec.Name is "S-104" or "S-111" ? info.StationSeries : null,
            EggCode = info.EggCode,
            NavigationalWarning = info.NavigationalWarning,
            NotForNavigation = notForNavigation,
            Credit = credit,
            DatasetId = datasetId,
            Aml = processor is AmlDatasetProcessor aml ? AmlPickProvenance.From(aml, info) : null,
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
        var selected = _selection.Hit;
        if (selected?.OwningProcessor is not { } processor)
        {
            __cmd.SetStatus(false, "no selected hit owns a processor");
            ShowReferenceNotFound(reference);
            return false;
        }

        __cmd.SetTag("s100.viewer.product_spec", processor.Spec.Name);
        __cmd.SetTag("s100.viewer.reference.role", reference.Role);

        var ok = OpenFeature(processor, reference.TargetRef, selected.DatasetFileName ?? string.Empty);
        if (!ok)
        {
            __cmd.SetStatus(false, "target ref not found");
            ShowReferenceNotFound(reference);
        }
        return ok;
    }

    // Says the click registered but the target is missing.
    private void ShowReferenceNotFound(FeatureReference reference) =>
        _notifications.Create(Strings.Toast_Warning)
            .WithSeverity(NotificationSeverity.Warning)
            .WithContent(string.Format(
                Strings.Status_FeatureRefNotFound,
                reference.TargetRef))
            .Show();

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

        Publish(new PickResult([BuildHit(info, datasetFileName, processor)], []));
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
        var time = _globalTime.CurrentTime;

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
    /// Resolves the depth over time at a pick when the point is classified as
    /// water and a base depth resolves. Returns <c>null</c> on land,
    /// off-coverage, or when no depth source overlaps; Object Information then
    /// shows no depth card.
    /// </summary>
    private EncDotNet.S100.Viewer.Services.Depth.LocationDepthResult? BuildLocationDepth(
        IReadOnlyList<PickHit> hits,
        double latitude,
        double longitude)
    {
        // AML never feeds depth answers (#1372, design decision 13): its
        // features keep S-57 classes, but a CLB depth area must not reach the card.
        hits = WithoutAml(hits);
        using var processors = _loader.AcquireProcessors();
        // The water level comes from the passage check's source for the same
        // loaded datasets, so the card and Check passage give one answer (#1132).
        var waterLevel = _routeSources.Get(_catalog.Datasets).WaterLevel;
        // The "Apply anyway when datum is not declared" override applies to the
        // card as it does to the chart, so the two agree (#1132 Q2).
        var probe = _depthProbe.Evaluate(
            processors, hits, latitude, longitude, waterLevel,
            _waterLevelSettings.WaterLevelApplyUndeclaredDatum);
        return probe.Class == EncDotNet.S100.Viewer.Services.Depth.WaterLandClass.Water ? probe.Depth : null;
    }

    /// <summary>The hits that may feed the Depth card: every hit but those from AML cells.</summary>
    internal static IReadOnlyList<PickHit> WithoutAml(IReadOnlyList<PickHit> hits) =>
        hits.Any(h => h.Aml is not null || h.OwningProcessor is AmlDatasetProcessor)
            ? hits.Where(h => h.Aml is null && h.OwningProcessor is not AmlDatasetProcessor).ToList()
            : hits;

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
