using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

internal sealed class StubFeatureSearchService : IFeatureSearchService
{
    public (IReadOnlyList<FeatureSearchHit> Hits, int TotalMatched) Search(string? query, int limit)
        => (System.Array.Empty<FeatureSearchHit>(), 0);
}

/// <summary>
/// A pick service that resolves nothing; <see cref="Publish"/> raises
/// <see cref="Picked"/> as a pick would.
/// </summary>
internal sealed class StubPickService : IPickService
{
    public event EventHandler<PickResult>? Picked;

    /// <summary>Publishes <paramref name="result"/> as a pick would.</summary>
    public void Publish(PickResult result) => Picked?.Invoke(this, result);

    public System.Threading.Tasks.Task HandlePickAsync(Mapsui.MPoint? worldPosition, double resolution, System.Collections.Generic.IReadOnlyList<EncDotNet.S100.Viewer.ViewModels.DynamicPickHit>? dynamicHits = null) => System.Threading.Tasks.Task.CompletedTask;

    public void PresentGeographicPick(
        double latitude,
        double longitude,
        System.Collections.Generic.IReadOnlyList<EncDotNet.S100.Viewer.Services.GeographicPickFeature> features)
    { }

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
