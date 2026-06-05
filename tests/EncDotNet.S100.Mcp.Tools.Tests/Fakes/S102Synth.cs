using EncDotNet.S100.Datasets.S102;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S102Synth
{
    public static S102Dataset Dataset(
        double originLat = 0.0,
        double originLon = 0.0,
        double spacingLat = 0.01,
        double spacingLon = 0.01,
        int numRows = 4,
        int numCols = 4,
        float depth = 12.5f,
        float uncertainty = 0.25f)
    {
        var values = new BathymetryValue[numRows * numCols];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new BathymetryValue(depth, uncertainty);
        }

        var coverage = new BathymetryCoverage
        {
            OriginLatitude = originLat,
            OriginLongitude = originLon,
            SpacingLatitudinal = spacingLat,
            SpacingLongitudinal = spacingLon,
            NumPointsLatitudinal = numRows,
            NumPointsLongitudinal = numCols,
            Values = values,
        };

        return new S102Dataset
        {
            HorizontalCRS = 4326,
            Coverages = [coverage],
        };
    }

    public static S102CoverageSource Source(S102Dataset? dataset = null) =>
        new(dataset ?? Dataset());
}

/// <summary>S-102 coverage source that throws <see cref="ObjectDisposedException"/> from <c>Sample</c> after being disposed.</summary>
internal sealed class DisposableS102CoverageSource(S102Dataset dataset) : S102CoverageSource(dataset), IDisposable
{
    private bool _disposed;

    public override EncDotNet.S100.Pipelines.Coverage.SampledCoverage Sample(EncDotNet.S100.Pipelines.Coverage.GridRegion region, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return base.Sample(region, cancellationToken);
    }

    public void Dispose() => _disposed = true;
}
