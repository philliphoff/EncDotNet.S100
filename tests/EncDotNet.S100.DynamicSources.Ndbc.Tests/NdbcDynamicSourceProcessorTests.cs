using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia.Scene;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

/// <summary>
/// The NDBC observation source as a dataset processor, through
/// <see cref="DynamicSourceProcessor"/>: picks, descriptions, the portrayal on
/// its plane, a headless scene and live changes.
/// </summary>
public sealed class NdbcDynamicSourceProcessorTests : IAsyncLifetime
{
    private static readonly BoundingBox NewEngland = new(40.5, -71.5, 43.0, -69.5);
    private static readonly SpecRef Spec = new(SpecName.Ndbc, default);

    private readonly HttpClient _http = new(FakeNdbcHandler.WithFixtures());
    private readonly FakeTimeProvider _time = new(NdbcFixtures.NewestTime(NdbcFixtures.Buoy) + TimeSpan.FromMinutes(10));
    private readonly NdbcObservationSource _source;
    private readonly DynamicSourceProcessor _processor;

    public NdbcDynamicSourceProcessorTests()
    {
        _source = new NdbcObservationSource(_http, timeProvider: _time, options: null);
        _processor = new DynamicSourceProcessor(_source, NdbcStationPortrayal.Instance, Spec);
    }

    public async ValueTask InitializeAsync()
    {
        _source.UpdateArea(NewEngland);
        await _source.PollAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _processor.Dispose();
        await _source.DisposeAsync();
        _http.Dispose();
    }

    [Fact]
    public void The_stations_are_the_datasets_features()
    {
        var features = _processor.EnumerateFeatures().ToList();

        Assert.Equal(["ndbc:44013", "ndbc:BUZM3"], features.Select(f => f.FeatureRef));
        Assert.All(features, f => Assert.StartsWith(NdbcObservationSource.FeatureKind, f.FeatureType, StringComparison.Ordinal));
        Assert.NotNull(_processor.Metadata.Extent);
    }

    [Fact]
    public void A_pick_at_a_buoy_finds_it_and_describes_it_with_the_sources_describer()
    {
        // 44013 is at 42.346 N 70.651 W.
        var hit = Assert.Single(_processor.HitTestFeatures(42.346, -70.651, 500));
        Assert.Equal("ndbc:44013", hit.FeatureRef);

        var info = _processor.GetFeatureInfo(hit.FeatureRef);

        Assert.NotNull(info);
        var expected = _source.Describe(_source.CurrentFeatures.Single(f => f.Id == hit.FeatureRef))!;
        Assert.Equal(expected.Title, info!.FeatureTypeName);
        Assert.Equal(expected.Properties.Select(p => p.Label), info.Attributes.Select(a => a.Name));
        Assert.Null(_processor.GetFeatureInfoAt(hit.Ordinal));
    }

    [Fact]
    public async Task The_portrayal_draws_on_the_sources_plane_and_builds_a_headless_scene()
    {
        var result = await _processor.BuildVectorPortrayalAsync(cancellationToken: TestContext.Current.CancellationToken);

        var layer = Assert.Single(result.SubLayers);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, layer.Plane);
        Assert.Equal(100, layer.WithinPlanePriority);
        Assert.NotEmpty(layer.Instructions);

        var scene = HeadlessVectorRenderer.BuildScene(
            layer.Instructions,
            result.GeometryProvider,
            result.Palette,
            result.SymbolProvider,
            result.LineStyleProvider,
            result.SymbolScale,
            result.TextScale);
        Assert.NotEmpty(scene.Ops);
    }

    [Fact]
    public void Observations_going_stale_are_raised_as_a_dataset_change()
    {
        var changes = new List<DatasetChangedEventArgs>();
        ((ILiveDatasetProcessor)_processor).Changed += (_, e) => changes.Add(e);

        _time.Advance(TimeSpan.FromHours(3));
        _source.Age();

        // Re-ageing republishes the whole snapshot, which reaches the map as
        // a change that may touch any feature.
        Assert.Null(Assert.Single(changes).FeatureRefs);
    }

    [Fact]
    public void Disposing_the_processor_stops_forwarding_changes()
    {
        var changes = 0;
        ((ILiveDatasetProcessor)_processor).Changed += (_, _) => changes++;

        _processor.Dispose();
        _time.Advance(TimeSpan.FromHours(3));
        _source.Age();

        Assert.Equal(0, changes);
    }
}
