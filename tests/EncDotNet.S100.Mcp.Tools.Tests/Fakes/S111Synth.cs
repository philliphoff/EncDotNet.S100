using EncDotNet.S100.Datasets.S111;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S111Synth
{
    public static S111Dataset Dataset(
        double originLat = 0.0,
        double originLon = 0.0,
        double spacingLat = 0.01,
        double spacingLon = 0.01,
        int numRows = 4,
        int numCols = 4,
        float speed = 0.5f,
        float direction = 90.0f,
        int dataCodingFormat = 2,
        DateTime[]? times = null)
    {
        times ??= new[]
        {
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc),
        };

        var coverages = new List<SurfaceCurrentCoverage>();
        for (int t = 0; t < times.Length; t++)
        {
            var values = new SurfaceCurrentValue[numRows * numCols];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new SurfaceCurrentValue(speed + t * 0.05f, direction);
            }
            coverages.Add(new SurfaceCurrentCoverage
            {
                OriginLatitude = originLat,
                OriginLongitude = originLon,
                SpacingLatitudinal = spacingLat,
                SpacingLongitudinal = spacingLon,
                NumPointsLatitudinal = numRows,
                NumPointsLongitudinal = numCols,
                TimePoint = times[t],
                Values = values,
            });
        }

        return new S111Dataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = dataCodingFormat,
            Coverages = coverages,
        };
    }

    public static S111CoverageSource Source(S111Dataset? dataset = null) =>
        new(dataset ?? Dataset());
}
