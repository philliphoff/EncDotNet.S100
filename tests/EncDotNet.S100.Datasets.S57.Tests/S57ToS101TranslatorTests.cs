using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

public class S57ToS101TranslatorTests
{
    private const byte RcnmIsolatedNode = 110;
    private const byte RcnmConnectedNode = 120;
    private const byte RcnmEdge = 130;

    private static S57Document BuildDocument(
        ImmutableDictionary<S57Name, S57VectorRecord>? vectorRecords = null,
        ImmutableArray<S57FeatureRecord>? features = null,
        uint comf = 10_000_000,
        uint somf = 10)
        => new()
        {
            Identification = new S57DatasetIdentification
            {
                DatasetName = "TEST.000",
                Edition = "1",
                UpdateNumber = "0",
                IssueDate = "20240101",
            },
            Parameters = new S57DatasetParameters
            {
                CompilationScale = 50_000,
                CoordinateMultiplicationFactor = comf,
                SoundingMultiplicationFactor = somf,
            },
            VectorRecords = vectorRecords ?? ImmutableDictionary<S57Name, S57VectorRecord>.Empty,
            Features = features ?? ImmutableArray<S57FeatureRecord>.Empty,
        };

    private static (S57Name name, S57VectorRecord record) Node(uint id, int y, int x, byte rcnm = RcnmConnectedNode)
    {
        var rec = new S57VectorRecord
        {
            RecordName = rcnm,
            RecordId = id,
            Pointers = ImmutableArray<S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray.Create((y, x)),
            Coordinates3D = ImmutableArray<(int, int, int)>.Empty,
            Attributes = ImmutableArray<S57Attribute>.Empty,
        };
        return (new S57Name(rcnm, id), rec);
    }

    private static (S57Name name, S57VectorRecord record) Edge(
        uint id, uint beginNodeId, uint endNodeId,
        params (int Y, int X)[] intermediates)
    {
        var rec = new S57VectorRecord
        {
            RecordName = RcnmEdge,
            RecordId = id,
            Pointers = ImmutableArray.Create(
                new S57VectorPointer(RcnmConnectedNode, beginNodeId, 1, 0, 1, 255),
                new S57VectorPointer(RcnmConnectedNode, endNodeId, 1, 0, 2, 255)),
            Coordinates2D = intermediates.Length == 0
                ? ImmutableArray<(int, int)>.Empty
                : intermediates.ToImmutableArray(),
            Coordinates3D = ImmutableArray<(int, int, int)>.Empty,
            Attributes = ImmutableArray<S57Attribute>.Empty,
        };
        return (new S57Name(RcnmEdge, id), rec);
    }

    [Fact]
    public void Translate_NodeBecomesPointRecord()
    {
        var (n1, r1) = Node(1, 100, 200);
        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty.Add(n1, r1));

        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Single(s101.Points);
        var pt = s101.Points.Values.Single();
        Assert.Equal(100, pt.Y);
        Assert.Equal(200, pt.X);
    }

    [Fact]
    public void Translate_EdgeBecomesCurveSegmentWithBeginEndAssociations()
    {
        var (n1, r1) = Node(1, 0, 0);
        var (n2, r2) = Node(2, 100, 100);
        var (e1, e1r) = Edge(10, 1, 2, (50, 50));

        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty
                .Add(n1, r1).Add(n2, r2).Add(e1, e1r));

        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Equal(2, s101.Points.Count);
        var cs = Assert.Single(s101.CurveSegments.Values);
        Assert.Equal(2, cs.PointAssociations.Length);
        Assert.Equal(1, cs.PointAssociations[0].Topology);
        Assert.Equal(2, cs.PointAssociations[1].Topology);
        Assert.Equal((50, 50), cs.IntermediateCoordinates[0]);
    }

    [Fact]
    public void Translate_PointFeature_ReferencesPoint()
    {
        var (n1, r1) = Node(1, 100, 200);
        var feature = new S57FeatureRecord
        {
            RecordId = 1,
            Primitive = 1,
            ObjectClass = 5, // BCNCAR → CardinalBeacon
            ProducingAgency = 540,
            FeatureIdentificationNumber = 1,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S57Attribute>.Empty,
            SpatialPointers = ImmutableArray.Create(
                new S57FeatureSpatialPointer(RcnmConnectedNode, 1, 1, 0, 0)),
        };

        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty.Add(n1, r1),
            features: ImmutableArray.Create(feature));

        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("CardinalBeacon", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(110, sa.RecordName);
    }

    [Fact]
    public void Translate_LineFeature_ReferencesCurveSegments()
    {
        var (n1, r1) = Node(1, 0, 0);
        var (n2, r2) = Node(2, 100, 100);
        var (e1, e1r) = Edge(10, 1, 2);

        var feature = new S57FeatureRecord
        {
            RecordId = 1,
            Primitive = 2,
            ObjectClass = 30, // COALNE → Coastline
            ProducingAgency = 540,
            FeatureIdentificationNumber = 1,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S57Attribute>.Empty,
            SpatialPointers = ImmutableArray.Create(
                new S57FeatureSpatialPointer(RcnmEdge, 10, 1, 0, 0)),
        };

        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty
                .Add(n1, r1).Add(n2, r2).Add(e1, e1r),
            features: ImmutableArray.Create(feature));

        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("Coastline", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(120, sa.RecordName);
    }

    [Fact]
    public void Translate_AreaFeature_BuildsSurfaceWithCompositeCurveExterior()
    {
        var (n1, r1) = Node(1, 0, 0);
        var (n2, r2) = Node(2, 0, 100);
        var (n3, r3) = Node(3, 100, 50);
        var (e1, e1r) = Edge(10, 1, 2);
        var (e2, e2r) = Edge(11, 2, 3);
        var (e3, e3r) = Edge(12, 3, 1);

        var feature = new S57FeatureRecord
        {
            RecordId = 1,
            Primitive = 3,
            ObjectClass = 42, // DEPARE → DepthArea
            ProducingAgency = 540,
            FeatureIdentificationNumber = 1,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray.Create(
                new S57Attribute(87, "10"),
                new S57Attribute(88, "20")),
            SpatialPointers = ImmutableArray.Create(
                new S57FeatureSpatialPointer(RcnmEdge, 10, 1, 1, 0),
                new S57FeatureSpatialPointer(RcnmEdge, 11, 1, 1, 0),
                new S57FeatureSpatialPointer(RcnmEdge, 12, 1, 1, 0)),
        };

        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty
                .Add(n1, r1).Add(n2, r2).Add(n3, r3)
                .Add(e1, e1r).Add(e2, e2r).Add(e3, e3r),
            features: ImmutableArray.Create(feature));

        var s101 = new S57ToS101Translator().Translate(doc);

        var feat = Assert.Single(s101.Features);
        Assert.Equal("DepthArea", s101.FeatureTypeCatalogue[feat.FeatureTypeCode]);
        Assert.Equal(2, feat.Attributes.Length);
        var sa = Assert.Single(feat.SpatialAssociations);
        Assert.Equal(130, sa.RecordName);

        var surface = s101.Surfaces[sa.RecordId];
        var ring = Assert.Single(surface.RingAssociations);
        Assert.Equal(1, ring.Usage);
        Assert.Equal(125, ring.RecordName);

        var composite = s101.CompositeCurves[ring.RecordId];
        Assert.Equal(3, composite.CurveComponents.Length);
    }

    [Fact]
    public void Translate_SoundingFeature_BecomesMultiPointSounding()
    {
        // S-57 SOUNDG (OBJL=129) features are translated into a single S-101
        // Sounding feature backed by a multi-point spatial record (RCNM=115).
        // The depth values live on the points within that record.
        var soundingNode = new S57VectorRecord
        {
            RecordName = RcnmIsolatedNode,
            RecordId = 1,
            Pointers = ImmutableArray<S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray<(int, int)>.Empty,
            Coordinates3D = ImmutableArray.Create(
                (10, 20, 50),
                (30, 40, 75),
                (50, 60, 100)),
            Attributes = ImmutableArray<S57Attribute>.Empty,
        };

        var feature = new S57FeatureRecord
        {
            RecordId = 1,
            Primitive = 1,
            ObjectClass = 129, // SOUNDG
            ProducingAgency = 540,
            FeatureIdentificationNumber = 1,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S57Attribute>.Empty,
            SpatialPointers = ImmutableArray.Create(
                new S57FeatureSpatialPointer(RcnmIsolatedNode, 1, 1, 0, 0)),
        };

        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty
                .Add(new S57Name(RcnmIsolatedNode, 1), soundingNode),
            features: ImmutableArray.Create(feature),
            somf: 10);

        var s101 = new S57ToS101Translator().Translate(doc);

        var s101Feature = Assert.Single(s101.Features);
        var soundingTypeCode = s101.FeatureTypeCatalogue
            .First(kv => kv.Value == "Sounding").Key;
        Assert.Equal(soundingTypeCode, s101Feature.FeatureTypeCode);
        Assert.Empty(s101Feature.Attributes);

        var spa = Assert.Single(s101Feature.SpatialAssociations);
        Assert.Equal((byte)115, spa.RecordName);

        var mp = Assert.Single(s101.MultiPoints.Values);
        Assert.Equal(spa.RecordId, mp.RecordId);
        Assert.Equal(3, mp.Points.Length);
        Assert.Equal((10, 20, 50), mp.Points[0]);
        Assert.Equal((30, 40, 75), mp.Points[1]);
        Assert.Equal((50, 60, 100), mp.Points[2]);

        // Soundings must not pollute the Point record table — only the
        // MultiPoint record is emitted for them.
        Assert.Empty(s101.Points);

        // CMFZ defaults to SOMF (10) so consumers can recover real depth.
        Assert.Equal(10u, s101.StructureInfo.CoordinateMultiplicationFactorZ);
    }

    [Fact]
    public void Translate_SoundingFeature_AcrossMultipleNodes_AggregatesAllPoints()
    {
        var sn1 = new S57VectorRecord
        {
            RecordName = RcnmIsolatedNode,
            RecordId = 1,
            Pointers = ImmutableArray<S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray<(int, int)>.Empty,
            Coordinates3D = ImmutableArray.Create((1, 2, 3), (4, 5, 6)),
            Attributes = ImmutableArray<S57Attribute>.Empty,
        };
        var sn2 = new S57VectorRecord
        {
            RecordName = RcnmIsolatedNode,
            RecordId = 2,
            Pointers = ImmutableArray<S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray<(int, int)>.Empty,
            Coordinates3D = ImmutableArray.Create((7, 8, 9)),
            Attributes = ImmutableArray<S57Attribute>.Empty,
        };

        var feature = new S57FeatureRecord
        {
            RecordId = 1,
            Primitive = 1,
            ObjectClass = 129,
            ProducingAgency = 540,
            FeatureIdentificationNumber = 1,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S57Attribute>.Empty,
            SpatialPointers = ImmutableArray.Create(
                new S57FeatureSpatialPointer(RcnmIsolatedNode, 1, 1, 0, 0),
                new S57FeatureSpatialPointer(RcnmIsolatedNode, 2, 1, 0, 0)),
        };

        var doc = BuildDocument(
            vectorRecords: ImmutableDictionary<S57Name, S57VectorRecord>.Empty
                .Add(new S57Name(RcnmIsolatedNode, 1), sn1)
                .Add(new S57Name(RcnmIsolatedNode, 2), sn2),
            features: ImmutableArray.Create(feature));

        var s101 = new S57ToS101Translator().Translate(doc);

        var mp = Assert.Single(s101.MultiPoints.Values);
        Assert.Equal(3, mp.Points.Length);
    }

    [Fact]
    public void Translate_UnmappedFeatureClass_IsSkipped()
    {
        var feature = new S57FeatureRecord
        {
            RecordId = 1,
            Primitive = 1,
            ObjectClass = 65535,
            ProducingAgency = 540,
            FeatureIdentificationNumber = 1,
            FeatureIdentificationSubdivision = 0,
            Attributes = ImmutableArray<S57Attribute>.Empty,
            SpatialPointers = ImmutableArray<S57FeatureSpatialPointer>.Empty,
        };

        var doc = BuildDocument(features: ImmutableArray.Create(feature));
        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Empty(s101.Features);
    }

    [Fact]
    public void Translate_DocumentMetadataIsSet()
    {
        var doc = BuildDocument(comf: 5_000_000, somf: 100);
        var s101 = new S57ToS101Translator().Translate(doc);

        Assert.Equal("S-101", s101.Identification.ProductSpecification);
        Assert.Equal("TEST.000", s101.Identification.DatasetName);
        Assert.Equal(5_000_000u, s101.StructureInfo.CoordinateMultiplicationFactorX);
        Assert.Equal(5_000_000u, s101.StructureInfo.CoordinateMultiplicationFactorY);
        Assert.Equal(100u, s101.StructureInfo.CoordinateMultiplicationFactorZ);
    }
}
