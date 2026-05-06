using System;
using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IPickService"/> implementation: locates the dataset
/// entry and processor that own each hit feature via
/// <see cref="IDatasetLoaderService"/> and pushes the resolved feature list
/// into <see cref="MainViewModel.PickReport"/> /
/// <see cref="MainViewModel.StatusText"/>.
/// </summary>
/// <remarks>
/// Mapsui's <c>MapInfo</c> exposes every overlapping feature at the click
/// location through <see cref="MapInfo.MapInfoRecords"/>; this service
/// resolves each record into a <see cref="PickHit"/>, deduplicates by
/// (processor, feature ref) so a feature drawn into multiple style layers
/// only appears once, and presents the deduped list to the panel.
/// </remarks>
internal sealed class PickService : IPickService
{
    private readonly IDatasetLoaderService _loader;
    private readonly MainViewModel _viewModel;

    public PickService(IDatasetLoaderService loader, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(viewModel);
        _loader = loader;
        _viewModel = viewModel;

        // Bridge the panel's Navigate command back into this service.
        // Failures surface as a transient status-bar message so the user
        // knows the click registered but the target is missing.
        _viewModel.PickReport.SetNavigateHandler(reference =>
        {
            if (!NavigateToReference(reference))
            {
                _viewModel.StatusText = string.Format(
                    Resources.Strings.Status_FeatureRefNotFound,
                    reference.TargetRef);
            }
        });
    }

    public void HandlePick(MapInfo? mapInfo)
    {
        if (mapInfo is null)
        {
            _viewModel.PickReport.Clear();
            return;
        }

        var hits = ResolveHits(mapInfo);
        if (hits.Count == 0)
        {
            // Either an empty-map tap or a hit on a feature whose layer
            // isn't owned by any loaded dataset entry. Either way, hide
            // the panel so it doesn't keep showing stale state.
            _viewModel.PickReport.Clear();
            return;
        }

        _viewModel.PickReport.SetPicks(hits);

        // Status text follows the first (selected) hit, with a "+N more"
        // suffix when additional features were resolved.
        var first = hits[0];
        var primary = string.Format(
            Strings.Status_FeatureSummary,
            first.FeatureTypeName ?? first.FeatureType,
            first.FeatureRef);
        _viewModel.StatusText = hits.Count > 1
            ? string.Format(Strings.Status_FeatureSummaryWithMore, primary, hits.Count - 1)
            : primary;
    }

    private List<PickHit> ResolveHits(MapInfo mapInfo)
    {
        var hits = new List<PickHit>();
        var seen = new HashSet<(IDatasetProcessor processor, string featureRef)>();

        foreach (var record in mapInfo.MapInfoRecords)
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

            hits.Add(new PickHit
            {
                FeatureType = info.FeatureType,
                FeatureTypeName = info.FeatureTypeName,
                FeatureRef = info.FeatureRef,
                DatasetFileName = owningEntry.DisplayName,
                ProductSpec = processor.ProductSpec,
                Attributes = info.Attributes,
                References = info.References,
                OwningProcessor = processor,
            });
        }

        return hits;
    }

    public bool NavigateToReference(FeatureReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // Resolve the owning processor of the currently selected hit and
        // re-query it for the target ref. Cross-dataset hops are out of
        // scope for milestone 3 — they slot into the search milestone
        // where a global feature snapshot already exists.
        var selected = _viewModel.PickReport.SelectedHit;
        if (selected?.OwningProcessor is not { } processor)
            return false;

        var info = processor.GetFeatureInfo(reference.TargetRef);
        if (info is null)
            return false;

        _viewModel.PickReport.SetPicks(new[]
        {
            new PickHit
            {
                FeatureType = info.FeatureType,
                FeatureTypeName = info.FeatureTypeName,
                FeatureRef = info.FeatureRef,
                DatasetFileName = selected.DatasetFileName,
                ProductSpec = selected.ProductSpec,
                Attributes = info.Attributes,
                References = info.References,
                OwningProcessor = processor,
            },
        });

        _viewModel.StatusText = string.Format(
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
