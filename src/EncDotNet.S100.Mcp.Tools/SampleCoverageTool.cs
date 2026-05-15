using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="SampleCoverageTool"/>.</summary>
/// <param name="Spec">Spec of the coverage to sample. Currently only S-102 is supported.</param>
/// <param name="Latitude">Sample latitude (decimal degrees, WGS-84).</param>
/// <param name="Longitude">Sample longitude (decimal degrees, WGS-84).</param>
/// <param name="Time">Optional time selector for time-varying products. Ignored for S-102.</param>
public sealed record SampleCoverageRequest(
    SpecRef Spec,
    double Latitude,
    double Longitude,
    DateTimeOffset? Time = null);

/// <summary>Result of <see cref="SampleCoverageTool"/>.</summary>
public sealed record SampleCoverageResult(
    DatasetId DatasetId,
    double Latitude,
    double Longitude,
    SampledValue Value);

/// <summary>Discriminated payload returned by <see cref="SampleCoverageTool"/>.</summary>
public abstract record SampledValue;

/// <summary>S-102 depth sample (metres below the vertical datum, positive down).</summary>
public sealed record DepthSample(double DepthMeters, double? UncertaintyMeters) : SampledValue;

/// <summary>
/// Samples a coverage product at a single lat/lon, returning the nearest
/// grid cell's value. Currently supports S-102; S-104 and S-111 return
/// <see cref="SpecNotSupportedForTool"/>.
/// </summary>
/// <remarks>
/// "Nearest cell" semantics: the cell whose centre is closest to the
/// requested point. No interpolation is performed.
/// </remarks>
public sealed class SampleCoverageTool
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "sample_coverage";

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="SampleCoverageTool"/>.</summary>
    public SampleCoverageTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<SampleCoverageResult>> InvokeAsync(
        SampleCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(request.Spec.Name, "S-102", StringComparison.Ordinal))
        {
            return Task.FromResult(
                ToolResult<SampleCoverageResult>.Err(
                    new SpecNotSupportedForTool(request.Spec, Name)));
        }

        var snapshot = _catalog.Datasets;
        LoadedDataset? match = null;
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S102CoverageData) continue;
            if (!Contains(dataset.Bounds, request.Latitude, request.Longitude)) continue;
            match = dataset;
            break;
        }

        if (match is null)
        {
            return Task.FromResult(
                ToolResult<SampleCoverageResult>.Err(
                    new NoDatasetCoversPoint(request.Latitude, request.Longitude)));
        }

        var source = ((S102CoverageData)match.Data).Source;

        try
        {
            var metadata = source.Metadata;
            var grid = metadata.GridMetadata;
            var (row, col) = NearestCell(grid, request.Latitude, request.Longitude);

            var region = new GridRegion(row, row + 1, col, col + 1, 1, 1);
            var sampled = source.Sample(region);

            var depth = ReadScalar(sampled, "depth");
            var uncertainty = TryReadScalar(sampled, "uncertainty");

            var noData = metadata.NoDataValue;
            double? depthValue = depth == noData ? null : depth;
            double? uncertaintyValue = uncertainty is { } u && u != noData ? u : null;

            if (depthValue is null)
            {
                return Task.FromResult(
                    ToolResult<SampleCoverageResult>.Err(
                        new NoDatasetCoversPoint(request.Latitude, request.Longitude)));
            }

            var result = new SampleCoverageResult(
                match.Id,
                request.Latitude,
                request.Longitude,
                new DepthSample(depthValue.Value, uncertaintyValue));

            return Task.FromResult(ToolResult<SampleCoverageResult>.Ok(result));
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(
                ToolResult<SampleCoverageResult>.Err(
                    new DatasetClosedDuringQuery(match.Id)));
        }
    }

    private static bool Contains(BoundingBox b, double lat, double lon) =>
        lat >= b.SouthLatitude
        && lat <= b.NorthLatitude
        && lon >= b.WestLongitude
        && lon <= b.EastLongitude;

    private static (int Row, int Col) NearestCell(GridMetadata grid, double lat, double lon)
    {
        var row = (int)Math.Round((lat - grid.OriginLatitude) / grid.SpacingLatitudinal);
        var col = (int)Math.Round((lon - grid.OriginLongitude) / grid.SpacingLongitudinal);
        row = Math.Clamp(row, 0, grid.NumRows - 1);
        col = Math.Clamp(col, 0, grid.NumColumns - 1);
        return (row, col);
    }

    private static float ReadScalar(SampledCoverage sampled, string field)
    {
        var data = sampled.GetField(field);
        return data[0, 0];
    }

    private static float? TryReadScalar(SampledCoverage sampled, string field)
    {
        if (!sampled.Values.TryGetValue(field, out var data))
        {
            return null;
        }
        return data[0, 0];
    }
}
