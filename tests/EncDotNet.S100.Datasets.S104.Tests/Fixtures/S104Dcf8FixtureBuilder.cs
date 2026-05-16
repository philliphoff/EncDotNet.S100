using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Datasets.S104.Tests.Fixtures;

/// <summary>
/// Helpers that author small synthetic S-104 dcf8 ("time series at fixed
/// stations") HDF5 files for reader tests.
/// </summary>
internal static class S104Dcf8FixtureBuilder
{
    // ---- Compound row variants --------------------------------------------------

    public struct SpecValueRow
    {
        [H5Name("waterLevelHeight")] public float WaterLevelHeight;
        [H5Name("waterLevelTrend")] public byte WaterLevelTrend;
    }

    public struct UkhoValueRow
    {
        [H5Name("surfaceHeight")] public float SurfaceHeight;
        [H5Name("trend")] public sbyte Trend;
    }

    public struct UkhoValueRowF32Trend
    {
        [H5Name("surfaceHeight")] public float SurfaceHeight;
        [H5Name("trend")] public float Trend;
    }

    public struct GeometryRow
    {
        [H5Name("latitude")] public float Latitude;
        [H5Name("longitude")] public float Longitude;
    }

    public sealed class Station<TRow> where TRow : struct
    {
        public required string Identifier { get; init; }
        public required float Latitude { get; init; }
        public required float Longitude { get; init; }
        public required string StartDateTime { get; init; }
        public required string EndDateTime { get; init; }
        public required long TimeRecordInterval { get; init; }
        public required TRow[] Values { get; init; }
    }

    /// <summary>
    /// Writes a minimal S-104 dcf8 file with one <c>WaterLevel.01</c>
    /// instance containing the supplied stations. Position rows match
    /// station declaration order per S-104 §10.2.3.
    /// </summary>
    public static string WriteFile<TRow>(
        string path,
        IReadOnlyList<Station<TRow>> stations,
        bool includePositioning = true,
        double? waterLevelTrendThreshold = null)
        where TRow : struct
    {
        var instanceGroups = new Dictionary<string, object>();
        for (int i = 0; i < stations.Count; i++)
        {
            var s = stations[i];
            instanceGroups[$"Group_{i + 1:000}"] = new H5Group
            {
                Attributes = new()
                {
                    ["stationIdentification"] = s.Identifier,
                    ["startDateTime"] = s.StartDateTime,
                    ["endDateTime"] = s.EndDateTime,
                    ["numberOfTimes"] = (long)s.Values.Length,
                    ["timeRecordInterval"] = s.TimeRecordInterval,
                },
                ["values"] = s.Values,
            };
        }

        long firstInterval = stations.Count > 0 ? stations[0].TimeRecordInterval : 0L;
        int firstNumberOfTimes = stations.Count > 0 ? stations[0].Values.Length : 0;

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["numberOfStations"] = (long)stations.Count,
                ["numberOfTimes"] = (long)firstNumberOfTimes,
                ["timeRecordInterval"] = firstInterval,
            },
        };
        foreach (var (k, v) in instanceGroups)
            instance[k] = v;

        var rootAttrs = new Dictionary<string, object>
        {
            ["horizontalCRS"] = 4326,
            ["geographicIdentifier"] = "Test",
            ["issueDate"] = "2024-01-01",
        };
        if (waterLevelTrendThreshold is not null)
            rootAttrs["waterLevelTrendThreshold"] = waterLevelTrendThreshold.Value;

        var file = new H5File
        {
            Attributes = rootAttrs,
            ["WaterLevel"] = new H5Group
            {
                Attributes = new()
                {
                    ["dataCodingFormat"] = (byte)8,
                    ["methodWaterLevelProduct"] = "astronomical prediction",
                },
                ["WaterLevel.01"] = instance,
            },
        };

        if (includePositioning)
        {
            var geom = new GeometryRow[stations.Count];
            for (int i = 0; i < stations.Count; i++)
                geom[i] = new GeometryRow { Latitude = stations[i].Latitude, Longitude = stations[i].Longitude };

            file["Positioning"] = new H5Group
            {
                ["geometryValues"] = geom,
            };
        }

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);

        file.Write(path, options);
        return path;
    }
}
