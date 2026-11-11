using System.Collections.ObjectModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// A feature referencing several surfaces (S-100 Part 10a SPAS, 0..*) is
/// resolved into one <see cref="SurfacePart"/> per surface, each with its own
/// holes (issue #643). Producer cells do this for depth areas split by the cell
/// boundary, among others.
/// </summary>
public sealed class S101SurfacePartsTests
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmSurface = 130;
    private const byte OrientForward = 1;
    private const byte UsageExterior = 1;
    private const byte UsageInterior = 2;

    [Fact]
    public void TwoSurfaces_BecomeTwoParts_EachWithItsOwnHoles()
    {
        var feature = Assert.Single(new S101VectorSource(S101Dataset.FromDocument(BuildDocument(20, 21))).GetFeatures());

        Assert.Equal(2, feature.SurfaceParts.Count);
        Assert.Equal(5, feature.SurfaceParts[0].ExteriorRing.Count);
        Assert.Single(feature.SurfaceParts[0].InteriorRings);
        Assert.Empty(feature.SurfaceParts[1].InteriorRings);
        Assert.Equal(2, ((IS100Feature)feature).Surfaces.Count);

        // The joined exterior and all the holes are kept for existing consumers.
        Assert.Equal(10, feature.Coordinates.Count);
        Assert.Single(feature.InteriorRings);
    }

    [Fact]
    public void OneSurface_HasNoParts()
    {
        var feature = Assert.Single(new S101VectorSource(S101Dataset.FromDocument(BuildDocument(20))).GetFeatures());

        Assert.Empty(feature.SurfaceParts);
        Assert.Single(((IS100Feature)feature).Surfaces);
    }

    // Surface 20: a 10×10 square with a 2..4 hole; surface 21: a square at 20..30.
    private static S101Document BuildDocument(params uint[] surfaces)
    {
        S101CurveSegmentRecord Ring(uint id, uint point, (int, int)[] via) => new()
        {
            RecordId = id,
            PointAssociations = [new S101PointAssociation(RcnmPoint, point, 1), new S101PointAssociation(RcnmPoint, point, 2)],
            IntermediateCoordinates = via,
        };

        var points = new[]
        {
            new S101PointRecord { RecordId = 1, Y = 0, X = 0 },
            new S101PointRecord { RecordId = 2, Y = 2, X = 2 },
            new S101PointRecord { RecordId = 3, Y = 0, X = 20 },
        };
        var curves = new[]
        {
            Ring(10, 1, [(0, 10), (10, 10), (10, 0)]),
            Ring(11, 2, [(2, 4), (4, 4), (4, 2)]),
            Ring(12, 3, [(0, 30), (10, 30), (10, 20)]),
        };
        var surfaceRecords = new[]
        {
            new S101SurfaceRecord
            {
                RecordId = 20,
                RingAssociations = [
                    new S101RingAssociation(RcnmCurveSegment, 10, OrientForward, UsageExterior),
                    new S101RingAssociation(RcnmCurveSegment, 11, OrientForward, UsageInterior)],
            },
            new S101SurfaceRecord
            {
                RecordId = 21,
                RingAssociations = [new S101RingAssociation(RcnmCurveSegment, 12, OrientForward, UsageExterior)],
            },
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
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [42] = "DepthArea" }.ToDictionary(),
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = points.ToDictionary(p => p.RecordId),
            CurveSegments = curves.ToDictionary(c => c.RecordId),
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = surfaceRecords.ToDictionary(s => s.RecordId),
            Features =
            [
                new S101FeatureRecord
                {
                    RecordId = 100,
                    FeatureTypeCode = 42,
                    Attributes = [],
                    SpatialAssociations = surfaces.Select(s => new S101SpatialAssociation(RcnmSurface, s, OrientForward)).ToList(),
                    FeatureAssociations = [],
                    InformationAssociations = [],
                },
            ],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
