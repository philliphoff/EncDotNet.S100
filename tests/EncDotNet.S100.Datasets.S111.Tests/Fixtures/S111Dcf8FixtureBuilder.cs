using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Datasets.S111.Tests.Fixtures;

/// <summary>
/// Helpers that author small synthetic S-111 dcf8 ("time series at fixed
/// stations") HDF5 files for reader tests. Mirrors the S-104 dcf8 fixture
/// builder used by PR-C.
/// </summary>
internal static class S111Dcf8FixtureBuilder
{
    // ---- Compound row variants --------------------------------------------------

    public struct SpecValueRow
    {
        [H5Name("surfaceCurrentSpeed")] public float SurfaceCurrentSpeed;
        [H5Name("surfaceCurrentDirection")] public float SurfaceCurrentDirection;
    }

    /// <summary>
    /// Short member names <c>speed</c>/<c>direction</c> seen on legacy /
    /// pre-Edition-2 fixtures.
    /// </summary>
    public struct LegacyValueRow
    {
        [H5Name("speed")] public float Speed;
        [H5Name("direction")] public float Direction;
    }

    public struct GeometryRow
    {
        [H5Name("latitude")] public float Latitude;
        [H5Name("longitude")] public float Longitude;
    }

    /// <summary>
    /// UKHO-style short member names <c>lat</c>/<c>long</c>.
    /// </summary>
    public struct GeometryRowShort
    {
        [H5Name("lat")] public float Lat;
        [H5Name("long")] public float Long;
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
    /// Writes a minimal S-111 dcf8 file with one <c>SurfaceCurrent.01</c>
    /// instance containing the supplied stations. Position rows match
    /// station declaration order per S-111 Edition 2.0.0 §10.2.3.
    /// </summary>
    public static string WriteFile<TRow>(
        string path,
        IReadOnlyList<Station<TRow>> stations,
        bool includePositioning = true,
        bool useShortGeometryNames = false,
        bool positioningUnderInstance = false)
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
            ["horizontalDatumValue"] = 4326,
            ["geographicIdentifier"] = "Test",
            ["issueDate"] = "2024-01-01",
        };

        var file = new H5File
        {
            Attributes = rootAttrs,
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new()
                {
                    ["dataCodingFormat"] = (byte)8,
                    ["typeOfCurrentData"] = (long)6,
                },
                ["SurfaceCurrent.01"] = instance,
            },
        };

        if (includePositioning)
        {
            object geom;
            if (useShortGeometryNames)
            {
                var arr = new GeometryRowShort[stations.Count];
                for (int i = 0; i < stations.Count; i++)
                    arr[i] = new GeometryRowShort { Lat = stations[i].Latitude, Long = stations[i].Longitude };
                geom = arr;
            }
            else
            {
                var arr = new GeometryRow[stations.Count];
                for (int i = 0; i < stations.Count; i++)
                    arr[i] = new GeometryRow { Latitude = stations[i].Latitude, Longitude = stations[i].Longitude };
                geom = arr;
            }

            var positioningGroup = new H5Group { ["geometryValues"] = geom };
            if (positioningUnderInstance)
            {
                instance["Positioning"] = positioningGroup;
            }
            else
            {
                file["Positioning"] = positioningGroup;
            }
        }

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);

        file.Write(path, options);
        return path;
    }

    /// <summary>
    /// Writes an empty S-111 dcf8 file with <c>numberOfStations=0</c> and
    /// no <c>Group_NNN</c> children (still includes a Positioning group
    /// with an empty <c>geometryValues</c> dataset).
    /// </summary>
    public static string WriteEmptyFile(string path)
    {
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["numberOfStations"] = 0L,
                ["numberOfTimes"] = 0L,
                ["timeRecordInterval"] = 0L,
            },
        };

        var file = new H5File
        {
            Attributes = new()
            {
                ["horizontalDatumValue"] = 4326,
            },
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new() { ["dataCodingFormat"] = (byte)8 },
                ["SurfaceCurrent.01"] = instance,
            },
            ["Positioning"] = new H5Group
            {
                ["geometryValues"] = Array.Empty<GeometryRow>(),
            },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
        return path;
    }
}
