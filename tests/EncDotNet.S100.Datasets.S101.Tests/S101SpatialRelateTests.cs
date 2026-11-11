using System.Collections.ObjectModel;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Tests for <see cref="S101SpatialRelate"/>, the host side of the S-100
/// Part 13 <c>HostSpatialRelate</c> function. The S-401 <c>MARSYS01</c> rule
/// uses <c>T*****FF*</c> to find the <c>NavigationalSystemOfMarks</c> around a
/// buoy; without the host function every lateral buoy in a European inland
/// cell fell back to Default (question mark) symbology.
/// </summary>
public sealed class S101SpatialRelateTests
{
    private const string Covers = "T*****FF*";

    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmSurface = 130;
    private const byte OrientForward = 1;
    private const byte UsageExterior = 1;
    private const byte UsageInterior = 2;

    // Surface 20: the square 0..100 with the hole 40..60.
    private const string Area = "130:20";

    [Theory]
    [InlineData("110:1", true)]   // inside
    [InlineData("110:2", false)]  // on the exterior ring: the interiors don't meet
    [InlineData("110:3", false)]  // outside
    [InlineData("110:4", false)]  // inside the hole
    public void Covers_Point(string point, bool expected)
    {
        Assert.Equal(expected, Relate(Area, point, Covers));
    }

    [Theory]
    [InlineData("120:30", true)]  // inside
    [InlineData("120:31", false)] // leaves through the exterior ring
    [InlineData("120:32", false)] // runs through the hole
    public void Covers_Curve(string curve, bool expected)
    {
        Assert.Equal(expected, Relate(Area, curve, Covers));
    }

    [Theory]
    [InlineData("130:21", true)]  // a small square inside
    [InlineData("130:22", false)] // a square around the hole
    [InlineData("130:20", true)]  // itself
    public void Covers_Surface(string surface, bool expected)
    {
        Assert.Equal(expected, Relate(Area, surface, Covers));
    }

    [Fact]
    public void Pattern_IsCaseInsensitive()
    {
        Assert.Equal(true, Relate(Area, "110:1", "t*****ff*"));
    }

    [Fact]
    public void UnsupportedPattern_ReturnsError()
    {
        var error = Assert.IsType<string>(Relate(Area, "110:1", "T*F**F***"));
        Assert.Contains("T*F**F***", error);
        Assert.Contains("not supported", error);
    }

    [Fact]
    public void NonSurfaceFirstSpatial_ReturnsError()
    {
        var error = Assert.IsType<string>(Relate("110:1", Area, Covers));
        Assert.Contains("surface", error);
    }

    [Fact]
    public void UnknownSpatial_ReturnsError()
    {
        var error = Assert.IsType<string>(Relate(Area, "110:999", Covers));
        Assert.Contains("110:999", error);
    }

    private static object Relate(string a, string b, string pattern) =>
        new S101SpatialRelate(BuildDocument()).Relate(a, b, pattern);

    private static S101Document BuildDocument()
    {
        var points = new Dictionary<uint, S101PointRecord>();
        var curves = new Dictionary<uint, S101CurveSegmentRecord>();
        var surfaces = new Dictionary<uint, S101SurfaceRecord>();
        uint nextPoint = 1000;

        void AddPoint(uint id, int x, int y) => points[id] = new S101PointRecord { RecordId = id, X = x, Y = y };

        void AddCurve(uint id, params (int X, int Y)[] coords)
        {
            uint begin = nextPoint++, end = nextPoint++;
            AddPoint(begin, coords[0].X, coords[0].Y);
            AddPoint(end, coords[^1].X, coords[^1].Y);
            curves[id] = new S101CurveSegmentRecord
            {
                RecordId = id,
                PointAssociations = [new(RcnmPoint, begin, 1), new(RcnmPoint, end, 2)],
                IntermediateCoordinates = coords[1..^1].Select(c => (c.Y, c.X)).ToList(),
            };
        }

        void AddSquare(uint curveId, int min, int max) =>
            AddCurve(curveId, (min, min), (max, min), (max, max), (min, max), (min, min));

        void AddSurface(uint id, uint exterior, uint? interior = null)
        {
            var rings = new List<S101RingAssociation> { new(RcnmCurveSegment, exterior, OrientForward, UsageExterior) };
            if (interior is { } hole)
                rings.Add(new(RcnmCurveSegment, hole, OrientForward, UsageInterior));
            surfaces[id] = new S101SurfaceRecord { RecordId = id, RingAssociations = rings };
        }

        AddPoint(1, 10, 10);
        AddPoint(2, 0, 50);
        AddPoint(3, 150, 50);
        AddPoint(4, 50, 50);

        AddCurve(30, (10, 10), (30, 30));
        AddCurve(31, (10, 10), (150, 10));
        AddCurve(32, (10, 50), (90, 50));

        AddSquare(10, 0, 100);
        AddSquare(11, 40, 60);
        AddSurface(20, exterior: 10, interior: 11);

        AddSquare(12, 5, 30);
        AddSurface(21, exterior: 12);

        AddSquare(13, 30, 70);
        AddSurface(22, exterior: 13);

        return new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "RELATE.000" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 1,
                CoordinateMultiplicationFactorY = 1,
                CoordinateMultiplicationFactorZ = 1,
            },
            FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = points,
            CurveSegments = curves,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = surfaces,
            Features = [],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
