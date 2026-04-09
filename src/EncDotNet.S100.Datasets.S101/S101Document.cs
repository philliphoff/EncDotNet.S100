                                                                                                                                                                                                                                                                                                                                           using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Parsed S-101 dataset document built directly from ISO 8211 records.
/// </summary>
internal sealed class S101Document
{
    public required S101DatasetIdentification Identification { get; init; }
    public required S101DatasetStructureInfo StructureInfo { get; init; }
    public required ImmutableDictionary<ushort, string> FeatureTypeCatalogue { get; init; }
    public required ImmutableDictionary<ushort, string> AttributeTypeCatalogue { get; init; }
    public required ImmutableDictionary<uint, S101PointRecord> Points { get; init; }
    public required ImmutableDictionary<uint, S101CurveSegmentRecord> CurveSegments { get; init; }
    public required ImmutableDictionary<uint, S101CompositeCurveRecord> CompositeCurves { get; init; }
    public required ImmutableDictionary<uint, S101SurfaceRecord> Surfaces { get; init; }
    public required ImmutableArray<S101FeatureRecord> Features { get; init; }
    public required ImmutableDictionary<uint, S101InformationRecord> InformationTypes { get; init; }
    public required ImmutableDictionary<ushort, string> InformationTypeCatalogue { get; init; }
    public required ImmutableDictionary<ushort, string> InformationAssociationCatalogue { get; init; }
    public required ImmutableDictionary<ushort, string> FeatureAssociationCatalogue { get; init; }
    public required ImmutableDictionary<ushort, string> RoleCatalogue { get; init; }
}

/// <summary>DSID — Dataset Identification.</summary>
internal sealed class S101DatasetIdentification
{
    public byte RecordName { get; init; }
    public uint RecordId { get; init; }
    public string EncodingSpecification { get; init; } = "";
    public string EncodingSpecificationEdition { get; init; } = "";
    public string ProductSpecification { get; init; } = "";
    public string ProductSpecificationEdition { get; init; } = "";
    public string ApplicationProfile { get; init; } = "";
    public string DatasetName { get; init; } = "";
    public string DatasetTitle { get; init; } = "";
    public string DatasetReferenceDate { get; init; } = "";
    public string DatasetLanguage { get; init; } = "";
}

/// <summary>DSSI — Dataset Structure Information.</summary>
internal sealed class S101DatasetStructureInfo
{
    public uint CoordinateMultiplicationFactorX { get; init; }
    public uint CoordinateMultiplicationFactorY { get; init; }
    public uint CoordinateMultiplicationFactorZ { get; init; }
}

/// <summary>PRID + C2IT — Point record with a single 2D coordinate.</summary>
internal sealed class S101PointRecord
{
    public uint RecordId { get; init; }
    public int Y { get; init; }
    public int X { get; init; }
}

/// <summary>CRID + PTAS + C2IL — Curve segment with start/end point refs and intermediate coordinates.</summary>
internal sealed class S101CurveSegmentRecord
{
    public uint RecordId { get; init; }
    public ImmutableArray<S101PointAssociation> PointAssociations { get; init; }
    public ImmutableArray<(int Y, int X)> IntermediateCoordinates { get; init; }
}

/// <summary>Point-topology association from PTAS field.</summary>
internal readonly record struct S101PointAssociation(byte RecordName, uint RecordId, byte Topology);

/// <summary>CCID + CUCO — Composite curve referencing curve segments.</summary>
internal sealed class S101CompositeCurveRecord
{
    public uint RecordId { get; init; }
    public ImmutableArray<S101CurveUsage> CurveComponents { get; init; }
}

/// <summary>Curve usage from CUCO field.</summary>
internal readonly record struct S101CurveUsage(byte RecordName, uint RecordId, byte Orientation);

/// <summary>SRID + RIAS — Surface record with ring associations to curves.</summary>
internal sealed class S101SurfaceRecord
{
    public uint RecordId { get; init; }
    public ImmutableArray<S101RingAssociation> RingAssociations { get; init; }
}

/// <summary>Ring association from RIAS field: exterior (USAG=1) or interior (USAG=2).</summary>
internal readonly record struct S101RingAssociation(byte RecordName, uint RecordId, byte Orientation, byte Usage);

/// <summary>FRID + FOID + ATTR + SPAS + FACS + INAS — Feature record.</summary>
internal sealed class S101FeatureRecord
{
    public uint RecordId { get; init; }
    public ushort FeatureTypeCode { get; init; }
    public ushort ProducingAgency { get; init; }
    public uint FeatureIdentificationNumber { get; init; }
    public ushort FeatureIdentificationSubdivision { get; init; }
    public ImmutableArray<S101Attribute> Attributes { get; init; }
    public ImmutableArray<S101SpatialAssociation> SpatialAssociations { get; init; }
    public ImmutableArray<S101FeatureAssociation> FeatureAssociations { get; init; }
    public ImmutableArray<S101InformationAssociation> InformationAssociations { get; init; }
}

/// <summary>IRID + ATTR — Information type record.</summary>
internal sealed class S101InformationRecord
{
    public uint RecordId { get; init; }
    public ushort InformationTypeCode { get; init; }
    public ImmutableArray<S101Attribute> Attributes { get; init; }
}

/// <summary>Attribute from ATTR field.</summary>
internal readonly record struct S101Attribute(ushort NumericCode, ushort Index, string Value);

/// <summary>Spatial association from SPAS field.</summary>
internal readonly record struct S101SpatialAssociation(byte RecordName, uint RecordId, byte Orientation);

/// <summary>Feature association from FACS field: links a feature to another feature.</summary>
internal readonly record struct S101FeatureAssociation(ushort NumericCode, uint RecordId, ushort RoleCode);

/// <summary>Information association from INAS field: links a feature to an information type.</summary>
internal readonly record struct S101InformationAssociation(ushort NumericCode, uint RecordId, ushort RoleCode);
