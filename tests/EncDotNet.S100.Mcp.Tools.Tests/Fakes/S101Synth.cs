using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S101Synth
{
    /// <summary>Builds a minimal in-memory S-101 dataset for tests.</summary>
    public static S101Dataset Dataset(string name = "test-enc")
    {
        var document = new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                DatasetName = name,
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            Points = ImmutableDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ImmutableDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ImmutableDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ImmutableDictionary<uint, S101SurfaceRecord>.Empty,
            Features = ImmutableArray<S101FeatureRecord>.Empty,
            InformationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };
        return S101Dataset.FromDocument(document);
    }
}
