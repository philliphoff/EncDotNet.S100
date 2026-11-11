using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Testing.Scenarios.Hdf5;

/// <summary>
/// Helpers that author small synthetic S-104 HDF5 files for reader-hardening
/// tests. Each builder method writes to a temp <c>.h5</c> path and returns
/// it; the caller is responsible for deletion.
/// </summary>
public static class S104FixtureBuilder
{
    // ---- Compound row variants --------------------------------------------------

    // Spec member names — height as F32, trend as uint8.
    public struct SpecRow
    {
        [H5Name("waterLevelHeight")] public float WaterLevelHeight;
        [H5Name("waterLevelTrend")] public byte WaterLevelTrend;
    }

    // S-104 Ed 2.0.0 shape with the optional per-record uncertainty (Table 10-3).
    public struct UncertaintyRow
    {
        [H5Name("waterLevelHeight")] public float WaterLevelHeight;
        [H5Name("waterLevelTrend")] public byte WaterLevelTrend;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    // The instance-level uncertainty dataset (S-104 Ed 2.0.0 §10.2.2.4).
    public struct InstanceUncertaintyRow
    {
        [H5Name("name")] public string Name;
        [H5Name("value")] public float Value;
    }

    // UKHO dcf2 shape — height as F32, trend as F32.
    public struct UkhoRow
    {
        [H5Name("surfaceHeight")] public float SurfaceHeight;
        [H5Name("trend")] public float Trend;
    }

    // IC-ENC NL shape — spec member names, trend stored as Int16 (issue #254).
    public struct Int16TrendRow
    {
        [H5Name("waterLevelHeight")] public float WaterLevelHeight;
        [H5Name("waterLevelTrend")] public short WaterLevelTrend;
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
        string timePoint = "20210401T000000Z",
        int? verticalDatum = null,
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
            ["horizontalCRS"] = projected?.Epsg ?? 4326,
            ["geographicIdentifier"] = "Test",
            ["issueDate"] = "2021-04-01",
        };
        if (verticalDatum is int vd)
        {
            rootAttributes["verticalDatum"] = vd;
        }
        if (productSpecification is not null)
        {
            rootAttributes["productSpecification"] = productSpecification;
        }

        var file = new H5File
        {
            Attributes = rootAttributes,
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

        if (extraRootAttributes is not null)
        {
            foreach (var (name, value) in extraRootAttributes)
                file.Attributes[name] = value;
        }

        file.Write(path, options);
        return path;
    }

    /// <summary>A geographic regular grid's georeferencing (S-104 Ed 2.0.0 §10.2.2).</summary>
    public readonly record struct GridGeoreference(
        double OriginLatitude,
        double OriginLongitude,
        double SpacingLatitudinal,
        double SpacingLongitudinal,
        int NumPointsLatitudinal,
        int NumPointsLongitudinal);

    /// <summary>
    /// Writes a dcf2 file with one <c>WaterLevel.01</c> instance holding a
    /// <c>Group_NNN</c> per time step, in the order given. Attributes are
    /// written in dictionary order after the ones this method sets, so the
    /// same arguments give the same bytes.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="steps">Each step's <c>timePoint</c> (for example <c>20260301T000000Z</c>) and row-major values, south row first.</param>
    /// <param name="rootAttributes">Root attributes (product specification, CRS, datum, extent, issue date, …).</param>
    /// <param name="featureAttributes">Extra <c>/WaterLevel</c> attributes after <c>dataCodingFormat</c>, or <c>null</c>.</param>
    /// <param name="instanceAttributes">Extra <c>/WaterLevel/WaterLevel.01</c> attributes after the grid ones, or <c>null</c>.</param>
    /// <param name="instanceUncertainty">The instance-level <c>uncertainty</c> dataset's value for <c>waterLevelHeight</c>, or <c>null</c> to omit the dataset.</param>
    /// <returns><paramref name="path"/>.</returns>
    public static string WriteTimeSeries<TRow>(
        string path,
        GridGeoreference grid,
        IReadOnlyList<(string TimePoint, TRow[] Values)> steps,
        IReadOnlyDictionary<string, object> rootAttributes,
        IReadOnlyDictionary<string, object>? featureAttributes = null,
        IReadOnlyDictionary<string, object>? instanceAttributes = null,
        float? instanceUncertainty = null)
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
        if (instanceUncertainty is { } u)
            instance["uncertainty"] = new[] { new InstanceUncertaintyRow { Name = "waterLevelHeight", Value = u } };

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
            ["WaterLevel.01"] = instance,
        };
        foreach (var (name, value) in featureAttributes ?? new Dictionary<string, object>())
            feature.Attributes[name] = value;

        var file = new H5File { ["WaterLevel"] = feature };
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
