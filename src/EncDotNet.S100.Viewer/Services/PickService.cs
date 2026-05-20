using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Rendering;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IPickService"/> implementation: locates the dataset
/// entry and processor that own each hit feature via
/// <see cref="IDatasetLoaderService"/> and pushes the resolved feature list
/// into <see cref="PickReportViewModel"/> + <see cref="IStatusPresenter"/>.
/// </summary>
/// <remarks>
/// Mapsui's <c>MapInfo</c> exposes every overlapping feature at the click
/// location through <see cref="MapInfo.MapInfoRecords"/>; this service
/// resolves each record into a <see cref="PickHit"/>, deduplicates by
/// (processor, feature ref) so a feature drawn into multiple style layers
/// only appears once, and presents the deduped list to the panel.
///
/// Depends only on small, leaf-level singletons — never on the root
/// <c>MainViewModel</c> — so the dependency graph stays acyclic.
/// </remarks>
internal sealed class PickService : IPickService
{
    private readonly IDatasetLoaderService _loader;
    private readonly PickReportViewModel _pickReport;
    private readonly IStatusPresenter _status;
    private readonly GlobalTimeService? _globalTime;
    private readonly ITimeFormatProvider? _timeFormat;

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        IStatusPresenter status)
        : this(loader, pickReport, status, globalTime: null, timeFormat: null)
    {
    }

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        IStatusPresenter status,
        GlobalTimeService? globalTime)
        : this(loader, pickReport, status, globalTime, timeFormat: null)
    {
    }

    public PickService(
        IDatasetLoaderService loader,
        PickReportViewModel pickReport,
        IStatusPresenter status,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(status);
        _loader = loader;
        _pickReport = pickReport;
        _status = status;
        _globalTime = globalTime;
        _timeFormat = timeFormat;

        // The pick-report VM raises NavigateRequested when the user
        // clicks a row in the References list. Failures surface as a
        // transient status-bar message so the user knows the click
        // registered but the target is missing.
        _pickReport.NavigateRequested += (_, reference) =>
        {
            if (!NavigateToReference(reference))
            {
                _status.StatusText = string.Format(
                    Resources.Strings.Status_FeatureRefNotFound,
                    reference.TargetRef);
            }
        };
    }

    public void HandlePick(MapInfo? mapInfo)
    {
        using var __cmd = ViewerObservability.BeginCommand("pick");

        if (mapInfo is null)
        {
            _pickReport.Clear();
            return;
        }

        var hits = ResolveHits(mapInfo);
        if (hits.Count == 0)
        {
            // No vector hit. Try a coverage fallback before clearing —
            // S-102/S-104/S-111 processors expose
            // GetCoverageInfo(lat, lon) which samples the underlying
            // grid and returns a synthesised feature.
            if (TryCoveragePick(mapInfo, out var coverageHit))
            {
                _pickReport.SetPicks(new[] { coverageHit });
                _status.StatusText = string.Format(
                    Strings.Status_FeatureSummary,
                    coverageHit.FeatureTypeName ?? coverageHit.FeatureType,
                    coverageHit.FeatureRef);
                return;
            }

            // Either an empty-map tap or a hit on a feature whose layer
            // isn't owned by any loaded dataset entry. Either way, hide
            // the panel so it doesn't keep showing stale state.
            _pickReport.Clear();
            return;
        }

        _pickReport.SetPicks(hits);

        // Status text follows the first (selected) hit, with a "+N more"
        // suffix when additional features were resolved.
        var first = hits[0];
        var primary = string.Format(
            Strings.Status_FeatureSummary,
            first.FeatureTypeName ?? first.FeatureType,
            first.FeatureRef);
        _status.StatusText = hits.Count > 1
            ? string.Format(Strings.Status_FeatureSummaryWithMore, primary, hits.Count - 1)
            : primary;
    }

    private List<PickHit> ResolveHits(MapInfo mapInfo)
    {
        var hits = new List<PickHit>();
        var seen = new HashSet<(IDatasetProcessor processor, string featureRef)>();

        // PR-L1 (S-98): rank multi-hit picks by the current S-98 layer
        // stack so the user sees the topmost-painted feature first.
        // Mapsui's MapInfoRecords arrive in scene-graph order which
        // does not necessarily match the cross-dataset paint order
        // computed by InteroperabilityAuthority.Sort. Re-sort here.
        //
        // S-98 Main §10.12 and Annex A §A-6.12 designate interrogation
        // as "under development" — there is no normative pick-order
        // rule yet. Top-of-stack-first matches user expectation that
        // the visually frontmost feature gets reported first.
        var stack = _loader.CurrentStackedLayers;
        int StackIndex(ILayer? layer)
        {
            if (layer is null) return -1;
            for (int i = 0; i < stack.Count; i++)
                if (ReferenceEquals(stack[i], layer)) return i;
            return -1;
        }

        // Higher stack index = drawn later = on top. Descending order
        // means top-of-stack records are walked first; the dedup
        // HashSet then locks in the topmost hit for each (processor,
        // featureRef) key. A stable order-preserving sort keeps
        // intra-layer ordering as Mapsui returned it.
        var records = mapInfo.MapInfoRecords.ToList();
        records.Sort((a, b) => StackIndex(b.Layer).CompareTo(StackIndex(a.Layer)));

        foreach (var record in records)
        {
            if (record.Feature is not { } feature || record.Layer is not { } layer)
                continue;

            if (feature[MapsuiDisplayListRenderer.FeatureRefKey] is not string featureRef)
                continue;

            if (!TryResolveOwner(layer, out var owningEntry, out var processor))
                continue;

            // A single feature can appear in multiple style sublayers (line
            // + label + symbol). Deduplicate so the panel doesn't show
            // duplicate rows for the same logical feature.
            var key = (processor, featureRef);
            if (!seen.Add(key))
                continue;

            var info = processor.GetFeatureInfo(featureRef);
            if (info is null)
                continue;

            hits.Add(BuildHit(info, owningEntry.DisplayName, processor));
        }

        return hits;
    }

    /// <summary>
    /// Wraps a processor <see cref="FeatureInfo"/> into a <see cref="PickHit"/>,
    /// including a viewer-side <see cref="StationTimeSeriesViewModel"/> when
    /// the underlying info carries a station snapshot.
    /// </summary>
    private PickHit BuildHit(FeatureInfo info, string? datasetFileName, IDatasetProcessor processor)
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
            OwningProcessor = processor,
            StationSeries = BuildStationSeriesViewModel(info, processor),
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
            "S-104" => new S104StationTimeSeriesViewModel(snapshot, _globalTime, _timeFormat),
            "S-111" => new S111StationTimeSeriesViewModel(snapshot, _globalTime, _timeFormat),
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

        _status.StatusText = string.Format(
            Resources.Strings.Status_FeatureSummary,
            info.FeatureTypeName ?? info.FeatureType,
            info.FeatureRef);
        return true;
    }

    private bool TryResolveOwner(
        ILayer hitLayer,
        out DatasetEntry owningEntry,
        out IDatasetProcessor processor)
    {
        foreach (var (entry, layers) in _loader.EntryLayers)
        {
            if (!Contains(layers, hitLayer))
                continue;

            if (_loader.Processors.TryGetValue(entry, out var proc))
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

    private bool TryCoveragePick(MapInfo mapInfo, out PickHit hit)
    {
        hit = null!;
        if (mapInfo.WorldPosition is not { } world)
            return false;

        // SphericalMercator → WGS84. Mapsui returns (lon, lat).
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        var time = _globalTime?.CurrentTime;

        foreach (var (entry, processor) in _loader.Processors)
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

            hit = BuildHit(info, entry.DisplayName, processor);
            return true;
        }

        return false;
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
