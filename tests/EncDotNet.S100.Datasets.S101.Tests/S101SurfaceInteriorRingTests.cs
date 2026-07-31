using System.Collections.ObjectModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;

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
            new[] { new GeoPosition(0, 0), new GeoPosition(0, 10), new GeoPosition(10, 10), new GeoPosition(10, 0), new GeoPosition(0, 0) },
            feature.Coordinates);

        // Interior ring: the 2..4 square hole carried through as a single hole.
        var hole = Assert.Single(feature.InteriorRings);
        Assert.Equal(
            new[] { new GeoPosition(2, 2), new GeoPosition(2, 4), new GeoPosition(4, 4), new GeoPosition(4, 2), new GeoPosition(2, 2) },
            hole);
    }

    [Fact]
    public void GeometryProvider_SurfaceWithInteriorRing_ExposesHoleToRenderers()
    {
        var dataset = S101Dataset.FromDocument(BuildDocumentWithHole());

        var geometry = new FeatureGeometryProvider<Feature>(new S101VectorSource(dataset).GetFeatures()).GetGeometry("100");

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
            PointAssociations = [
                new S101PointAssociation(RcnmPoint, 1, TopologyBegin),
                new S101PointAssociation(RcnmPoint, 1, TopologyEnd)],
            IntermediateCoordinates = [(0, 10), (10, 10), (10, 0)],
        };

        var interior = new S101CurveSegmentRecord
        {
            RecordId = 11,
            PointAssociations = [
                new S101PointAssociation(RcnmPoint, 2, TopologyBegin),
                new S101PointAssociation(RcnmPoint, 2, TopologyEnd)],
            IntermediateCoordinates = [(2, 4), (4, 4), (4, 2)],
        };

        var surface = new S101SurfaceRecord
        {
            RecordId = 20,
            RingAssociations = [
                new S101RingAssociation(RcnmCurveSegment, 10, OrientForward, UsageExterior),
                new S101RingAssociation(RcnmCurveSegment, 11, OrientForward, UsageInterior)],
        };

        var feature = new S101FeatureRecord
        {
            RecordId = 100,
            FeatureTypeCode = 42,
            Attributes = [],
            SpatialAssociations = [
                new S101SpatialAssociation(RcnmSurface, 20, OrientForward)],
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
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [42] = "DepthArea" }.ToDictionary(),
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = points.ToDictionary(p => p.RecordId),
            CurveSegments = new[] { exterior, interior }.ToDictionary(c => c.RecordId),
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = new[] { surface }.ToDictionary(s => s.RecordId),
            Features = [feature],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
