using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="DynamicSourceProcessor"/> with a source that has no describer,
/// and drawn live by the map session.
/// </summary>
public sealed class DynamicSourceProcessorTests
{
    private static readonly SpecRef Spec = new(SpecName.Ndbc, default);

    [Fact]
    public void Without_a_describer_a_pick_lists_the_motion_and_attributes()
    {
        var source = new TrackerSource();
        source.Publish(Point("t1", 42, -70, new Dictionary<string, object?> { ["name"] = "Test target" }) with
        {
            Motion = new DynamicMotion { SpeedOverGround = Speed.FromKnots(6.5) },
        });
        using var processor = new DynamicSourceProcessor(source, new StubPortrayal(), Spec);

        var info = processor.GetFeatureInfo("t1");

        Assert.NotNull(info);
        Assert.Equal("Test source", info!.FeatureTypeName);
        Assert.Equal("6.5 kn", info.Attributes.Single(a => a.Code == "speedOverGround").RawValue);
        Assert.Equal("Test target", info.Attributes.Single(a => a.Code == "name").RawValue);
        Assert.Null(processor.GetFeatureInfo("unknown"));
    }

    [Fact]
    public void An_empty_source_has_no_extent_and_no_features()
    {
        using var processor = new DynamicSourceProcessor(new TrackerSource(), new StubPortrayal(), Spec);

        Assert.Null(processor.Metadata.Extent);
        Assert.Empty(processor.EnumerateFeatures());
        Assert.Empty(processor.HitTestFeatures(0, 0, 1000));
    }

    [Fact]
    public async Task The_map_session_draws_the_source_again_when_it_changes()
    {
        var source = new TrackerSource();
        source.Publish(Point("t1", 42, -70));
        var processor = new DynamicSourceProcessor(source, new StubPortrayal(), Spec);
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = new MapsuiDatasetLayerSession(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));
        var id = new MapDatasetId("test");
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(new MapDataset(id, "Test source", new DatasetMetadata { Spec = Spec }, isVisible: true, isActive: true));
        await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.DatasetRenderCompleted += (_, e) =>
        {
            if (e.Kind == MapSessionRenderKind.LiveRefresh)
                refreshed.TrySetResult();
        };
        session.DatasetRenderFailed += (_, e) => refreshed.TrySetException(e.Exception);

        source.Publish(Point("t2", 43, -69));

        await refreshed.Task.WaitAsync(MapsuiLiveDatasetTests.Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["t1", "t2"], processor.EnumerateFeatures().Select(f => f.FeatureRef).Order());
    }

    private static DynamicFeature Point(string id, double latitude, double longitude, Dictionary<string, object?>? attributes = null) => new()
    {
        Id = id,
        Kind = "test",
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(latitude, longitude)],
        Attributes = attributes ?? new Dictionary<string, object?>(),
        LastUpdated = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero),
    };

    private sealed class TrackerSource : IDynamicFeatureSource
    {
        private readonly DynamicFeatureTracker<DynamicFeature> _tracker = new(f => f);

        public string Id => "test";

        public DynamicSourceMetadata Metadata { get; } = new()
        {
            DisplayName = "Test source",
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = 5,
        };

        public IReadOnlyList<DynamicFeature> CurrentFeatures => _tracker.Current;

        public event EventHandler<DynamicFeaturesChanged>? Changed;

        public void Publish(DynamicFeature feature)
        {
            var change = _tracker.Apply(feature);
            Changed?.Invoke(this, change);
        }
    }

    private sealed class StubPortrayal : IDynamicFeaturePortrayal
    {
        public ApplicationPalette Palette { get; } = new("test", new Dictionary<string, PaletteColors>
        {
            ["CHBLK"] = PaletteColors.FromHex("#000000", "#000000", "#000000"),
        });

        public Func<string, string?> SymbolProvider { get; } = _ => null;

        public bool CanPortray(DynamicFeature feature) => true;

        public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature) =>
            [new InstructionBuilder().Point(feature.Id, "TEST01")];
    }
}
