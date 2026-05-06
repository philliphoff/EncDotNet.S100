using System;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IPickService"/> implementation: locates the dataset
/// entry and processor that own a hit feature via
/// <see cref="IDatasetLoaderService"/> and pushes the resolved feature
/// information into <see cref="MainViewModel.PickReport"/> /
/// <see cref="MainViewModel.StatusText"/>.
/// </summary>
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
    }

    public void HandlePick(MapInfo? mapInfo)
    {
        using var __cmd = ViewerObservability.BeginCommand("pick");

        if (mapInfo?.Feature is not { } hitFeature || mapInfo.Layer is not { } hitLayer)
        {
            // Tap on empty map: clear any active pick so the panel hides.
            _viewModel.PickReport.Clear();
            return;
        }

        if (hitFeature[MapsuiDisplayListRenderer.FeatureRefKey] is not string featureRef)
            return;

        // Find which dataset entry owns the hit layer
        DatasetEntry? owningEntry = null;
        foreach (var (entry, layers) in _loader.EntryLayers)
        {
            if (Contains(layers, hitLayer))
            {
                owningEntry = entry;
                break;
            }
        }

        if (owningEntry is null || !_loader.Processors.TryGetValue(owningEntry, out var processor))
            return;

        var info = processor.GetFeatureInfo(featureRef);
        if (info is null)
        {
            _viewModel.PickReport.Clear();
            _viewModel.StatusText = string.Format(Strings.Status_FeatureNoDetails, featureRef);
            return;
        }

        _viewModel.PickReport.SetPick(
            featureType: info.FeatureType,
            featureRef: info.FeatureRef,
            datasetFileName: owningEntry.DisplayName,
            productSpec: processor.ProductSpec,
            attributes: info.Attributes);

        _viewModel.StatusText = string.Format(Strings.Status_FeatureSummary, info.FeatureType, info.FeatureRef);
    }

    private static bool Contains<T>(System.Collections.Generic.IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Equals(list[i], item))
                return true;
        }
        return false;
    }
}
