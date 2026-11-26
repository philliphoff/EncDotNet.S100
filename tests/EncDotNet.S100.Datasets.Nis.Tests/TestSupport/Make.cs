namespace EncDotNet.S100.Datasets.Nis.Tests.TestSupport;

/// <summary>Builds small NIS features for the translator tests.</summary>
internal static class Make
{
    public const string Cell = "XX5TEST1.000";

    private static long _nextId = 1;

    public static NisFeature Feature(
        string objectClass,
        NisGeometry? geometry,
        string? longName = null,
        IReadOnlyDictionary<string, object>? attributes = null,
        string? cell = Cell)
        => new()
        {
            FeatureClass = "Test",
            ObjectClass = objectClass,
            ObjectId = Interlocked.Increment(ref _nextId),
            LongName = longName,
            DatasetName = cell,
            CompilationScale = 20000,
            Geometry = geometry,
            Attributes = attributes ?? new Dictionary<string, object>(),
        };

    public static NisGeometry Point(double lon, double lat, double? z = null)
        => NisGeometry.FromPoint(new NisPosition(lon, lat, z));

    public static NisGeometry Line(params (double Lon, double Lat)[] positions)
        => new(NisGeometryKind.Line, [positions.Select(p => new NisPosition(p.Lon, p.Lat)).ToArray()]);

    /// <summary>An area from rings given as lon/lat pairs; each ring is closed here.</summary>
    public static NisGeometry Area(params (double Lon, double Lat)[][] rings)
        => new(NisGeometryKind.Area, rings.Select(r => (IReadOnlyList<NisPosition>)r.Append(r[0]).Select(p => new NisPosition(p.Lon, p.Lat)).ToArray()).ToArray());

    /// <summary>A clockwise square ring with its south-west corner at (lon, lat).</summary>
    public static (double, double)[] Square(double lon, double lat, double size)
        => [(lon, lat), (lon, lat + size), (lon + size, lat + size), (lon + size, lat)];

    /// <summary>An anticlockwise square ring, for a hole.</summary>
    public static (double, double)[] SquareHole(double lon, double lat, double size)
        => Square(lon, lat, size).Reverse().ToArray();

    public static Dictionary<string, object> Attributes(params (string Field, object Value)[] values)
        => values.ToDictionary(v => v.Field, v => v.Value, StringComparer.OrdinalIgnoreCase);

    public static NisToS57Options Options(bool coverage = false)
        => new() { IssueDate = new DateOnly(2026, 11, 26), SynthesiseCoverage = coverage };
}
