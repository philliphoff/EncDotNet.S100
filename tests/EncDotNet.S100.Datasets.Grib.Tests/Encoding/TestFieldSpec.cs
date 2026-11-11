namespace EncDotNet.S100.Datasets.Grib.Tests.Encoding;

/// <summary>A grid for template 3.0, in degrees.</summary>
public sealed record TestGrid(
    int Ni,
    int Nj,
    double FirstLatitude,
    double FirstLongitude,
    double LastLatitude,
    double LastLongitude,
    double IIncrement,
    double JIncrement,
    byte ScanningMode = 0,
    bool IncrementsGiven = true,
    int TemplateNumber = 0)
{
    /// <summary>A small grid of <paramref name="ni"/> × <paramref name="nj"/> points at 0.5°, rows running south from 50°N.</summary>
    public static TestGrid Small(int ni = 7, int nj = 5) =>
        new(ni, nj, 50, 300, 50 - ((nj - 1) * 0.5), 300 + ((ni - 1) * 0.5), 0.5, 0.5);
}

/// <summary>How the values are packed (Section 5 and 7).</summary>
public abstract record TestPacking(int DecimalScale, int BinaryScale)
{
    /// <summary>Overrides the reference value R (otherwise the largest float not above the minimum).</summary>
    public float? Reference { get; init; }
}

/// <summary>Template 5.0. <c>Bits</c> null picks the fewest that hold the range.</summary>
public sealed record SimpleTestPacking(int DecimalScale = 0, int BinaryScale = 0, int? Bits = null)
    : TestPacking(DecimalScale, BinaryScale);

/// <summary>Template 5.2 (order 0) or 5.3 (order 1 or 2), in groups of <c>GroupSize</c> values.</summary>
public sealed record ComplexTestPacking(
    int DecimalScale = 0,
    int BinaryScale = 0,
    int GroupSize = 8,
    int MissingValueManagement = 0,
    int SpatialDifferencingOrder = 0)
    : TestPacking(DecimalScale, BinaryScale);

/// <summary>Template 5.41, with an image <c>Depth</c> of 1 to 32 bits.</summary>
public sealed record PngTestPacking(int DecimalScale = 0, int BinaryScale = 0, int Depth = 16)
    : TestPacking(DecimalScale, BinaryScale);

/// <summary>Any data template, written as given: the template body (octets 12 on) and Section 7's body.</summary>
public sealed record RawTestPacking(int TemplateNumber, byte[] TemplateBody, byte[] Data, int PackedValues)
    : TestPacking(0, 0);

/// <summary>How the field's bitmap is written.</summary>
public enum TestBitmap
{
    /// <summary>No bitmap (indicator 255); missing values go in the packed data, if the packing allows.</summary>
    None,

    /// <summary>A bitmap (indicator 0) marking the NaN values missing.</summary>
    Explicit,

    /// <summary>Reuse the message's previous bitmap (indicator 254).</summary>
    ReusePrevious,
}

/// <summary>One field of a test message.</summary>
/// <remarks>
/// <see cref="Values"/> are in the file's scanning order. <see cref="double.NaN"/>
/// is missing; <see cref="double.NegativeInfinity"/> is a secondary missing
/// value, for complex packing with missing value management 2.
/// </remarks>
public sealed record TestField
{
    public byte Discipline { get; init; }

    public byte Category { get; init; } = 2;

    public byte Number { get; init; } = 2;

    public DateTime ReferenceTime { get; init; } = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    public ushort Centre { get; init; } = 7;

    public TestGrid Grid { get; init; } = TestGrid.Small();

    public int ProductTemplate { get; init; }

    public byte TimeUnit { get; init; } = 1;

    public int ForecastTime { get; init; } = 3;

    public byte SurfaceType { get; init; } = 103;

    public int SurfaceScale { get; init; }

    public int SurfaceValue { get; init; } = 10;

    /// <summary>For template 4.8: the end of the interval, and the process type and length in hours.</summary>
    public DateTime IntervalEnd { get; init; } = new(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc);

    public byte StatisticalProcess { get; init; } = 2;

    public int StatisticalLength { get; init; } = 3;

    public TestPacking Packing { get; init; } = new SimpleTestPacking();

    public TestBitmap Bitmap { get; init; }

    public required double[] Values { get; init; }
}
