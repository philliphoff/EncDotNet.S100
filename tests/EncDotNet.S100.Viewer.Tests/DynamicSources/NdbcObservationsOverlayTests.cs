using System.Net;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ndbc;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ndbc;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>#1070: the viewer's binding of the NDBC source to the viewport and its Layer Stack toggle.</summary>
public sealed class NdbcObservationsOverlayTests : IAsyncLifetime
{
    private readonly NotFoundHandler _handler = new();
    private readonly FakeNotifier _notifier = new();
    private readonly NdbcObservationsOverlay _overlay;

    public NdbcObservationsOverlayTests()
    {
        var http = new HttpClient(_handler);
        var source = new NdbcObservationSource(http, new NdbcObservationSourceOptions { Id = NdbcObservationsServiceCollectionExtensions.SourceId });
        _overlay = new NdbcObservationsOverlay(source, http, _notifier, debounce: TimeSpan.FromMilliseconds(10));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _overlay.DisposeAsync();

    [Fact]
    public void The_overlay_starts_hidden_unless_the_user_chose_otherwise()
    {
        var fresh = new FakeRegistry();
        _overlay.SeedDefaultVisibility(fresh, new Dictionary<string, bool>());
        Assert.False(fresh.GetVisible("ndbc"));

        var returning = new FakeRegistry();
        _overlay.SeedDefaultVisibility(returning, new Dictionary<string, bool> { ["ndbc"] = true });
        Assert.True(returning.GetVisible("ndbc"));
    }

    [Fact]
    public void The_layer_stack_toggle_turns_fetching_on_and_off()
    {
        var registry = new FakeRegistry();
        registry.SetVisible("ndbc", false);

        _overlay.Attach(registry);
        Assert.False(_overlay.Source.IsActive);

        registry.SetVisible("ndbc", true);
        Assert.True(_overlay.Source.IsActive);

        registry.SetVisible("ndbc", false);
        Assert.False(_overlay.Source.IsActive);
    }

    [Fact]
    public async Task The_viewport_becomes_the_sources_area_after_the_debounce()
    {
        _notifier.Current = Snapshot(41, -72, 43, -69);
        _overlay.Attach(new FakeRegistry());
        Assert.Equal(41, _overlay.Source.Area!.SouthLatitude);

        _notifier.Publish(Snapshot(30, -80, 31, -79));
        _notifier.Publish(Snapshot(36, -76, 38, -74));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (_overlay.Source.Area!.SouthLatitude != 36 && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(new EncDotNet.S100.Pipelines.BoundingBox(36, -76, 38, -74), _overlay.Source.Area);
    }

    private static MapViewportSnapshot Snapshot(double south, double west, double north, double east) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = north,
        MaxLongitude = east,
    };

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; set; }
        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(MapViewportSnapshot snapshot)
        {
            Current = snapshot;
            ViewportChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeRegistry : IS100DynamicSourceRegistry
    {
        private readonly Dictionary<string, bool> _visible = new(StringComparer.Ordinal);

        public IDisposable Register(IDynamicFeatureSource source) => throw new NotSupportedException();
        public IReadOnlyList<DynamicSourceRegistrationInfo> Sources => [];
        public event Action? SourcesChanged;
        public bool GetVisible(string sourceId) => !_visible.TryGetValue(sourceId, out var v) || v;

        public void SetVisible(string sourceId, bool visible)
        {
            _visible[sourceId] = visible;
            SourcesChanged?.Invoke();
        }

        public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances() => [];
        public IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution) => [];
    }
}
