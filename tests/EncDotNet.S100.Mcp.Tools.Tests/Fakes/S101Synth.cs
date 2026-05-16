using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S101Synth
{
    /// <summary>Builds a minimal in-memory S-101 dataset for tests.</summary>
    public static S101Dataset Dataset(string name = "test-enc")
    {
        return Dataset(name, features: ImmutableArray<S101FeatureRecord>.Empty);
    }

    /// <summary>
    /// Builds an S-101 dataset with the supplied feature records and
    /// optional code/acronym dictionaries.
    /// </summary>
    public static S101Dataset Dataset(
        string name,
        ImmutableArray<S101FeatureRecord> features,
        ImmutableDictionary<ushort, string>? featureTypes = null,
        ImmutableDictionary<ushort, string>? attributeTypes = null)
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
            FeatureTypeCatalogue = featureTypes ?? ImmutableDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = attributeTypes ?? ImmutableDictionary<ushort, string>.Empty,
            Points = ImmutableDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ImmutableDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ImmutableDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ImmutableDictionary<uint, S101SurfaceRecord>.Empty,
            Features = features,
            InformationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };
        return S101Dataset.FromDocument(document);
    }

    /// <summary>
    /// Builds a feature record with the given RCID, feature type code,
    /// and optional flat attribute list and spatial associations.
    /// </summary>
    public static S101FeatureRecord Feature(
        uint rcid,
        ushort featureTypeCode,
        IEnumerable<(ushort Code, string Value)>? attributes = null,
        byte spatialRcnm = 110)
    {
        var attrs = attributes is null
            ? ImmutableArray<S101Attribute>.Empty
            : attributes.Select(a => new S101Attribute(a.Code, 1, a.Value)).ToImmutableArray();
        var spatial = ImmutableArray.Create(new S101SpatialAssociation(spatialRcnm, rcid, 1));
        return new S101FeatureRecord
        {
            RecordId = rcid,
            FeatureTypeCode = featureTypeCode,
            ProducingAgency = 540,
            FeatureIdentificationNumber = rcid,
            FeatureIdentificationSubdivision = 0,
            Attributes = attrs,
            SpatialAssociations = spatial,
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
        };
    }
}
