using System.Collections.ObjectModel;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Tests for the index behind the S-100 Part 13 host function
/// <c>HostSpatialGetAssociatedFeatureIDs</c>. The S-101 rules call it for every
/// point feature (<c>GetColocatedTextCount</c>) and every edge of every depth
/// area (<c>DEPARE03</c>, to find the safety contour), so it must be a lookup
/// rather than a scan of every feature. It must match the record by record name
/// as well as id (point 5 and curve 5 are different records), and include the
/// features that reach an edge through a surface ring or composite curve.
/// </summary>
public sealed class S101SpatialFeatureIndexTests
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;

    [Fact]
    public void MatchesRecordNameAndId()
    {
        var index = S101LuaDataProvider.BuildFeatureIdsBySpatial(Document(
            Feature(1, (RcnmPoint, 5)),
            Feature(2, (RcnmCurveSegment, 5)),
            Feature(3, (RcnmPoint, 5))));

        Assert.Equal([1.0, 3.0], index[(RcnmPoint, 5)]);
        Assert.Equal([2.0], index[(RcnmCurveSegment, 5)]);
        Assert.False(index.ContainsKey((RcnmPoint, 6)));
    }

    [Fact]
    public void ListsAFeatureOnce_InDatasetOrder()
    {
        // A feature can reference the same edge twice (an area whose ring
        // doubles back); the host returned it once, in dataset order.
        var index = S101LuaDataProvider.BuildFeatureIdsBySpatial(Document(
            Feature(9, (RcnmCurveSegment, 1), (RcnmCurveSegment, 1)),
            Feature(4, (RcnmCurveSegment, 1))));

        Assert.Equal([9.0, 4.0], index[(RcnmCurveSegment, 1)]);
    }

    [Fact]
    public void IncludesFeaturesThatUseTheRecordThroughSurfacesAndCompositeCurves()
    {
        // Two depth areas share edge 1: area 1's surface ring names the edge,
        // area 2's ring is composite curve 7, which contains it.
        var index = S101LuaDataProvider.BuildFeatureIdsBySpatial(Document(
            [Feature(1, (RcnmSurface, 10)), Feature(2, (RcnmSurface, 11)), Feature(3, (RcnmCurveSegment, 1))],
            surfaces: [Surface(10, (RcnmCurveSegment, 1)), Surface(11, (RcnmCompositeCurve, 7))],
            compositeCurves: [CompositeCurve(7, (RcnmCurveSegment, 1), (RcnmCurveSegment, 2))]));

        Assert.Equal([1.0, 2.0, 3.0], index[(RcnmCurveSegment, 1)]);
        Assert.Equal([2.0], index[(RcnmCurveSegment, 2)]);
        Assert.Equal([2.0], index[(RcnmCompositeCurve, 7)]);
        Assert.Equal([1.0], index[(RcnmSurface, 10)]);
    }

    [Fact]
    public void CyclicCompositeCurves_DoNotRecurseForever()
    {
        var index = S101LuaDataProvider.BuildFeatureIdsBySpatial(Document(
            [Feature(1, (RcnmCompositeCurve, 7))],
            compositeCurves: [CompositeCurve(7, (RcnmCompositeCurve, 8)), CompositeCurve(8, (RcnmCompositeCurve, 7))]));

        Assert.Equal([1.0], index[(RcnmCompositeCurve, 8)]);
    }

    private static S101SurfaceRecord Surface(uint id, (byte Rcnm, uint Rcid) exterior) => new()
    {
        RecordId = id,
        RingAssociations = [new S101RingAssociation(exterior.Rcnm, exterior.Rcid, Orientation: 1, Usage: 1)],
    };

    private static S101CompositeCurveRecord CompositeCurve(uint id, params (byte Rcnm, uint Rcid)[] components) => new()
    {
        RecordId = id,
        CurveComponents = [.. components.Select(c => new S101CurveUsage(c.Rcnm, c.Rcid, Orientation: 1))],
    };

    private static S101FeatureRecord Feature(uint id, params (byte Rcnm, uint Rcid)[] spatials) => new()
    {
        RecordId = id,
        SpatialAssociations = [.. spatials.Select(s => new S101SpatialAssociation(s.Rcnm, s.Rcid, Orientation: 1))],
    };

    private static S101Document Document(params S101FeatureRecord[] features) => Document(features, [], []);

    private static S101Document Document(
        S101FeatureRecord[] features,
        S101SurfaceRecord[]? surfaces = null,
        S101CompositeCurveRecord[]? compositeCurves = null) => new()
    {
        Identification = new S101DatasetIdentification { DatasetName = "INDEX.000" },
        StructureInfo = new S101DatasetStructureInfo(),
        FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
        CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
        CompositeCurves = (compositeCurves ?? []).ToDictionary(c => c.RecordId),
        Surfaces = (surfaces ?? []).ToDictionary(s => s.RecordId),
        Features = features,
        InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
        InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
    };
}
