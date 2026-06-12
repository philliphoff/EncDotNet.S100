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

    /// <summary>
    /// Writes a dcf2 fixture with multiple <c>Group_NNN</c> time steps and
    /// the <c>dateTimeOfFirstRecord</c> / <c>timeRecordInterval</c> instance
    /// attributes that drive the reader's deferred (lazy) time-point
    /// arithmetic. Each step's values are <paramref name="valuesPerStep"/>[i].
    /// </summary>
    public static string WriteMultiStepFile(
        string path,
        SpecRow[][] valuesPerStep,
        int numLat,
        int numLon,
        string dateTimeOfFirstRecord = "20260101T00:00:00Z",
        long timeRecordInterval = 1200)
    {
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = numLat,
                ["numPointsLongitudinal"] = numLon,
                ["numberOfTimes"] = valuesPerStep.Length,
                ["dateTimeOfFirstRecord"] = dateTimeOfFirstRecord,
                ["timeRecordInterval"] = timeRecordInterval,
            },
        };

        for (int i = 0; i < valuesPerStep.Length; i++)
        {
            // Per-step timePoint is still written so the eager path (which
            // does not consult dateTimeOfFirstRecord) can also read the file.
            var stepStart = ParseFirstRecord(dateTimeOfFirstRecord).AddSeconds(timeRecordInterval * i);
            instance[$"Group_{i + 1:000}"] = new H5Group
            {
                Attributes = new()
                {
                    ["timePoint"] = stepStart.ToString("yyyyMMdd'T'HHmmss'Z'"),
                },
                ["values"] = valuesPerStep[i],
            };
        }

        var file = new H5File
        {
            Attributes = new()
            {
                ["horizontalDatumValue"] = 4326,
                ["geographicIdentifier"] = "Test",
                ["issueDate"] = "2026-01-01",
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

    private static DateTime ParseFirstRecord(string s) =>
        DateTime.ParseExact(
            s,
            ["yyyyMMdd'T'HH:mm:ss'Z'", "yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
}
