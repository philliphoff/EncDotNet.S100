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

    /// <summary>
    /// Creates a synthetic <see cref="S104StationSeriesDataset"/> (dcf8)
    /// containing <paramref name="stationCount"/> stations, each with
    /// <paramref name="samplesPerStation"/> samples at one-hour cadence
    /// starting at <c>2024-01-01T00:00:00Z</c>.
    /// </summary>
    public static S104StationSeriesDataset StationSeries(
        int stationCount = 2,
        int samplesPerStation = 4,
        string idPrefix = "STN_",
        float baseHeight = 1.0f)
    {
        var stations = new List<WaterLevelStation>(stationCount);
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromHours(1);

        DateTime? min = null, max = null;
        for (int s = 0; s < stationCount; s++)
        {
            var heights = new float[samplesPerStation];
            var trends = new byte[samplesPerStation];
            for (int i = 0; i < samplesPerStation; i++)
            {
                heights[i] = baseHeight + s * 0.5f + i * 0.1f;
                trends[i] = (byte)((i % 3) + 1);
            }
            var end = start + TimeSpan.FromTicks(interval.Ticks * (samplesPerStation - 1));
            stations.Add(new WaterLevelStation
            {
                Identifier = $"{idPrefix}{s + 1:D3}",
                Latitude = 47.0 + s * 0.1,
                Longitude = -122.0 - s * 0.1,
                StartTime = start,
                EndTime = end,
                TimeRecordInterval = interval,
                NumberOfTimes = samplesPerStation,
                Heights = heights,
                Trends = trends,
            });
            min = min is null || start < min ? start : min;
            max = max is null || end > max ? end : max;
        }

        return new S104StationSeriesDataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = 8,
            MethodWaterLevelProduct = "astronomical prediction",
            WaterLevelTrendThreshold = 0.1,
            Stations = stations,
            MinTime = min,
            MaxTime = max,
        };
    }
}
