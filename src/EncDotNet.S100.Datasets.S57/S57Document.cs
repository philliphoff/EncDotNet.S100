using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Parsed S-57 (Edition 3.1) ENC base cell, built directly from ISO 8211
/// records. Only the subset of fields required to translate into the S-101
/// in-memory document model is retained.
/// </summary>
/// <remarks>
/// See IHO S-57 Appendix A "ENC Product Specification" and Appendix B.1
/// "ENC Cell Encoding Format" for the field-level definitions.
/// </remarks>
public sealed class S57Document
{
    /// <summary>Dataset identification (DSID record).</summary>
    public required S57DatasetIdentification Identification { get; init; }

    /// <summary>Dataset parameters (DSPM record).</summary>
    public required S57DatasetParameters Parameters { get; init; }

    /// <summary>Vector records keyed by their composite (RCNM, RCID) name.</summary>
    public required ImmutableDictionary<S57Name, S57VectorRecord> VectorRecords { get; init; }

    /// <summary>Feature records in dataset order.</summary>
    public required ImmutableArray<S57FeatureRecord> Features { get; init; }
}

/// <summary>
/// Composite S-57 record name: (RCNM, RCID). Used as a stable key for
/// cross-referencing vector records from feature spatial pointers.
/// </summary>
public readonly record struct S57Name(byte RecordName, uint RecordId);

/// <summary>DSID — Data Set Identification record fields relevant to translation.</summary>
public sealed class S57DatasetIdentification
{
    /// <summary>Exchange purpose: 1 = new, 2 = revision.</summary>
    public byte ExchangePurpose { get; init; }

    /// <summary>Intended usage (navigational purpose) code, 1–6.</summary>
    public byte IntendedUsage { get; init; }

    /// <summary>Dataset name (file basename without extension).</summary>
    public string DatasetName { get; init; } = "";

    /// <summary>Dataset edition number.</summary>
    public string Edition { get; init; } = "";

    /// <summary>Update number; <c>"0"</c> for a base cell.</summary>
    public string UpdateNumber { get; init; } = "";

    /// <summary>Issue date in <c>yyyymmdd</c> form.</summary>
    public string IssueDate { get; init; } = "";

    /// <summary>Edition number of the S-57 standard (e.g. <c>03.1</c>).</summary>
    public string StandardEdition { get; init; } = "";

    /// <summary>Product specification (e.g. <c>ENC</c>).</summary>
    public string ProductSpecification { get; init; } = "";

    /// <summary>Producing agency code.</summary>
    public ushort ProducingAgency { get; init; }
}

/// <summary>DSPM — Data Set Parameters record fields relevant to translation.</summary>
public sealed class S57DatasetParameters
{
    /// <summary>Compilation scale denominator (CSCL).</summary>
    public uint CompilationScale { get; init; }

    /// <summary>Coordinate multiplication factor for X/Y (COMF). Use 10<sup>7</sup> when zero.</summary>
    public uint CoordinateMultiplicationFactor { get; init; }

    /// <summary>Sounding multiplication factor (SOMF). Use 10 when zero.</summary>
    public uint SoundingMultiplicationFactor { get; init; }

    /// <summary>Horizontal datum code (HDAT).</summary>
    public byte HorizontalDatum { get; init; }

    /// <summary>Vertical datum code (VDAT).</summary>
    public byte VerticalDatum { get; init; }

    /// <summary>Sounding datum code (SDAT).</summary>
    public byte SoundingDatum { get; init; }
}

/// <summary>
/// Vector record (VRID + ATTV + VRPT + SG2D/SG3D) — an isolated node, connected
/// node, edge, or face in the S-57 vector model.
/// </summary>
public sealed class S57VectorRecord
{
    /// <summary>Record name (RCNM): 110 = isolated node, 120 = connected node, 130 = edge, 140 = face.</summary>
    public byte RecordName { get; init; }

    /// <summary>Record identification number (RCID).</summary>
    public uint RecordId { get; init; }

    /// <summary>Vector record pointers (VRPT field) — links to begin/end nodes for edges.</summary>
    public ImmutableArray<S57VectorPointer> Pointers { get; init; }

    /// <summary>2D coordinates (SG2D field) — one entry for nodes, multiple for edge intermediates.</summary>
    public ImmutableArray<(int Y, int X)> Coordinates2D { get; init; }

    /// <summary>3D coordinates (SG3D field) — used by SOUNDG features only.</summary>
    public ImmutableArray<(int Y, int X, int Z)> Coordinates3D { get; init; }

    /// <summary>Attributes attached to the vector record (ATTV field) — rare in ENCs.</summary>
    public ImmutableArray<S57Attribute> Attributes { get; init; }
}

/// <summary>VRPT — Vector Record Pointer.</summary>
/// <param name="RecordName">RCNM of the referenced record.</param>
/// <param name="RecordId">RCID of the referenced record.</param>
/// <param name="Orientation">ORNT: 1 = forward, 2 = reverse, 255 = null.</param>
/// <param name="Usage">USAG: 1 = exterior, 2 = interior, 3 = exterior truncated, 255 = null.</param>
/// <param name="Topology">TOPI: 1 = begin, 2 = end, 3 = left face, 4 = right face, 5 = containing face, 255 = null.</param>
/// <param name="Mask">MASK: 1 = mask, 2 = show, 255 = null.</param>
public readonly record struct S57VectorPointer(
    byte RecordName,
    uint RecordId,
    byte Orientation,
    byte Usage,
    byte Topology,
    byte Mask);

/// <summary>
/// Feature record (FRID + FOID + ATTF + FSPT). NATF (national language
/// attributes) and FFPT (feature-to-feature pointers) are not retained in v1.
/// </summary>
public sealed class S57FeatureRecord
{
    /// <summary>Record identification number (RCID).</summary>
    public uint RecordId { get; init; }

    /// <summary>Geometric primitive (PRIM): 1 = Point, 2 = Line, 3 = Area, 255 = none.</summary>
    public byte Primitive { get; init; }

    /// <summary>Group code (GRUP).</summary>
    public byte Group { get; init; }

    /// <summary>Object class code (OBJL) — resolved against the S-57 object catalogue.</summary>
    public ushort ObjectClass { get; init; }

    /// <summary>Producing agency code (FOID.AGEN).</summary>
    public ushort ProducingAgency { get; init; }

    /// <summary>Feature identification number (FOID.FIDN).</summary>
    public uint FeatureIdentificationNumber { get; init; }

    /// <summary>Feature identification subdivision (FOID.FIDS).</summary>
    public ushort FeatureIdentificationSubdivision { get; init; }

    /// <summary>Flat list of feature attributes (ATTF field).</summary>
    public ImmutableArray<S57Attribute> Attributes { get; init; }

    /// <summary>Pointers to spatial records (FSPT field).</summary>
    public ImmutableArray<S57FeatureSpatialPointer> SpatialPointers { get; init; }
}

/// <summary>S-57 attribute: numeric code and string value.</summary>
/// <param name="Code">Numeric attribute code (ATTL).</param>
/// <param name="Value">Attribute value as a string (ATVL).</param>
public readonly record struct S57Attribute(ushort Code, string Value);

/// <summary>FSPT — Feature Record to Spatial Record Pointer.</summary>
/// <param name="RecordName">RCNM of the referenced spatial record.</param>
/// <param name="RecordId">RCID of the referenced spatial record.</param>
/// <param name="Orientation">ORNT: 1 = forward, 2 = reverse, 255 = null.</param>
/// <param name="Usage">USAG: 1 = exterior, 2 = interior, 3 = exterior truncated, 255 = null.</param>
/// <param name="Mask">MASK: 1 = mask, 2 = show, 255 = null.</param>
public readonly record struct S57FeatureSpatialPointer(
    byte RecordName,
    uint RecordId,
    byte Orientation,
    byte Usage,
    byte Mask);
