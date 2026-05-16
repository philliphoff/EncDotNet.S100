using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Datasets.S111.Tests.Fixtures;

internal static class S111FixtureBuilder
{
    public struct SpecRow
    {
        [H5Name("surfaceCurrentSpeed")] public float SurfaceCurrentSpeed;
        [H5Name("surfaceCurrentDirection")] public float SurfaceCurrentDirection;
    }

    public struct LegacyRow
    {
        public float Speed;
        public float Direction;
    }

    public static string WriteFile<TRow>(
        string path,
        TRow[] values,
        int numLat,
        int numLon,
        bool useF64GridAttrs,
        bool useUnsignedCounts,
        string timePoint = "20210401T000000Z")
        where TRow : struct
    {
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = useF64GridAttrs ? 50.0 : (object)50.0f,
                ["gridOriginLongitude"] = useF64GridAttrs ? -1.0 : (object)-1.0f,
                ["gridSpacingLatitudinal"] = useF64GridAttrs ? 0.01 : (object)0.01f,
                ["gridSpacingLongitudinal"] = useF64GridAttrs ? 0.01 : (object)0.01f,
                ["numPointsLatitudinal"] = useUnsignedCounts ? (object)(uint)numLat : numLat,
                ["numPointsLongitudinal"] = useUnsignedCounts ? (object)(uint)numLon : numLon,
            },
            ["Group_001"] = new H5Group
            {
                Attributes = new()
                {
                    ["timePoint"] = timePoint,
                },
                ["values"] = values,
            },
        };

        var file = new H5File
        {
            Attributes = new()
            {
                ["horizontalDatumValue"] = 4326,
                ["geographicIdentifier"] = "Test",
                ["issueDate"] = "2021-04-01",
            },
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new()
                {
                    ["dataCodingFormat"] = (byte)2,
                    ["typeOfCurrentData"] = (byte)6,
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
