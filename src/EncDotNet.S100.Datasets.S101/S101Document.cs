using System.Collections.ObjectModel;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Record update instruction (<c>RUIN</c>) — and the per-element variants
/// <c>SAUI</c> / <c>FAUI</c> / <c>IUIN</c> / <c>ATIN</c> — that drive sequential
/// update application.
/// </summary>
/// <remarks>
/// <para>
/// Numeric values match the ISO 8211 encoding used by S-101 (S-100 Part 10a):
/// <c>1 = Insert</c>, <c>2 = Delete</c>, <c>3 = Modify</c>. <see cref="None"/>
/// (<c>0</c>) is a non-spec sentinel meaning "no instruction recorded" — used
/// for records built in memory (for example the S-57 → S-101 translator) and as
/// the safe default before a value is read.
/// </para>
/// <para>
/// Mirrors <c>EncDotNet.S57.S57UpdateInstruction</c> so the two product
/// pipelines share a vocabulary; see the design note in the session plan.
/// </para>
/// </remarks>
public enum S101UpdateInstruction : byte
{
    /// <summary>No update instruction recorded (in-memory / base data default).</summary>
    None = 0,

    /// <summary>Insert a new record/attribute/association (<c>RUIN = 1</c>).</summary>
    Insert = 1,

    /// <summary>Delete the referenced record/attribute/association (<c>RUIN = 2</c>).</summary>
    Delete = 2,

    /// <summary>Modify the referenced record/attribute/association (<c>RUIN = 3</c>).</summary>
    Modify = 3,
}

/// <summary>
/// Parsed S-101 dataset document built directly from ISO 8211 records.
/// </summary>
/// <remarks>
/// This type is the in-memory representation consumed by
/// <see cref="S101LuaDataProvider"/> and <see cref="S101VectorSource"/>. It is
/// public so that adapters for other product specifications (for example,
/// S-57) can construct an equivalent graph in memory and reuse the S-101
/// portrayal pipeline.
/// </remarks>
public sealed class S101Document
{
    /// <summary>Dataset identification (DSID record).</summary>
    public required S101DatasetIdentification Identification { get; init; }

    /// <summary>Dataset structure information (DSSI record), including coordinate scale factors.</summary>
    public required S101DatasetStructureInfo StructureInfo { get; init; }

    /// <summary>Maps numeric feature type codes to their feature catalogue acronyms (e.g. <c>DepthArea</c>).</summary>
    public required IReadOnlyDictionary<ushort, string> FeatureTypeCatalogue { get; init; }

    /// <summary>Maps numeric attribute codes to their feature catalogue acronyms.</summary>
    public required IReadOnlyDictionary<ushort, string> AttributeTypeCatalogue { get; init; }

    /// <summary>Point spatial records keyed by record id (RCNM = 110).</summary>
    public required IReadOnlyDictionary<uint, S101PointRecord> Points { get; init; }

    /// <summary>
    /// Multi-point spatial records keyed by record id (RCNM = 115). Used by features
    /// such as <c>Sounding</c> whose portrayal expects a single feature with many
    /// 3D points.
    /// </summary>
    public IReadOnlyDictionary<uint, S101MultiPointRecord> MultiPoints { get; init; }
        = ReadOnlyDictionary<uint, S101MultiPointRecord>.Empty;

    /// <summary>Curve segment records keyed by record id (RCNM = 120).</summary>
    public required IReadOnlyDictionary<uint, S101CurveSegmentRecord> CurveSegments { get; init; }

    /// <summary>Composite curve records keyed by record id (RCNM = 125).</summary>
    public required IReadOnlyDictionary<uint, S101CompositeCurveRecord> CompositeCurves { get; init; }

    /// <summary>Surface records keyed by record id (RCNM = 130).</summary>
    public required IReadOnlyDictionary<uint, S101SurfaceRecord> Surfaces { get; init; }

    /// <summary>Feature records in dataset order.</summary>
    public required IReadOnlyList<S101FeatureRecord> Features { get; init; }

    /// <summary>Information type records keyed by record id.</summary>
    public required IReadOnlyDictionary<uint, S101InformationRecord> InformationTypes { get; init; }

    /// <summary>Maps numeric information type codes to their feature catalogue acronyms.</summary>
    public required IReadOnlyDictionary<ushort, string> InformationTypeCatalogue { get; init; }

    /// <summary>Maps numeric information association codes to their feature catalogue acronyms.</summary>
    public required IReadOnlyDictionary<ushort, string> InformationAssociationCatalogue { get; init; }

    /// <summary>Maps numeric feature association codes to their feature catalogue acronyms.</summary>
    public required IReadOnlyDictionary<ushort, string> FeatureAssociationCatalogue { get; init; }

    /// <summary>Maps numeric role codes to their feature catalogue acronyms.</summary>
    public required IReadOnlyDictionary<ushort, string> RoleCatalogue { get; init; }

    /// <summary>
    /// Applies a single sequential update document onto this document and returns
    /// a new, merged <see cref="S101Document"/> (this instance is not mutated).
    /// </summary>
    /// <param name="update">
    /// An update dataset (application profile <c>2</c>) parsed into the same
    /// <see cref="S101Document"/> shape. Record-level <c>RUIN</c> and the inlined
    /// per-element instructions (<c>SAUI</c> / <c>FAUI</c> / <c>IUIN</c> /
    /// <c>ATIN</c>) drive insert / delete / modify per S-100 Part 10a.
    /// </param>
    /// <returns>A new document reflecting the applied changes.</returns>
    /// <remarks>
    /// Mirrors <c>EncDotNet.S57.S57Document.ApplyChanges</c>: there is no separate
    /// update document type, and merging is a pure document-to-document operation.
    /// This single-step merge does not validate edition/update preconditions; use
    /// <see cref="S101UpdateApplicator.Apply(S101Document, IReadOnlyList{S101Document}, out S101UpdateReport)"/>
    /// for sequenced, best-effort application with a report.
    /// </remarks>
    public S101Document ApplyChanges(S101Document update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return S101UpdateApplicator.ApplySingle(this, update, messages: null);
    }
}

/// <summary>DSID — Dataset Identification record.</summary>
public sealed record S101DatasetIdentification
{
    /// <summary>Record name (RCNM) value identifying this as a DSID record.</summary>
    public byte RecordName { get; init; }

    /// <summary>Record identification number (RCID) within the dataset.</summary>
    public uint RecordId { get; init; }

    /// <summary>Encoding specification identifier (e.g. <c>S-100 Part 10b</c>).</summary>
    public string EncodingSpecification { get; init; } = "";

    /// <summary>Edition of the encoding specification.</summary>
    public string EncodingSpecificationEdition { get; init; } = "";

    /// <summary>Product specification identifier (e.g. <c>INT.IHO.S-101.1.0</c>).</summary>
    public string ProductSpecification { get; init; } = "";

    /// <summary>Edition of the product specification.</summary>
    public string ProductSpecificationEdition { get; init; } = "";

    /// <summary>Application profile identifier.</summary>
    public string ApplicationProfile { get; init; } = "";

    /// <summary>Dataset name (file basename without extension).</summary>
    public string DatasetName { get; init; } = "";

    /// <summary>Human-readable dataset title.</summary>
    public string DatasetTitle { get; init; } = "";

    /// <summary>Dataset reference date in ISO 8601 form.</summary>
    public string DatasetReferenceDate { get; init; } = "";

    /// <summary>Dataset language code (e.g. <c>eng</c>).</summary>
    public string DatasetLanguage { get; init; } = "";

    /// <summary>Dataset abstract (DSID/DSAB), when present.</summary>
    public string DatasetAbstract { get; init; } = "";

    /// <summary>
    /// Producer-supplied dataset edition string (DSID/DSED), when present.
    /// </summary>
    /// <remarks>
    /// Populated inconsistently across producers (some emit e.g. <c>1.0</c>,
    /// others leave it empty), so it is captured for reference but is not the
    /// authoritative edition source for update sequencing — prefer the exchange
    /// catalogue's <c>editionNumber</c>. S-100 Part 10a.
    /// </remarks>
    public string DatasetEdition { get; init; } = "";

    /// <summary>
    /// Update number of this dataset within its edition: <c>0</c> for a base
    /// cell, <c>1..n</c> for sequential updates.
    /// </summary>
    /// <remarks>
    /// Derived from the dataset file extension carried in DSID/DSNM
    /// (<c>….000</c> = base, <c>….001</c> = update 1, …). This is the reliable
    /// per-dataset update number and matches the exchange catalogue's
    /// <c>updateNumber</c>. S-100 Part 10a / S-101.
    /// </remarks>
    public int UpdateNumber { get; init; }

    /// <summary>
    /// <see langword="true"/> when this dataset is a sequential update
    /// (application profile <c>2</c>) rather than a base/new dataset
    /// (application profile <c>1</c>), per DSID/PROF.
    /// </summary>
    /// <remarks>S-100 Part 10a — application profile distinguishes base vs update datasets.</remarks>
    public bool IsUpdate => ApplicationProfile == "2";
}

/// <summary>DSSI — Dataset Structure Information record.</summary>
public sealed class S101DatasetStructureInfo
{
    /// <summary>Coordinate multiplication factor for X (longitude). Use 10<sup>7</sup> when zero.</summary>
    public uint CoordinateMultiplicationFactorX { get; init; }

    /// <summary>Coordinate multiplication factor for Y (latitude). Use 10<sup>7</sup> when zero.</summary>
    public uint CoordinateMultiplicationFactorY { get; init; }

    /// <summary>Coordinate multiplication factor for Z (depth/height). Use 10<sup>7</sup> when zero.</summary>
    public uint CoordinateMultiplicationFactorZ { get; init; }
}

/// <summary>PRID + C2IT — Point record with a single 2D coordinate.</summary>
public sealed class S101PointRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>Y coordinate (latitude × CMFY).</summary>
    public int Y { get; init; }

    /// <summary>X coordinate (longitude × CMFX).</summary>
    public int X { get; init; }

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). <see cref="S101UpdateInstruction.Insert"/> for base cells. S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>
/// MPID + C3IL — Multi-point spatial record carrying many 3D coordinate triples.
/// Used by S-101 features whose portrayal pipeline expects a <c>MultiPoint</c>
/// primitive type, most notably <c>Sounding</c>.
/// </summary>
public sealed class S101MultiPointRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>3D coordinates: Y (× CMFY), X (× CMFX), Z (× CMFZ) — typically depth.</summary>
    public IReadOnlyList<(int Y, int X, int Z)> Points { get; init; }
        = [];

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>CRID + PTAS + C2IL — Curve segment with start/end point refs and intermediate coordinates.</summary>
public sealed class S101CurveSegmentRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>Begin and end point associations (PTAS field). Topology = 1 marks begin, 2 marks end.</summary>
    public IReadOnlyList<S101PointAssociation> PointAssociations { get; init; } = [];

    /// <summary>Intermediate coordinates between begin and end points, in (Y, X) order.</summary>
    public IReadOnlyList<(int Y, int X)> IntermediateCoordinates { get; init; } = [];

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>Point-topology association from the PTAS field.</summary>
/// <param name="RecordName">RCNM of the referenced record (typically 110 = Point).</param>
/// <param name="RecordId">RCID of the referenced record.</param>
/// <param name="Topology">Topology indicator: 1 = begin, 2 = end.</param>
public readonly record struct S101PointAssociation(byte RecordName, uint RecordId, byte Topology);

/// <summary>CCID + CUCO — Composite curve referencing curve segments.</summary>
public sealed class S101CompositeCurveRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>Curve component associations in traversal order.</summary>
    public IReadOnlyList<S101CurveUsage> CurveComponents { get; init; } = [];

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>Curve usage from the CUCO field.</summary>
/// <param name="RecordName">RCNM of the referenced curve record.</param>
/// <param name="RecordId">RCID of the referenced curve record.</param>
/// <param name="Orientation">Orientation: 1 = forward, 2 = reverse.</param>
public readonly record struct S101CurveUsage(byte RecordName, uint RecordId, byte Orientation);

/// <summary>SRID + RIAS — Surface record with ring associations to curves.</summary>
public sealed class S101SurfaceRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>Ring associations: exterior (USAG = 1) or interior (USAG = 2) rings of curves.</summary>
    public IReadOnlyList<S101RingAssociation> RingAssociations { get; init; } = [];

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>Ring association from the RIAS field.</summary>
/// <param name="RecordName">RCNM of the referenced curve or composite curve record.</param>
/// <param name="RecordId">RCID of the referenced record.</param>
/// <param name="Orientation">Orientation: 1 = forward, 2 = reverse.</param>
/// <param name="Usage">Usage: 1 = exterior, 2 = interior.</param>
public readonly record struct S101RingAssociation(byte RecordName, uint RecordId, byte Orientation, byte Usage);

/// <summary>FRID + FOID + ATTR + SPAS + FACS + INAS — Feature record.</summary>
public sealed class S101FeatureRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>Numeric feature type code (resolved against <see cref="S101Document.FeatureTypeCatalogue"/>).</summary>
    public ushort FeatureTypeCode { get; init; }

    /// <summary>Producing agency code (FOID.AGEN).</summary>
    public ushort ProducingAgency { get; init; }

    /// <summary>Feature identification number within the producing agency (FOID.FIDN).</summary>
    public uint FeatureIdentificationNumber { get; init; }

    /// <summary>Feature identification subdivision (FOID.FIDS).</summary>
    public ushort FeatureIdentificationSubdivision { get; init; }

    /// <summary>Flat list of attributes; complex attributes are represented as a marker row followed by their sub-rows.</summary>
    public IReadOnlyList<S101Attribute> Attributes { get; init; } = [];

    /// <summary>Spatial associations linking the feature to point/curve/composite-curve/surface records.</summary>
    public IReadOnlyList<S101SpatialAssociation> SpatialAssociations { get; init; } = [];

    /// <summary>Associations to other feature records.</summary>
    public IReadOnlyList<S101FeatureAssociation> FeatureAssociations { get; init; } = [];

    /// <summary>Associations to information type records.</summary>
    public IReadOnlyList<S101InformationAssociation> InformationAssociations { get; init; } = [];

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). <see cref="S101UpdateInstruction.Insert"/> for base cells. S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>IRID + ATTR — Information type record.</summary>
public sealed class S101InformationRecord
{
    /// <summary>Record identification number.</summary>
    public uint RecordId { get; init; }

    /// <summary>Numeric information type code.</summary>
    public ushort InformationTypeCode { get; init; }

    /// <summary>Flat list of attributes; complex attributes use the same marker convention as feature records.</summary>
    public IReadOnlyList<S101Attribute> Attributes { get; init; } = [];

    /// <summary>Record version (RVER); increments as the record is updated. S-100 Part 10a.</summary>
    public int RecordVersion { get; init; }

    /// <summary>Record update instruction (RUIN). S-100 Part 10a.</summary>
    public S101UpdateInstruction UpdateInstruction { get; init; }
}

/// <summary>Attribute value from the ATTR field.</summary>
/// <param name="NumericCode">Numeric attribute code resolved against <see cref="S101Document.AttributeTypeCatalogue"/>.</param>
/// <param name="Index">Sequence index within a complex attribute (1 marks the start of an instance); maps to ATIX.</param>
/// <param name="Value">Attribute value as a string; numeric and enumerated values are stringified.</param>
/// <param name="ParentIndex">Parent attribute index for nested/complex attributes (PAIX); 0 when top-level.</param>
/// <param name="UpdateInstruction">Per-attribute update instruction (ATIN) used during update application; <see cref="S101UpdateInstruction.None"/> in base cells.</param>
public readonly record struct S101Attribute(
    ushort NumericCode,
    ushort Index,
    string Value,
    ushort ParentIndex = 0,
    S101UpdateInstruction UpdateInstruction = S101UpdateInstruction.None);

/// <summary>Spatial association from the SPAS field.</summary>
/// <param name="RecordName">RCNM of the referenced spatial record (110 / 120 / 125 / 130).</param>
/// <param name="RecordId">RCID of the referenced spatial record.</param>
/// <param name="Orientation">Orientation: 1 = forward, 2 = reverse.</param>
/// <param name="UpdateInstruction">Per-pointer update instruction (SAUI) used during update application; <see cref="S101UpdateInstruction.None"/> in base cells.</param>
public readonly record struct S101SpatialAssociation(
    byte RecordName,
    uint RecordId,
    byte Orientation,
    S101UpdateInstruction UpdateInstruction = S101UpdateInstruction.None);

/// <summary>Feature association from the FACS field: links a feature to another feature.</summary>
/// <param name="NumericCode">Numeric association code resolved against <see cref="S101Document.FeatureAssociationCatalogue"/>.</param>
/// <param name="RecordId">RCID of the associated feature record.</param>
/// <param name="RoleCode">Numeric role code resolved against <see cref="S101Document.RoleCatalogue"/>.</param>
/// <param name="UpdateInstruction">Per-pointer update instruction (FAUI) used during update application; <see cref="S101UpdateInstruction.None"/> in base cells.</param>
public readonly record struct S101FeatureAssociation(
    ushort NumericCode,
    uint RecordId,
    ushort RoleCode,
    S101UpdateInstruction UpdateInstruction = S101UpdateInstruction.None);

/// <summary>Information association from the INAS field: links a feature to an information type record.</summary>
/// <param name="NumericCode">Numeric association code resolved against <see cref="S101Document.InformationAssociationCatalogue"/>.</param>
/// <param name="RecordId">RCID of the associated information type record.</param>
/// <param name="RoleCode">Numeric role code resolved against <see cref="S101Document.RoleCatalogue"/>.</param>
/// <param name="UpdateInstruction">Per-pointer update instruction (IUIN) used during update application; <see cref="S101UpdateInstruction.None"/> in base cells.</param>
public readonly record struct S101InformationAssociation(
    ushort NumericCode,
    uint RecordId,
    ushort RoleCode,
    S101UpdateInstruction UpdateInstruction = S101UpdateInstruction.None);
