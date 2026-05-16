using EncDotNet.S100.Datasets.S104;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S104Synth
{
    public static S104Dataset Dataset(
        double originLat = 0.0,
        double originLon = 0.0,
        double spacingLat = 0.01,
        double spacingLon = 0.01,
        int numRows = 4,
        int numCols = 4,
        float height = 1.5f,
        byte trend = 3,
        int dataCodingFormat = 2,
        DateTime[]? times = null)
    {
        times ??= new[]
        {
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc),
        };

        var coverages = new List<WaterLevelCoverage>();
        for (int t = 0; t < times.Length; t++)
        {
            var values = new WaterLevelValue[numRows * numCols];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new WaterLevelValue(height + t * 0.1f, trend);
            }
            coverages.Add(new WaterLevelCoverage
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

        return new S104Dataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = dataCodingFormat,
            Coverages = coverages,
        };
    }

    public static S104CoverageSource Source(S104Dataset? dataset = null) =>
        new(dataset ?? Dataset());
}
