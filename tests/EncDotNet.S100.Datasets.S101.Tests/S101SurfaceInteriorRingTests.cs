using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using Xunit;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Regression tests for surface geometry resolution in <see cref="S101VectorSource"/>,
/// specifically that interior rings (holes) declared by a surface's RIAS field
/// (USAG = 2) are carried through to <see cref="Feature.InteriorRings"/>.
/// </summary>
/// <remarks>
/// Motivated by real IC-ENC Faroe Islands cells where a single sea/depth area
/// (S-100 Part 10a surface topology) covers the whole cell and cuts the islands
/// out as interior rings. Dropping those holes made the depth fill paint solidly
/// over every <c>LandArea</c>, so the land never appeared. See S-101 Annex A /
/// S-100 Part 10a §surface topology.
/// </remarks>
public sealed class S101SurfaceInteriorRingTests
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmSurface = 130;
    private const byte OrientForward = 1;
    private const byte UsageExterior = 1;
    private const byte UsageInterior = 2;
    private const byte TopologyBegin = 1;
    private const byte TopologyEnd = 2;

    [Fact]
    public void GetFeatures_SurfaceWithInteriorRing_PopulatesInteriorRings()
    {
        var dataset = S101Dataset.FromDocument(BuildDocumentWithHole());

        var feature = Assert.Single(
            new S101VectorSource(dataset).GetFeatures(),
            f => f.Id == 100);

        Assert.Equal(GeometryType.Surface, feature.GeometryType);

        // Exterior ring: a 10×10 square closed back to the origin.
        Assert.Equal(
            new (double, double)[] { (0, 0), (0, 10), (10, 10), (10, 0), (0, 0) },
            feature.Coordinates);

        // Interior ring: the 2..4 square hole carried through as a single hole.
        var hole = Assert.Single(feature.InteriorRings);
        Assert.Equal(
            new (double, double)[] { (2, 2), (2, 4), (4, 4), (4, 2), (2, 2) },
            hole);
    }

    [Fact]
    public void GeometryProvider_SurfaceWithInteriorRing_ExposesHoleToRenderers()
    {
        var dataset = S101Dataset.FromDocument(BuildDocumentWithHole());

        var geometry = new S101FeatureGeometryProvider(dataset).GetGeometry("100");

        Assert.NotNull(geometry);
        Assert.Equal(GeometryType.Surface, geometry!.Type);
        var hole = Assert.Single(geometry.InteriorRings);
        Assert.Equal(5, hole.Count);
    }

    private static S101Document BuildDocumentWithHole()
    {
        var points = new[]
        {
            new S101PointRecord { RecordId = 1, Y = 0, X = 0 },  // exterior begin/end
            new S101PointRecord { RecordId = 2, Y = 2, X = 2 },  // interior begin/end
        };

        var exterior = new S101CurveSegmentRecord
        {
            RecordId = 10,
            PointAssociations = ImmutableArray.Create(
                new S101PointAssociation(RcnmPoint, 1, TopologyBegin),
                new S101PointAssociation(RcnmPoint, 1, TopologyEnd)),
            IntermediateCoordinates = ImmutableArray.Create((0, 10), (10, 10), (10, 0)),
        };

        var interior = new S101CurveSegmentRecord
        {
            RecordId = 11,
            PointAssociations = ImmutableArray.Create(
                new S101PointAssociation(RcnmPoint, 2, TopologyBegin),
                new S101PointAssociation(RcnmPoint, 2, TopologyEnd)),
            IntermediateCoordinates = ImmutableArray.Create((2, 4), (4, 4), (4, 2)),
        };

        var surface = new S101SurfaceRecord
        {
            RecordId = 20,
            RingAssociations = ImmutableArray.Create(
                new S101RingAssociation(RcnmCurveSegment, 10, OrientForward, UsageExterior),
                new S101RingAssociation(RcnmCurveSegment, 11, OrientForward, UsageInterior)),
        };

        var feature = new S101FeatureRecord
        {
            RecordId = 100,
            FeatureTypeCode = 42,
            Attributes = ImmutableArray<S101Attribute>.Empty,
            SpatialAssociations = ImmutableArray.Create(
                new S101SpatialAssociation(RcnmSurface, 20, OrientForward)),
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
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
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [42] = "DepthArea" }.ToImmutableDictionary(),
            AttributeTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            Points = points.ToImmutableDictionary(p => p.RecordId),
            CurveSegments = new[] { exterior, interior }.ToImmutableDictionary(c => c.RecordId),
            CompositeCurves = ImmutableDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = new[] { surface }.ToImmutableDictionary(s => s.RecordId),
            Features = ImmutableArray.Create(feature),
            InformationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };
    }
}
