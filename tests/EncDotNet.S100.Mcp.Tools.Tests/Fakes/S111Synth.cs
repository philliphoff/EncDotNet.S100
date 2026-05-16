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

    /// <summary>
    /// Creates a synthetic <see cref="S111StationSeriesDataset"/> (dcf8).
    /// </summary>
    public static S111StationSeriesDataset StationSeries(
        int stationCount = 2,
        int samplesPerStation = 4,
        string idPrefix = "CUR_",
        float baseSpeed = 0.3f,
        float baseDirection = 45.0f)
    {
        var stations = new List<SurfaceCurrentStation>(stationCount);
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromHours(1);

        DateTime? min = null, max = null;
        for (int s = 0; s < stationCount; s++)
        {
            var speeds = new float[samplesPerStation];
            var dirs = new float[samplesPerStation];
            for (int i = 0; i < samplesPerStation; i++)
            {
                speeds[i] = baseSpeed + s * 0.05f + i * 0.01f;
                dirs[i] = (baseDirection + s * 10f + i * 5f) % 360f;
            }
            var end = start + TimeSpan.FromTicks(interval.Ticks * (samplesPerStation - 1));
            stations.Add(new SurfaceCurrentStation
            {
                Identifier = $"{idPrefix}{s + 1:D3}",
                Latitude = 47.0 + s * 0.1,
                Longitude = -122.0 - s * 0.1,
                StartTime = start,
                EndTime = end,
                TimeRecordInterval = interval,
                NumberOfTimes = samplesPerStation,
                SpeedsMetresPerSecond = speeds,
                DirectionsDegreesTrue = dirs,
            });
            min = min is null || start < min ? start : min;
            max = max is null || end > max ? end : max;
        }

        return new S111StationSeriesDataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = 8,
            TypeOfCurrentData = 6,
            SurfaceCurrentDepth = 1.5f,
            Stations = stations,
            MinTime = min,
            MaxTime = max,
        };
    }
}
