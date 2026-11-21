using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>--source</c> on a one-shot command (data source design note D9, slice
/// 11d): parsed, started for the command's area, and left out when it has no
/// data in time.
/// </summary>
public sealed class OneShotSourceTests
{
    private static readonly GeographicBounds Harbour = new(-122.5, 47.5, -122.3, 47.7);

    [Fact]
    public void A_source_argument_is_an_id_then_name_value_pairs()
    {
        var argument = SourceArgument.TryParse("nmea.weather:Input=udp:10110, Station=44013", out var error);

        Assert.Null(error);
        Assert.Equal("nmea.weather", argument!.Id);
        Assert.Equal("udp:10110", argument.Options["Input"]);
        Assert.Equal("44013", argument.Options["Station"]);
        Assert.Empty(SourceArgument.TryParse("ndbc.realtime", out _)!.Options);

        Assert.Null(SourceArgument.TryParse("", out error));
        Assert.Contains("needs a source id", error, StringComparison.Ordinal);
        Assert.Null(SourceArgument.TryParse(":a=b", out error));
        Assert.Contains("no source id", error, StringComparison.Ordinal);
        Assert.Null(SourceArgument.TryParse("x:oops", out error));
        Assert.Contains("isn't name=value", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_with_data_is_a_dataset_and_one_without_is_left_out()
    {
        var (sources, error) = await OneShotSources.StartAsync(
            ["test.vessels", "test.quiet", "test.vessels"], Registry(), Harbour, TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(error);
        using (sources)
        {
            Assert.Equal(["test.vessels", "test.vessels-2"], sources!.Datasets.Select(d => d.Id.Value));
            var warning = Assert.Single(sources.Warnings);
            Assert.Contains("test.quiet: left out; no data within 0 s", warning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_source_is_left_out_when_the_area_is_wider_than_its_range()
    {
        var (sources, error) = await OneShotSources.StartAsync(
            ["test.quiet"], Registry(), new GeographicBounds(-130, 30, -100, 50), TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(error);
        using (sources)
        {
            Assert.Empty(sources!.Datasets);
            Assert.Contains("wider than the 5°", Assert.Single(sources.Warnings), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_bad_source_is_an_error()
    {
        var (sources, error) = await OneShotSources.StartAsync(
            ["acme.gone"], Registry(), Harbour, TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(sources);
        Assert.Contains("--source acme.gone", error, StringComparison.Ordinal);
        Assert.Contains("no source 'acme.gone'", error, StringComparison.Ordinal);

        (sources, error) = await OneShotSources.StartAsync(
            ["test.vessels:Nope=1"], Registry(), Harbour, TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(sources);
        Assert.Contains("no option 'Nope'", error, StringComparison.Ordinal);
    }

    private static SourceRegistry Registry() => new SourceRegistry()
        .AddDataSource(new SourceRegistration
        {
            Id = "test.vessels",
            DisplayName = "Test vessels",
            Spec = SpecName.Ais,
            Create = _ => new DynamicSourceProcessor(new FixedSource([Vessel()]), VesselPortrayal.Ais, new SpecRef(SpecName.Ais, default), ownsSource: true),
        })
        .AddDataSource(new SourceRegistration
        {
            Id = "test.quiet",
            DisplayName = "Quiet feed",
            Spec = SpecName.Ndbc,
            Activation = SourceActivation.BelowViewportSpan(5),
            Create = _ => new DynamicSourceProcessor(new FixedSource([]), VesselPortrayal.Ais, new SpecRef(SpecName.Ndbc, default), ownsSource: true),
        });

    private static DynamicFeature Vessel() => new()
    {
        Id = "ais:366000001",
        Kind = "vessel.ais",
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(47.6, -122.4)],
        Attributes = new Dictionary<string, object?> { ["mmsi"] = 366000001u },
        LastUpdated = DateTimeOffset.UnixEpoch,
    };

    private sealed class FixedSource(IReadOnlyList<DynamicFeature> features) : IDynamicFeatureSource
    {
        public string Id => "test";

        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "Test", Plane = S98DisplayPlane.Vessels };

        public IReadOnlyList<DynamicFeature> CurrentFeatures => features;

        public event EventHandler<DynamicFeaturesChanged>? Changed { add { } remove { } }
    }
}
