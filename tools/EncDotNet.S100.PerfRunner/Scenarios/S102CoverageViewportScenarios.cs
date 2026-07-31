using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// PR-B: quantifies the payoff of viewport-scoped coverage sampling
/// (issue #487) against the same synthetic 1000×1000 fixture used by
/// <see cref="S102CoverageRenderLargeScenario"/>. All three scenarios
/// share the fixture (built once in temp) and only vary the
/// <see cref="RenderContext.Viewport"/> so the diff isolates the effect
/// of subset+stride sampling on the coverage pipeline's dominant span
/// (<c>s100.render.coverage.build</c>).
/// </summary>
internal abstract class S102CoverageViewportScenarioBase : IPerfScenario
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    private IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var fixturePath = S102CoverageRenderLargeScenario.EnsureSyntheticFixture(
                S102CoverageRenderLargeScenario.SyntheticDim);
            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(fixturePath);
        }

        var viewport = BuildViewport();
        var context = new S102RenderContext { Viewport = viewport };
        var layerCount = ProcessorRenderBridge.RenderLayerCount(_processor, context, ct);

        if (layerCount == 0)
            throw new InvalidOperationException("Expected at least one layer from S-102 render.");

        return Task.CompletedTask;
    }

    protected abstract Viewport BuildViewport();

    // Fixture is at OriginLat=50.0, OriginLon=-1.0, spacing 0.0001 for
    // 1000×1000 nodes, so the grid covers 50.0..50.0999 lat and
    // -1.0..-0.9001 lon.
    protected const double GridSouth = S102CoverageRenderLargeScenario.OriginLat;
    protected const double GridNorth = GridSouth + S102CoverageRenderLargeScenario.Spacing * (S102CoverageRenderLargeScenario.SyntheticDim - 1);
    protected const double GridWest = S102CoverageRenderLargeScenario.OriginLon;
    protected const double GridEast = GridWest + S102CoverageRenderLargeScenario.Spacing * (S102CoverageRenderLargeScenario.SyntheticDim - 1);
}

/// <summary>
/// Viewport that fits the entire synthetic grid at a scale where every
/// cell projects to roughly one pixel. Acts as a "no-regression" guard:
/// stride collapses to 1 and the sampled cell count matches the baseline
/// (1,000,000).
/// </summary>
internal sealed class S102CoverageViewportFitScenario : S102CoverageViewportScenarioBase
{
    public override string Name => "s102-coverage-viewport-fit";
    public override string Description => "S-102: viewport ≈ full grid extent (stride 1).";

    protected override Viewport BuildViewport() => new()
    {
        MinLatitude = GridSouth,
        MaxLatitude = GridNorth,
        MinLongitude = GridWest,
        MaxLongitude = GridEast,
        WidthPixels = 1000,
        HeightPixels = 1000,
        ScaleDenominator = 50_000,
    };
}

/// <summary>
/// Viewport that zooms into ~10 % of the grid extent at 800×600 px.
/// Cell size (0.0001°) still exceeds the viewport's ground resolution
/// (0.01° / 800 px = 1.25e-5°/px), so stride stays at 1 but the sampled
/// subset is roughly 100×100 = 10 000 cells (≈ 1 % of full-grid work).
/// </summary>
internal sealed class S102CoverageViewportZoomedInScenario : S102CoverageViewportScenarioBase
{
    public override string Name => "s102-coverage-viewport-zoomed-in";
    public override string Description => "S-102: viewport = 10% of grid, 800×600 px (stride 1, subset only).";

    protected override Viewport BuildViewport()
    {
        // 10 % of the grid extent centred on the middle of the grid.
        double centreLat = (GridSouth + GridNorth) / 2;
        double centreLon = (GridWest + GridEast) / 2;
        double halfLat = (GridNorth - GridSouth) * 0.05;
        double halfLon = (GridEast - GridWest) * 0.05;
        return new Viewport
        {
            MinLatitude = centreLat - halfLat,
            MaxLatitude = centreLat + halfLat,
            MinLongitude = centreLon - halfLon,
            MaxLongitude = centreLon + halfLon,
            WidthPixels = 800,
            HeightPixels = 600,
            ScaleDenominator = 5_000,
        };
    }
}

/// <summary>
/// Viewport that zooms out to ~4× the grid extent at 800×600 px. Ground
/// resolution (~0.0005°/px) is 5× the cell size, so stride derives to 5
/// and the sampled subset is roughly (1000/5)² = 40 000 cells — 25× less
/// than the full-grid baseline.
/// </summary>
internal sealed class S102CoverageViewportZoomedOutScenario : S102CoverageViewportScenarioBase
{
    public override string Name => "s102-coverage-viewport-zoomed-out";
    public override string Description => "S-102: viewport = 4x grid extent, 800×600 px (stride ~5).";

    protected override Viewport BuildViewport()
    {
        // 4× grid extent centred on the middle of the grid — intersects
        // the full grid but at coarse ground resolution.
        double centreLat = (GridSouth + GridNorth) / 2;
        double centreLon = (GridWest + GridEast) / 2;
        double halfLat = (GridNorth - GridSouth) * 2.0;
        double halfLon = (GridEast - GridWest) * 2.0;
        return new Viewport
        {
            MinLatitude = centreLat - halfLat,
            MaxLatitude = centreLat + halfLat,
            MinLongitude = centreLon - halfLon,
            MaxLongitude = centreLon + halfLon,
            WidthPixels = 800,
            HeightPixels = 600,
            ScaleDenominator = 200_000,
        };
    }
}
