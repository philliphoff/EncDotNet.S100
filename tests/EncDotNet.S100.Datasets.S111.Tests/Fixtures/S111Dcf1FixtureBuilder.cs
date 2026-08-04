using System.Reflection;

using PureHDF;

namespace EncDotNet.S100.Datasets.S111.Tests.Fixtures;

internal static class S111Dcf1FixtureBuilder
{
    public struct ValueRow
    {
        [H5Name("surfaceCurrentSpeed")]
        public float SurfaceCurrentSpeed;

        [H5Name("surfaceCurrentDirection")]
        public float SurfaceCurrentDirection;
    }

    public struct GeometryRow
    {
        [H5Name("longitude")]
        public double Longitude;

        [H5Name("latitude")]
        public double Latitude;
    }

    public sealed class Station
    {
        public required double Latitude { get; init; }

        public required double Longitude { get; init; }
    }

    public sealed class TimeStep
    {
        public required string TimePoint { get; init; }

        public required ValueRow[] Values { get; init; }
    }

    public static string WriteFile(
        string path,
        IReadOnlyList<Station> stations,
        IReadOnlyList<TimeStep> timeSteps,
        long declaredInterval = 3600)
    {
        var geometry = new GeometryRow[stations.Count];
        for (int index = 0; index < stations.Count; index++)
        {
            geometry[index] = new GeometryRow
            {
                Latitude = stations[index].Latitude,
                Longitude = stations[index].Longitude,
            };
        }

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["numberOfStations"] = (long)stations.Count,
                ["numberOfTimes"] = (long)timeSteps.Count,
                ["timeRecordInterval"] = declaredInterval,
            },
            ["Positioning"] = new H5Group
            {
                ["geometryValues"] = geometry,
            },
        };

        for (int index = 0; index < timeSteps.Count; index++)
        {
            instance[$"Group_{index + 1:000}"] = new H5Group
            {
                Attributes = new()
                {
                    ["timePoint"] = timeSteps[index].TimePoint,
                },
                ["values"] = timeSteps[index].Values,
            };
        }

        var file = new H5File
        {
            Attributes = new()
            {
                ["horizontalCRS"] = 4326,
                ["productSpecification"] = "INT.IHO.S-111.1.2",
                ["geographicIdentifier"] = "Synthetic DCF1",
            },
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new()
                {
                    ["dataCodingFormat"] = (byte)1,
                    ["typeOfCurrentData"] = (long)6,
                },
                ["SurfaceCurrent.01"] = instance,
            },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: field => field.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
        return path;
    }
}
