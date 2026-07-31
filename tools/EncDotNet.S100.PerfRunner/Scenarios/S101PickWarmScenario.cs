using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Warm S-101 identify/pick loop against a real UKHO trial cell — the
/// scenario introduced alongside the spatial-index change in issue
/// #490. Loads the cell once, then per iteration performs one
/// <see cref="IdentifyFeaturesService"/> call at a deterministic
/// pseudo-random pick point drawn from the cell's extent (fixed seed
/// so the sequence is reproducible across before/after runs).
/// </summary>
/// <remarks>
/// <para>
/// The path is supplied via <c>ENC_DOTNET_PERF_REAL_S101</c> (same env
/// var as <see cref="S101RealWarmScenario"/>). This scenario is
/// intentionally not part of the synthetic <c>baseline</c> run because
/// the trial cells are licensed and cannot be committed.
/// </para>
/// <para>
/// Warm timing captures the *steady-state* per-pick cost after the
/// dataset's spatial index is built. The build cost is amortised into
/// the first iteration and reported separately by
/// <c>s100.vector.index.build.duration</c>.
/// </para>
/// </remarks>
internal sealed class S101PickWarmScenario : IPerfScenario
{
    private const string PickSpec = "S-101";

    public string Name => "s101-pick-warm";

    public string Description =>
        "S-101 warm identify/pick on a real UKHO .000 cell from $ENC_DOTNET_PERF_REAL_S101.";

    private IdentifyFeaturesService? _service;
    private BoundingBox? _extent;
    private Random? _rng;

    public async Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_service is null)
        {
            var path = RealCorpusEnv.Require(RealCorpusEnv.S101Var);

            var spec = DatasetPipelineFactory.DetectProductSpec(path)
                ?? throw new InvalidOperationException(
                    $"Could not detect product specification for '{path}'.");

            var catalog = FileDatasetCatalog.Build(
                [new FileDatasetInput(new DatasetId(Path.GetFileNameWithoutExtension(path)), spec, path)]);

            if (catalog.Datasets.Count == 0)
            {
                var warnings = string.Join(" | ", catalog.Warnings);
                throw new InvalidOperationException(
                    $"FileDatasetCatalog rejected '{path}': {warnings}");
            }

            _service = new IdentifyFeaturesService(catalog);
            _extent = catalog.Datasets[0].Bounds;
            _rng = new Random(0x54_31_29_11); // fixed seed → deterministic pick sequence
        }

        var extent = _extent!;
        var rng = _rng!;

        var lat = extent.SouthLatitude + rng.NextDouble() * (extent.NorthLatitude - extent.SouthLatitude);
        var lon = extent.WestLongitude + rng.NextDouble() * (extent.EastLongitude - extent.WestLongitude);

        var request = new IdentifyFeaturesRequest(
            Latitude: lat,
            Longitude: lon,
            Spec: new SpecRef(PickSpec, default),
            RadiusMeters: 50.0,
            MaxResults: 20);

        var result = await _service.InvokeAsync(request, ct);
        if (result.TryGetError(out var err))
        {
            throw new InvalidOperationException(
                $"identify_features returned error at ({lat:F5}, {lon:F5}): {err.Message}");
        }
    }
}
