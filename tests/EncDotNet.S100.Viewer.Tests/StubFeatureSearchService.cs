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
    public void HandlePick(MapInfo? mapInfo) { }
    public void HandlePick(MapInfo? mapInfo, System.Collections.Generic.IReadOnlyList<EncDotNet.S100.Viewer.ViewModels.DynamicPickHit>? dynamicHits = null) { }

    public bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference) => false;

    public bool OpenFeature(
        EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor processor,
        string featureRef,
        string datasetFileName) => false;

    public bool OpenFeatureAt(
        EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor processor,
        int ordinal,
        string datasetFileName) => false;
}
