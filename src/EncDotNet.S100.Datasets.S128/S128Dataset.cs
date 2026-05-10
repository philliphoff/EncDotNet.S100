using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// Root data model for an IHO S-128 (Catalogue of Nautical Products) dataset,
/// parsed from S-100 Part 10b GML encoding via <see cref="S128DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-128 (edition 2.0.0) catalogues describe the nautical products produced
/// by an agency (charts, ENCs, S-1xx services) together with their coverage
/// extents, currency, and distribution metadata. See S-128 § 12 for the
/// Feature Catalogue and § 13 for the Portrayal Catalogue.
/// </remarks>
public sealed class S128Dataset
{
    /// <summary>The product specification identifier (e.g. <c>"S-128"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier (gml:id of the dataset root).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    /// <remarks>
    /// In S-128 2.0.0 every member of the catalogue (including metadata
    /// records such as <c>DistributorInformation</c> that the FC declares as
    /// information types) is encoded as a child of <c>&lt;members&gt;</c> and
    /// is rendered through the XSLT portrayal pipeline. The reader therefore
    /// surfaces every <c>&lt;member&gt;</c>/<c>&lt;members&gt;</c> child here
    /// and reserves <see cref="InformationTypes"/> for explicit
    /// <c>&lt;imember&gt;</c>/<c>&lt;imembers&gt;</c> entries, which are
    /// permitted by the encoding for forward compatibility.
    /// </remarks>
    public required ImmutableArray<S128Feature> Features { get; init; }

    /// <summary>
    /// Information type instances carried in <c>&lt;imember&gt;</c> /
    /// <c>&lt;imembers&gt;</c> wrappers. Empty for the official 2.0.0 sample.
    /// </summary>
    public required ImmutableArray<S128InformationType> InformationTypes { get; init; }

    /// <summary>
    /// Catalogue product entries — the subset of <see cref="Features"/> whose
    /// FeatureType is one of the navigational product classes
    /// (<c>ElectronicProduct</c>, <c>PhysicalProduct</c>, <c>S100Service</c>).
    /// Built lazily on first access.
    /// </summary>
    public IReadOnlyList<S128ProductEntry> Entries
    {
        get
        {
            _entries ??= Features
                .Where(S128ProductEntry.IsProductFeature)
                .Select(f => new S128ProductEntry(f))
                .ToArray();
            return _entries;
        }
    }
    private S128ProductEntry[]? _entries;

    /// <summary>Opens an S-128 dataset from a file path.</summary>
    public static S128Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S128DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-128 dataset from a stream.</summary>
    public static S128Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S128DatasetReader.Read(stream);
    }
}

/// <summary>
/// A feature parsed from an S-128 GML dataset.
/// </summary>
/// <remarks>
/// See S-128 § 12 (Feature Catalogue) for the enumerated feature types.
/// The reader is namespace-driven and does not gate on a hard-coded
/// feature-type allow-list, so producer extensions are surfaced as well.
/// </remarks>
public sealed class S128Feature : IGmlFeature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The feature type code, taken from the GML element local name (e.g.
    /// <c>ElectronicProduct</c>, <c>PhysicalProduct</c>, <c>S100Service</c>,
    /// <c>DistributorInformation</c>).
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type associated with the feature.</summary>
    public GmlGeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public ImmutableArray<(double Latitude, double Longitude)> Points { get; init; }

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> Curves { get; init; }

    /// <summary>Surface exterior ring coordinates.</summary>
    public ImmutableArray<(double Latitude, double Longitude)> ExteriorRing { get; init; }

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> InteriorRings { get; init; }

    /// <summary>Simple (leaf-text) attribute values keyed by attribute code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex (nested) attribute groups.</summary>
    public required ImmutableArray<S128ComplexAttribute> ComplexAttributes { get; init; }

    /// <inheritdoc/>
    IEnumerable<IGmlComplexAttribute> IGmlFeature.GmlComplexAttributes => ComplexAttributes.Cast<IGmlComplexAttribute>();

    /// <summary>
    /// Outgoing <c>xlink:href</c> references carried directly on the feature
    /// (e.g. <c>elementContainer</c>, <c>theReference</c>).
    /// </summary>
    public required ImmutableArray<S128XlinkReference> References { get; init; }
}

/// <summary>
/// An information type instance parsed from an S-128 <c>&lt;imember&gt;</c>
/// or <c>&lt;imembers&gt;</c> wrapper.
/// </summary>
public sealed class S128InformationType : IGmlInformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (taken from the element local name).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required ImmutableArray<S128ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex (nested) attribute instance containing sub-attribute values.
/// </summary>
public sealed class S128ComplexAttribute : IGmlComplexAttribute
{
    /// <summary>The complex attribute code (the carrying element local name).</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute leaf values keyed by sub-attribute code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }

    /// <summary>Nested complex attributes (e.g. <c>timeIntervalOfProduct/issuanceCycle</c>).</summary>
    public required ImmutableArray<S128ComplexAttribute> NestedAttributes { get; init; }
}

/// <summary>
/// An <c>xlink:href</c> reference carried on a feature.
/// </summary>
/// <param name="Role">The local name of the carrying element (e.g. <c>elementContainer</c>, <c>theReference</c>).</param>
/// <param name="Href">The raw xlink href value (typically <c>#id</c> for local references).</param>
/// <param name="Arcrole">Optional <c>xlink:arcrole</c> attribute.</param>
/// <param name="TargetId">The trimmed identifier portion of <paramref name="Href"/> (leading <c>#</c> stripped).</param>
public readonly record struct S128XlinkReference(string Role, string Href, string? Arcrole, string TargetId);
