using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Testing.Scenarios.Hdf5;

public static class S111FixtureBuilder
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
        string timePoint = "20210401T000000Z",
        ProjectedGrid? projected = null,
        string? productSpecification = null,
        IReadOnlyDictionary<string, object>? extraRootAttributes = null)
        where TRow : struct
    {
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = projected?.OriginNorthing ?? (useF64GridAttrs ? 50.0 : (object)50.0f),
                ["gridOriginLongitude"] = projected?.OriginEasting ?? (useF64GridAttrs ? -1.0 : (object)-1.0f),
                ["gridSpacingLatitudinal"] = projected?.Spacing ?? (useF64GridAttrs ? 0.01 : (object)0.01f),
                ["gridSpacingLongitudinal"] = projected?.Spacing ?? (useF64GridAttrs ? 0.01 : (object)0.01f),
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

        var rootAttributes = new Dictionary<string, object>
        {
            ["horizontalDatumValue"] = projected?.Epsg ?? 4326,
            ["geographicIdentifier"] = "Test",
            ["issueDate"] = "2021-04-01",
        };
        if (productSpecification is not null)
        {
            rootAttributes["productSpecification"] = productSpecification;
        }

        var file = new H5File
        {
            Attributes = rootAttributes,
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

        if (extraRootAttributes is not null)
        {
            foreach (var (name, value) in extraRootAttributes)
                file.Attributes[name] = value;
        }

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

    /// <summary>A geographic regular grid's georeferencing (S-111 Ed 2.0.0 §10.2.2).</summary>
    public readonly record struct GridGeoreference(
        double OriginLatitude,
        double OriginLongitude,
        double SpacingLatitudinal,
        double SpacingLongitudinal,
        int NumPointsLatitudinal,
        int NumPointsLongitudinal);

    /// <summary>
    /// Writes a dcf2 file with one <c>SurfaceCurrent.01</c> instance holding
    /// a <c>Group_NNN</c> per time step, in the order given. Attributes are
    /// written in dictionary order after the ones this method sets, so the
    /// same arguments give the same bytes.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="steps">Each step's <c>timePoint</c> (for example <c>20260301T000000Z</c>) and row-major values, south row first.</param>
    /// <param name="rootAttributes">Root attributes (product specification, CRS, extent, issue date, …).</param>
    /// <param name="featureAttributes">Extra <c>/SurfaceCurrent</c> attributes after <c>dataCodingFormat</c>, or <c>null</c>.</param>
    /// <param name="instanceAttributes">Extra <c>/SurfaceCurrent/SurfaceCurrent.01</c> attributes after the grid ones, or <c>null</c>.</param>
    /// <returns><paramref name="path"/>.</returns>
    public static string WriteTimeSeries<TRow>(
        string path,
        GridGeoreference grid,
        IReadOnlyList<(string TimePoint, TRow[] Values)> steps,
        IReadOnlyDictionary<string, object> rootAttributes,
        IReadOnlyDictionary<string, object>? featureAttributes = null,
        IReadOnlyDictionary<string, object>? instanceAttributes = null)
        where TRow : struct
    {
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = grid.OriginLatitude,
                ["gridOriginLongitude"] = grid.OriginLongitude,
                ["gridSpacingLatitudinal"] = grid.SpacingLatitudinal,
                ["gridSpacingLongitudinal"] = grid.SpacingLongitudinal,
                ["numPointsLatitudinal"] = grid.NumPointsLatitudinal,
                ["numPointsLongitudinal"] = grid.NumPointsLongitudinal,
                ["numberOfTimes"] = steps.Count,
                ["numGRP"] = steps.Count,
            },
        };
        foreach (var (name, value) in instanceAttributes ?? new Dictionary<string, object>())
            instance.Attributes[name] = value;

        for (int i = 0; i < steps.Count; i++)
        {
            instance[$"Group_{i + 1:000}"] = new H5Group
            {
                Attributes = new() { ["timePoint"] = steps[i].TimePoint },
                ["values"] = steps[i].Values,
            };
        }

        var feature = new H5Group
        {
            Attributes = new() { ["dataCodingFormat"] = (byte)2 },
            ["SurfaceCurrent.01"] = instance,
        };
        foreach (var (name, value) in featureAttributes ?? new Dictionary<string, object>())
            feature.Attributes[name] = value;

        var file = new H5File { ["SurfaceCurrent"] = feature };
        foreach (var (name, value) in rootAttributes)
            file.Attributes[name] = value;

        file.Write(path, new H5WriteOptions(FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }

    /// <summary>
    /// Projected (e.g. UTM) georeferencing for <c>WriteFile</c>: the grid
    /// origin and spacing are native metres and the root CRS attribute is
    /// <paramref name="Epsg"/>.
    /// </summary>
    public readonly record struct ProjectedGrid(int Epsg, double OriginNorthing, double OriginEasting, double Spacing);
}
