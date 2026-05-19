using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Datasets.S111.Tests.Fixtures;

/// <summary>
/// Helpers that author small synthetic S-111 dcf3 ("ungeorectified grid")
/// HDF5 files for reader tests. DCF 3 stores per-node positions in
/// <c>Positioning/geometryValues</c> and per-timestep value arrays in
/// <c>Group_NNN/values</c> (S-100 Part 10c §10.2.1).
/// </summary>
internal static class S111Dcf3FixtureBuilder
{
    public struct ValueRow
    {
        [H5Name("surfaceCurrentSpeed")] public float SurfaceCurrentSpeed;
        [H5Name("surfaceCurrentDirection")] public float SurfaceCurrentDirection;
    }

    public struct GeometryRow
    {
        [H5Name("latitude")] public float Latitude;
        [H5Name("longitude")] public float Longitude;
    }

    public sealed class Node
    {
        public required float Latitude { get; init; }
        public required float Longitude { get; init; }
    }

    public sealed class TimeStep
    {
        public required string TimePoint { get; init; }
        public required ValueRow[] Values { get; init; }
    }

    /// <summary>
    /// Writes a minimal S-111 dcf3 file with one <c>SurfaceCurrent.01</c>
    /// instance. Node positions live under
    /// <c>SurfaceCurrent.01/Positioning/geometryValues</c>; each time step
    /// is a <c>Group_NNN</c> with a flat <c>values</c> compound dataset.
    /// </summary>
    public static string WriteFile(
        string path,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<TimeStep> timeSteps,
        string firstDateTime = "20240101T000000Z",
        string lastDateTime = "20240101T030000Z",
        long timeRecordInterval = 3600)
    {
        // Build Positioning/geometryValues
        var geomRows = new GeometryRow[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            geomRows[i] = new GeometryRow { Latitude = nodes[i].Latitude, Longitude = nodes[i].Longitude };

        var positioningGroup = new H5Group { ["geometryValues"] = geomRows };

        // Build instance
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["numberOfNodes"] = (long)nodes.Count,
                ["numberOfTimes"] = (long)timeSteps.Count,
                ["dateTimeOfFirstRecord"] = firstDateTime,
                ["dateTimeOfLastRecord"] = lastDateTime,
                ["timeRecordInterval"] = timeRecordInterval,
            },
            ["Positioning"] = positioningGroup,
        };

        for (int t = 0; t < timeSteps.Count; t++)
        {
            instance[$"Group_{t + 1:000}"] = new H5Group
            {
                Attributes = new() { ["timePoint"] = timeSteps[t].TimePoint },
                ["values"] = timeSteps[t].Values,
            };
        }

        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["horizontalDatumValue"] = 4326,
                ["geographicIdentifier"] = "Test DCF3",
                ["issueDate"] = "2024-01-01",
            },
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new()
                {
                    ["dataCodingFormat"] = (byte)3,
                    ["typeOfCurrentData"] = (long)6,
                },
                ["SurfaceCurrent.01"] = instance,
            },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);

        file.Write(path, options);
        return path;
    }
}
