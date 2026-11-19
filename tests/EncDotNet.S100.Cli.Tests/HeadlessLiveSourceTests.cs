using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
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
