using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>Identity CRS factory for tests that need no reprojection.</summary>
internal sealed class IdentityCrsTransformFactory : ICrsTransformFactory
{
    public ICrsTransform Create(string sourceCrs, string targetCrs) =>
        IdentityCrsTransform.Instance;
}

/// <summary>
/// Configurable dataset processor test double: renders one or more vector
/// sub-layers, can be made time-aware, can throw on render, and counts
/// disposals. Shared by the reusable-session and AddS100 facade tests.
/// </summary>
internal sealed class StubProcessor :
    IDatasetProcessor,
    ITimeAwareDatasetProcessor,
    IVectorPortrayalSource,
    IDisposable
{
    private int _disposeCount;

    public StubProcessor(string datasetId)
    {
        DatasetId = datasetId;
    }

    public string DatasetId { get; }

    public int Version { get; set; } = 1;

    public IReadOnlyList<DateTime> AvailableTimes { get; set; } = [];

    public RenderContext? LastContext { get; private set; }

    public int RenderCount { get; private set; }

    public bool ThrowOnRender { get; set; }

    public int SubLayerCount { get; set; } = 1;

    public int? CellMinimumDisplayScale { get; set; }

    public IReadOnlyList<CoverageArea> CoverageAreas { get; set; } = [];

    /// <summary>
    /// When set, flows to the portrayal result's
    /// <c>GeographicExtent</c> so the rendered entry gets a controllable
    /// Web-Mercator extent (used to exercise viewport-gated refresh).
    /// </summary>
    public GeographicBounds? GeographicExtent { get; set; }

    public TimeSpan Delay { get; set; }

    public TaskCompletionSource? RenderStarted { get; set; }

    public TaskCompletionSource ReleaseDelayedRender { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ProductSpec { get; set; } = "S-101";

    public string? FeatureType { get; set; }

    public S98DisplayPlane Plane { get; set; } = S98DisplayPlane.BaseChartUnder;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public SpecRef Spec =>
        new(ProductSpec, new SpecVersion(1, 0, 0));

    /// <summary>Configurable geographic hits returned by <see cref="HitTestFeatures"/>.</summary>
    public IReadOnlyList<FeatureGeometryHit> Hits { get; set; } = [];

    /// <summary>When set, returned by <see cref="GetCoverageInfo"/> (coverage stub).</summary>
    public FeatureInfo? CoverageInfo { get; set; }

    public FeatureInfo? GetFeatureInfo(string featureRef) => null;

    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(
        double latitude, double longitude, double radiusMeters) => Hits;

    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        var hit = Hits.FirstOrDefault(h => h.Ordinal == ordinal);
        return hit is null
            ? null
            : new FeatureInfo
            {
                FeatureRef = hit.FeatureRef,
                FeatureType = hit.FeatureType,
                Attributes = [],
            };
    }

    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time) =>
        CoverageInfo;

    public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        LastContext = context;
        RenderCount++;
        if (ThrowOnRender)
            throw new InvalidOperationException("Render failed.");
        var version = Version;
        var delay = Delay;
        RenderStarted?.TrySetResult();
        if (delay > TimeSpan.Zero)
        {
            await Task.WhenAny(
                Task.Delay(delay, cancellationToken),
                ReleaseDelayedRender.Task);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var subLayers = Enumerable.Range(0, SubLayerCount)
            .Select(index => new VectorSubLayer
            {
                LayerKey = $"layer-{index}",
                LayerName = SubLayerCount == 1
                    ? $"{DatasetId}-v{version}"
                    : $"{DatasetId}-{index}-v{version}",
                Instructions =
                [
                    new AreaInstruction
                    {
                        FeatureReference = $"{index + 1}",
                        FillColor = "TEST_FILL",
                    },
                ],
                Plane = Plane,
            })
            .ToArray();
        return new VectorPortrayalResult
        {
            SubLayers = subLayers,
            Palette = new ColorPalette(
                "Test",
                new Dictionary<string, string>
                {
                    ["TEST_FILL"] = "#336699",
                }),
            GeometryProvider = new StubGeometryProvider(),
            Product = ProductSpec,
            Spec = Spec,
            SourceDatasetId = DatasetId,
            Info = DatasetId,
            LayerNames = subLayers.Select(layer => layer.LayerKey).ToArray(),
            FeatureTags = FeatureType is null
                ? null
                : new Dictionary<long, VectorFeatureTag>
                {
                    [1] = new(FeatureType, null),
                },
            CellMinimumDisplayScale = CellMinimumDisplayScale,
            CoverageAreas = CoverageAreas,
            GeographicExtent = GeographicExtent,
        };
    }

    public void Dispose() => Interlocked.Increment(ref _disposeCount);
}

/// <summary>Supplies a fixed unit-square surface geometry for stub features.</summary>
internal sealed class StubGeometryProvider : IFeatureGeometryProvider
{
    public FeatureGeometry? GetGeometry(string featureReference) =>
        new()
        {
            Type = GeometryType.Surface,
            Coordinates =
            [
                new GeoPosition(0, 0),
                new GeoPosition(0, 1),
                new GeoPosition(1, 1),
                new GeoPosition(1, 0),
                new GeoPosition(0, 0),
            ],
        };
}
