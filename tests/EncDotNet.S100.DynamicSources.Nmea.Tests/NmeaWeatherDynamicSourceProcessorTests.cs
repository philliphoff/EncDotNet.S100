using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Renderers.Skia.Scene;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.DynamicSources.Nmea.Tests;

/// <summary>
/// Own-ship weather from NMEA as a dataset processor, through
/// <see cref="DynamicSourceProcessor"/>: the feature, its description, the
/// portrayal on its plane, a headless scene and live changes.
/// </summary>
public sealed class NmeaWeatherDynamicSourceProcessorTests : IAsyncLifetime
{
    private static readonly SpecRef Spec = new(SpecName.Nmea, default);

    private readonly FakeTimeProvider _time = new(NmeaWeatherSourceTests.Start);
    private NmeaWeatherSource? _source;
    private DynamicSourceProcessor? _processor;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _processor?.Dispose();
        if (_source is not null)
            await _source.DisposeAsync();
    }

    private async Task<(NmeaWeatherSource Source, DynamicSourceProcessor Processor)> ReplayAsync()
    {
        _source = new NmeaWeatherSource(
            input: new NmeaReplayFile(NmeaWeatherSourceTests.FixturePath("ownship-weather.nmea")),
            options: null,
            timeProvider: _time);
        _processor = new DynamicSourceProcessor(_source, OwnShipWeatherPortrayal.Instance, Spec);
        await _source.ReadToEndAsync(TestContext.Current.CancellationToken);
        return (_source, _processor);
    }

    [Fact]
    public async Task The_own_ship_weather_feature_is_picked_and_described()
    {
        var (source, processor) = await ReplayAsync();
        var feature = Assert.Single(source.CurrentFeatures);
        var position = feature.Coordinates[0];

        var hit = Assert.Single(processor.HitTestFeatures(position.Latitude, position.Longitude, 100));
        var info = processor.GetFeatureInfo(hit.FeatureRef);

        Assert.NotNull(info);
        var expected = source.Describe(feature)!;
        Assert.Equal(expected.Title, info!.FeatureTypeName);
        Assert.Equal(expected.Properties.Select(p => p.Value), info.Attributes.Select(a => a.RawValue));
    }

    [Fact]
    public async Task The_portrayal_draws_on_the_sources_plane_and_builds_a_headless_scene()
    {
        var (_, processor) = await ReplayAsync();

        var result = await processor.BuildVectorPortrayalAsync(cancellationToken: TestContext.Current.CancellationToken);

        var layer = Assert.Single(result.SubLayers);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, layer.Plane);
        Assert.Equal(110, layer.WithinPlanePriority);
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
    public async Task New_sentences_are_raised_as_a_dataset_change()
    {
        var (source, processor) = await ReplayAsync();
        var changes = new List<DatasetChangedEventArgs>();
        ((ILiveDatasetProcessor)processor).Changed += (_, e) => changes.Add(e);

        Assert.True(source.ProcessLine(NmeaSentence.Format("IIMTW,15.5,C")));
        source.Refresh();

        var change = Assert.Single(changes);
        Assert.Equal([Assert.Single(source.CurrentFeatures).Id], change.FeatureRefs!);
    }
}
