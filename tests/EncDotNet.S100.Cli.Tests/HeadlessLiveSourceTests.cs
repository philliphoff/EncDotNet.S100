using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// A live source in the headless catalog (data source design note D9, slice
/// 11b): added as a processor, queried as it changes, rendered with the rest.
/// </summary>
public sealed class HeadlessLiveSourceTests
{
    [Fact]
    public async Task A_live_source_is_queryable_and_rendered_and_queries_see_its_latest_features()
    {
        using var catalog = new HeadlessMutableCatalog(transforms: null, factory: CliTestProcessors.Bundled);
        var source = new ListSource();
        source.Set(Vessel("ais:366000001", 47.60, -122.34));
        var processor = new DynamicSourceProcessor(source, VesselPortrayal.Ais, new SpecRef(SpecName.Ais, default), ownsSource: true);

        var id = catalog.AddProcessor("ais.aisstream", processor);

        Assert.Equal("ais.aisstream", id.Value);
        Assert.Contains(processor, catalog.RenderProcessors);
        var identify = new IdentifyFeaturesService(catalog);
        Assert.Equal(["ais:366000001"], await IdentifiedAt(identify, 47.60, -122.34));

        // The source moves on; the next query sees where it is now.
        source.Set(Vessel("ais:366000001", 47.70, -122.40), Vessel("ais:366000002", 47.60, -122.34));
        Assert.Equal(["ais:366000002"], await IdentifiedAt(identify, 47.60, -122.34));

        // A second source with the same id gets its own.
        var other = new DynamicSourceProcessor(new ListSource(), VesselPortrayal.Ais, new SpecRef(SpecName.Ais, default), ownsSource: true);
        Assert.NotEqual(id, catalog.AddProcessor("ais.aisstream", other));
        Assert.True(catalog.Remove(id));
    }

    [Fact]
    public void Headless_sources_read_secrets_from_the_environment_only()
    {
        var context = HeadlessSources.Context(new EncDotNet.S100.Datasets.Pipelines.AisFeedOptions());

        Assert.Null(context.ReadSecret);
        Assert.Null(context.ResolveSecret("sc-secret:ais.aisstream.apiKey"));
        using var http = context.CreateHttpClient();
        Assert.Equal(HeadlessSources.HttpTimeout, http.Timeout);
    }

    [Fact]
    public void A_source_is_listed_with_its_options_and_what_it_needs()
    {
        using var host = Host(out _, out _, environment: _ => null);

        var info = Assert.Single(host.Sources);

        Assert.Equal("test.feed", info.Id);
        Assert.Equal(10, info.MaxViewportSpanDegrees);
        Assert.Equal(["Key", "Station"], info.Options.Select(o => o.Name));
        Assert.Equal("secret", info.Options[0].Kind);
        Assert.Equal("TEST_FEED_KEY", info.Options[0].EnvironmentVariable);
        Assert.Contains("set the TEST_FEED_KEY environment variable", info.Missing, StringComparison.Ordinal);
        Assert.Empty(info.Datasets);
    }

    [Fact]
    public void A_secret_is_never_an_argument_and_a_missing_one_is_refused()
    {
        using var host = Host(out _, out _, environment: _ => null);

        Assert.Contains("TEST_FEED_KEY", Error(host.Add("test.feed", new Dictionary<string, string> { ["Key"] = "abc" })), StringComparison.Ordinal);
        Assert.Contains("no option 'Nope'", Error(host.Add("test.feed", new Dictionary<string, string> { ["Nope"] = "1" })), StringComparison.Ordinal);
        Assert.Contains("TEST_FEED_KEY", Error(host.Add("test.feed", null)), StringComparison.Ordinal);
        Assert.Contains("no source 'acme.gone'", Error(host.Add("acme.gone", null)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_added_source_runs_for_the_viewport_within_its_range()
    {
        using var host = Host(out var catalog, out var session, environment: name => name == "TEST_FEED_KEY" ? "from-env" : null);

        var result = host.Add("test.feed", new Dictionary<string, string> { ["Station"] = "44013" });
        Assert.True(result.TryGetValue(out var added));
        Assert.Equal("paused", added!.Status); // No viewport yet.
        var source = TestFeedSource.Last!;
        Assert.Equal("44013", source.Station);
        Assert.False(source.Started);

        session.SetToBounds(new BoundingBox(southLatitude: 42, westLongitude: -71, northLatitude: 43, eastLongitude: -70));
        Assert.True(source.Started);
        Assert.True(source.IsActive);
        Assert.Equal(-71, source.Area!.WestLongitude, 0.5);
        Assert.Equal("waiting", Assert.Single(Assert.Single(host.Sources).Datasets).Status);

        // Wider than its 10° range: paused.
        session.SetToBounds(new BoundingBox(southLatitude: 0, westLongitude: -80, northLatitude: 40, eastLongitude: -30));
        Assert.False(source.IsActive);
        Assert.Equal("paused", Assert.Single(Assert.Single(host.Sources).Datasets).Status);

        // close_dataset ends it.
        Assert.True(catalog.Remove(new EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetId(added.DatasetId)));
        Assert.Empty(Assert.Single(host.Sources).Datasets);
    }

    private static HeadlessLiveSources Host(out HeadlessMutableCatalog catalog, out HeadlessS100Session session, Func<string, string?> environment)
    {
        catalog = new HeadlessMutableCatalog(transforms: null, factory: CliTestProcessors.Bundled);
        session = new HeadlessS100Session(catalog);
        var registry = new EncDotNet.S100.Datasets.Pipelines.SourceRegistry().AddDataSource(TestFeedSource.Registration);
        return new HeadlessLiveSources(registry, catalog, session, environment: environment);
    }

    private static string Error<T>(EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<T> result)
    {
        Assert.True(result.TryGetError(out var error));
        return error!.Message;
    }

    public sealed record TestFeedOptions
    {
        [EncDotNet.S100.Datasets.Pipelines.SourceOption("Test key", Required = true)]
        [EncDotNet.S100.Datasets.Pipelines.Secret(EnvironmentVariable = "TEST_FEED_KEY")]
        public string? Key { get; init; }

        [EncDotNet.S100.Datasets.Pipelines.SourceOption("Station")]
        public string? Station { get; init; }
    }

    private sealed class TestFeedSource(string? station) : IStartableDynamicSource, IPausableDynamicSource, IAreaFilteredDynamicSource
    {
        public static TestFeedSource? Last { get; private set; }

        public static EncDotNet.S100.Datasets.Pipelines.SourceRegistration Registration { get; } = new()
        {
            Id = "test.feed",
            DisplayName = "Test feed",
            Spec = SpecName.Ndbc,
            Activation = EncDotNet.S100.Datasets.Pipelines.SourceActivation.BelowViewportSpan(10),
            OptionsType = typeof(TestFeedOptions),
            DefaultOptions = new TestFeedOptions(),
            Create = context =>
            {
                var options = context.GetOptions(new TestFeedOptions());
                Last = new TestFeedSource(options.Station);
                return new DynamicSourceProcessor(Last, VesselPortrayal.Ais, new SpecRef(SpecName.Ndbc, default), ownsSource: true);
            },
        };

        public string? Station { get; } = station;

        public bool Started { get; private set; }

        public bool IsActive { get; set; } = true;

        public BoundingBox? Area { get; private set; }

        public string Id => "test";

        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "Test feed", Plane = S98DisplayPlane.OtherChartOverlays };

        public IReadOnlyList<DynamicFeature> CurrentFeatures => [];

        public event EventHandler<DynamicFeaturesChanged>? Changed { add { } remove { } }

        public void Start() => Started = true;

        public void UpdateArea(BoundingBox? area) => Area = area;
    }

    private static async Task<IReadOnlyList<string>> IdentifiedAt(IdentifyFeaturesService identify, double latitude, double longitude)
    {
        var result = await identify.InvokeAsync(new IdentifyFeaturesRequest(latitude, longitude), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value));
        return [.. value!.Features.Select(f => f.FeatureId)];
    }

    private static DynamicFeature Vessel(string id, double latitude, double longitude) => new()
    {
        Id = id,
        Kind = "vessel.ais",
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(latitude, longitude)],
        Attributes = new Dictionary<string, object?> { ["mmsi"] = uint.Parse(id[4..], System.Globalization.CultureInfo.InvariantCulture) },
        LastUpdated = DateTimeOffset.UnixEpoch,
    };

    private sealed class ListSource : IDynamicFeatureSource
    {
        private IReadOnlyList<DynamicFeature> _features = [];

        public string Id => "test";

        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "Test vessels", Plane = S98DisplayPlane.Vessels };

        public IReadOnlyList<DynamicFeature> CurrentFeatures => _features;

        public event EventHandler<DynamicFeaturesChanged>? Changed;

        public void Set(params DynamicFeature[] features)
        {
            _features = features;
            Changed?.Invoke(this, new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset, ChangedIds = [] });
        }
    }
}
