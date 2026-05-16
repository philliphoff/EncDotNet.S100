using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Datasets.S104.Tests.Fixtures;

/// <summary>
/// Helpers that author small synthetic S-104 HDF5 files for reader-hardening
/// tests. Each builder method writes to a temp <c>.h5</c> path and returns
/// it; the caller is responsible for deletion.
/// </summary>
internal static class S104FixtureBuilder
{
    // ---- Compound row variants --------------------------------------------------

    // Spec member names — height as F32, trend as uint8.
    public struct SpecRow
    {
        [H5Name("waterLevelHeight")] public float WaterLevelHeight;
        [H5Name("waterLevelTrend")] public byte WaterLevelTrend;
    }

    // UKHO dcf2 shape — height as F32, trend as F32.
    public struct UkhoRow
    {
        [H5Name("surfaceHeight")] public float SurfaceHeight;
        [H5Name("trend")] public float Trend;
    }

    // Legacy synthetic-fixture shape — C# names that happen to match struct fields.
    public struct LegacyRow
    {
        public float Height;
        public byte Trend;
    }

    /// <summary>
    /// Writes a minimal S-104 file using the supplied compound-row type and
    /// the requested numeric width for grid-georef attributes.
    /// </summary>
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
                ["horizontalCRS"] = 4326,
                ["geographicIdentifier"] = "Test",
                ["issueDate"] = "2021-04-01",
            },
            ["WaterLevel"] = new H5Group
            {
                Attributes = new()
                {
                    ["dataCodingFormat"] = (byte)2,
                },
                ["WaterLevel.01"] = instance,
            },
        };

        // Map [H5Name(...)] on row fields → HDF5 compound member names.
        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);

        file.Write(path, options);
        return path;
    }
}
