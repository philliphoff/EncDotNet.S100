using System.Collections.ObjectModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// A curve feature whose spatial associations (S-100 Part 10a SPAS, 0..*) do not
/// all meet end to start is resolved into separate parts by
/// <see cref="S101VectorSource"/>, so that renderers and query tools do not draw
/// or measure a segment across the gap (issue #643). Producer cells do this for
/// depth contours clipped by the cell boundary, among others.
/// </summary>
public sealed class S101CurvePartsTests
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte OrientForward = 1;
    private const byte OrientReverse = 2;
    private const byte TopologyBegin = 1;
    private const byte TopologyEnd = 2;

    [Fact]
    public void DisconnectedCurveAssociations_BecomeParts()
    {
        // Curve 10: (0,0)→(0,1); curve 12: (5,0)→(5,1), which 10 does not reach.
        var feature = SingleFeature(BuildDocument((10, OrientForward), (12, OrientForward)));

        Assert.Equal(2, feature.Curves.Count);
        Assert.Equal(2, ((IS100Feature)feature).Curves.Count);
        Assert.Equal(4, feature.Coordinates.Count); // still the joined sequence

        var geometry = new FeatureGeometryProvider<Feature>([feature]).GetGeometry("100")!;
        Assert.Equal(2, geometry.Parts.Count);
        Assert.Equal(new[] { new GeoPosition(0, 0), new GeoPosition(0, 1) }, geometry.Parts[0]);
        Assert.Equal(new[] { new GeoPosition(5, 0), new GeoPosition(5, 1) }, geometry.Parts[1]);
    }

    [Fact]
    public void ChainedCurveAssociations_StayOneCurve()
    {
        // Curve 10 (0,0)→(0,1) then curve 11 (0,1)→(0,2): an ordinary line split
        // at a shared node, the way S-57-derived edges are encoded.
        var feature = SingleFeature(BuildDocument((10, OrientForward), (11, OrientForward)));

        Assert.Empty(feature.Curves);
        Assert.Single(((IS100Feature)feature).Curves);
        Assert.Empty(new FeatureGeometryProvider<Feature>([feature]).GetGeometry("100")!.Parts);
    }

    [Fact]
    public void ReversedAssociation_ThatStillMeets_StaysOneCurve()
    {
        // Curve 11 reversed runs (0,2)→(0,1), then curve 10 reversed (0,1)→(0,0).
        var feature = SingleFeature(BuildDocument((11, OrientReverse), (10, OrientReverse)));

        Assert.Empty(feature.Curves);
    }

    private static Feature SingleFeature(S101Document doc)
        => Assert.Single(new S101VectorSource(S101Dataset.FromDocument(doc)).GetFeatures(), f => f.Id == 100);

    private static S101Document BuildDocument(params (uint CurveId, byte Orientation)[] associations)
    {
        var points = new[]
        {
            new S101PointRecord { RecordId = 1, Y = 0, X = 0 },
            new S101PointRecord { RecordId = 2, Y = 0, X = 1 },
            new S101PointRecord { RecordId = 3, Y = 0, X = 2 },
            new S101PointRecord { RecordId = 4, Y = 5, X = 0 },
            new S101PointRecord { RecordId = 5, Y = 5, X = 1 },
        };

        static S101CurveSegmentRecord Curve(uint id, uint begin, uint end) => new()
        {
            RecordId = id,
            PointAssociations = [
                new S101PointAssociation(RcnmPoint, begin, TopologyBegin),
                new S101PointAssociation(RcnmPoint, end, TopologyEnd)],
            IntermediateCoordinates = [],
        };

        var curves = new[] { Curve(10, 1, 2), Curve(11, 2, 3), Curve(12, 4, 5) };

        var feature = new S101FeatureRecord
        {
            RecordId = 100,
            FeatureTypeCode = 42,
            Attributes = [],
            SpatialAssociations = associations
                .Select(a => new S101SpatialAssociation(RcnmCurveSegment, a.CurveId, a.Orientation))
                .ToList(),
            FeatureAssociations = [],
            InformationAssociations = [],
        };

        return new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "TEST.000" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 1,
                CoordinateMultiplicationFactorY = 1,
                CoordinateMultiplicationFactorZ = 1,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [42] = "DepthContour" }.ToDictionary(),
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = points.ToDictionary(p => p.RecordId),
            CurveSegments = curves.ToDictionary(c => c.RecordId),
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = [feature],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
