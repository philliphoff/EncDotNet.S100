using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class DispatcherGeographicPickPresenterTests
{
    private sealed class CapturingPickService : IPickService
    {
        public event EventHandler<PickResult>? Picked { add { } remove { } }

        public (double Lat, double Lon, IReadOnlyList<GeographicPickFeature> Features, MapViewId? Map)? Presented { get; private set; }

        public void PresentGeographicPick(
            double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features, IReadOnlyList<EncDotNet.S100.Viewer.ViewModels.PickHit> overlayHits, MapViewId? map)
            => Presented = (latitude, longitude, features, map);

        // Unused members for this test.
        public System.Threading.Tasks.Task HandlePickAsync(MapViewId map, Mapsui.MPoint? worldPosition, double resolution, System.Collections.Generic.IReadOnlyList<EncDotNet.S100.Viewer.ViewModels.PickHit>? dynamicHits = null) => System.Threading.Tasks.Task.CompletedTask;
        public bool NavigateToReference(FeatureReference reference) => false;
        public bool OpenFeature(IDatasetProcessor processor, string featureRef, string datasetFileName) => false;
        public bool OpenFeatureAt(IDatasetProcessor processor, int ordinal, string datasetFileName) => false;
    }

    [Fact]
    public void Present_MarshalsThenForwardsToPickService()
    {
        var pickService = new CapturingPickService();
        var marshalled = false;
        var presenter = new DispatcherGeographicPickPresenter(
            pickService,
            marshal: action => { marshalled = true; action(); },
            dynamicPicks: null,
            resolution: () => null);

        var features = new[] { new GeographicPickFeature("test.gml", "1") };
        _ = presenter.Present(47.6, -122.3, features, new MapViewId("map-2"));

        Assert.True(marshalled);
        Assert.NotNull(pickService.Presented);
        Assert.Equal(47.6, pickService.Presented!.Value.Lat, 6);
        Assert.Equal(-122.3, pickService.Presented!.Value.Lon, 6);
        Assert.Same(features, pickService.Presented!.Value.Features);
        // The map picked in travels with the pick (#1763).
        Assert.Equal(new MapViewId("map-2"), pickService.Presented!.Value.Map);
    }
}
