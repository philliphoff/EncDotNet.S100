using System;
using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class DispatcherGeographicPickPresenterTests
{
    private sealed class CapturingPickService : IPickService
    {
        public (double Lat, double Lon, IReadOnlyList<GeographicPickFeature> Features)? Presented { get; private set; }

        public void PresentGeographicPick(
            double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features)
            => Presented = (latitude, longitude, features);

        // Unused members for this test.
        public void HandlePick(Mapsui.MapInfo? mapInfo, IReadOnlyList<DynamicPickHit>? dynamicHits = null) { }
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
            marshal: action => { marshalled = true; action(); });

        var features = new[] { new GeographicPickFeature("test.gml", "1") };
        presenter.Present(47.6, -122.3, features);

        Assert.True(marshalled);
        Assert.NotNull(pickService.Presented);
        Assert.Equal(47.6, pickService.Presented!.Value.Lat, 6);
        Assert.Equal(-122.3, pickService.Presented!.Value.Lon, 6);
        Assert.Same(features, pickService.Presented!.Value.Features);
    }
}
