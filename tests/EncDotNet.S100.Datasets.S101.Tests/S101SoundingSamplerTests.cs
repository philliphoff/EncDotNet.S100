using System.Collections.ObjectModel;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Tests for <see cref="S101SoundingSampler"/>, which returns the nearest
/// individual charted sounding (per-point Z) to an arbitrary position.
/// </summary>
public sealed class S101SoundingSamplerTests
{
    private const ushort SoundingCode = 73;
    private const ushort OtherCode = 42;

    [Fact]
    public void SampleNearest_ReturnsNearestSoundingDepth()
    {
        var document = BuildDocument();

        // Closest to point A (50.0, 4.0) → 12.5 m.
        var near = S101SoundingSampler.SampleNearest(document, 50.00002, 4.00002);

        Assert.NotNull(near);
        Assert.Equal(12.5, near!.Value.DepthMeters, 3);
        Assert.Equal(50.0, near.Value.Position.Latitude, 6);
        Assert.Equal(4.0, near.Value.Position.Longitude, 6);
        Assert.True(near.Value.DistanceMeters > 0);
    }

    [Fact]
    public void SampleNearest_PicksTheOtherPoint_WhenCloser()
    {
        var document = BuildDocument();

        // Closest to point B (50.001, 4.001) → 20.0 m.
        var near = S101SoundingSampler.SampleNearest(document, 50.00105, 4.00105);

        Assert.NotNull(near);
        Assert.Equal(20.0, near!.Value.DepthMeters, 3);
    }

    [Fact]
    public void SampleNearest_IgnoresNonSoundingFeatures()
    {
        // A document whose only multipoint-bearing feature is NOT a Sounding.
        var document = BuildDocument(soundingTypeCode: OtherCode);

        Assert.Null(S101SoundingSampler.SampleNearest(document, 50.0, 4.0));
    }

    [Fact]
    public void SampleNearest_ReturnsNull_WhenNoSoundings()
    {
        var document = BuildEmptyDocument();

        Assert.Null(S101SoundingSampler.SampleNearest(document, 50.0, 4.0));
    }

    private static S101Document BuildDocument(ushort soundingTypeCode = SoundingCode)
    {
        // COMF 1e7 for lat/lon, SOMF 10 for depth.
        const double cmf = 10_000_000.0;
        const double somf = 10.0;

        var multiPoint = new S101MultiPointRecord
        {
            RecordId = 200,
            Points =
            [
                ((int)(50.0 * cmf), (int)(4.0 * cmf), (int)(12.5 * somf)),
                ((int)(50.001 * cmf), (int)(4.001 * cmf), (int)(20.0 * somf)),
            ],
        };

        var feature = new S101FeatureRecord
        {
            RecordId = 1,
            FeatureTypeCode = soundingTypeCode,
            SpatialAssociations = [new S101SpatialAssociation(115, 200, 1)],
        };

        return new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "TEST.000" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = (uint)cmf,
                CoordinateMultiplicationFactorY = (uint)cmf,
                CoordinateMultiplicationFactorZ = (uint)somf,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string>
            {
                [SoundingCode] = "Sounding",
                [OtherCode] = "DepthArea",
            },
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            MultiPoints = new[] { multiPoint }.ToDictionary(m => m.RecordId),
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
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

    private static S101Document BuildEmptyDocument() =>
        new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "TEST.000" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = [],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
}
