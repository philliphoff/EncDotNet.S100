using System.Collections.Generic;
using EncDotNet.S100.Viewer.Services;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

internal sealed class StubFeatureSearchService : IFeatureSearchService
{
    public (IReadOnlyList<FeatureSearchHit> Hits, int TotalMatched) Search(string? query, int limit)
        => (System.Array.Empty<FeatureSearchHit>(), 0);
}

internal sealed class StubPickService : IPickService
{
    public void Attach(EncDotNet.S100.Viewer.ViewModels.MainViewModel viewModel) { }

    public void HandlePick(MapInfo? mapInfo) { }

    public bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference) => false;

    public bool OpenFeature(
        EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor processor,
        string featureRef,
        string datasetFileName) => false;
}
