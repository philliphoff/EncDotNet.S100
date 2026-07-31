using System.Xml.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Gml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// Root data model for an S-411 Sea Ice dataset, parsed from S-100 Part 10b GML
/// encoding via <see cref="S411DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-411 is the JCOMM/IHO product specification for Ice Information for Surface
/// Navigation. See the S-411 Edition 1.2.1 Feature Catalogue for the full
/// feature/attribute model.
/// </remarks>
public sealed class S411Dataset
{
    /// <summary>The product specification identifier (e.g. <c>"S-411"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// The declared product-specification edition (e.g. <c>"2.0.0"</c>) read
    /// from <c>DatasetIdentificationInformation/productEdition</c>, or
    /// <c>null</c> when the dataset declares none. S-100 Part 10b.
    /// </summary>
    public string? DeclaredEdition { get; init; }

    /// <summary>The dataset identifier (gml:id of the dataset root).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>
    /// The dataset's reference / issue timestamp, if one was carried in the
    /// source GML. For the IHO 1.2.1 sample shape this is parsed from
    /// <c>S100:DatasetIdentificationInformation/S100:datasetReferenceDate</c>
    /// (S-100 Part 17 dataset identification metadata, encoded per
    /// S-100 Part 10b §C.4 and §C.6). For the JCOMM operational shape the
    /// reader probes a small set of well-known issue/observation timestamp
    /// elements (<c>ice:issueDateTime</c>, <c>ice:issueDate</c>,
    /// <c>ice:observationDateTime</c>, <c>ice:observationDate</c>) that
    /// real-world Canadian-Ice-Service feeds have been observed to use.
    /// May be <c>null</c> when no recognised timestamp element is present.
    /// </summary>
    /// <remarks>
    /// S-411 Edition 1.2.1 datasets represent a single issue snapshot of
    /// the ice picture, so this single value is sufficient — there are no
    /// per-feature time-step samples like S-104 / S-111. Callers that
    /// participate in the viewer's global time slider treat this as the
    /// "snapshot at-or-before T" boundary.
    /// </remarks>
    public DateTime? IssueDate { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required IReadOnlyList<S411Feature> Features { get; init; }

    /// <summary>
    /// The original GML document the dataset was parsed from. Preserved so
    /// the XSLT portrayal pipeline can run the official S-411 stylesheets
    /// directly against the source XML — the stylesheets target the
    /// <c>ice:IceDataSet</c> / <c>ice:IceFeatureMember</c> / <c>ice:&lt;class&gt;</c>
    /// shape used by JCOMM operational producers, not a synthesised
    /// projection.
    /// </summary>
    public required XDocument SourceDocument { get; init; }

    /// <summary>Opens an S-411 dataset from a file path.</summary>
    public static S411Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S411DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-411 dataset from a stream.</summary>
    public static S411Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S411DatasetReader.Read(stream);
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-411
    /// dataset at <paramref name="path"/> — its declared specification and
    /// geographic extent — for phased / deferred loading (issue #460).
    /// </summary>
    public static DatasetMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(path).ReadMetadata();
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-411
    /// dataset from <paramref name="stream"/> (issue #460).
    /// </summary>
    public static DatasetMetadata ReadMetadata(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(stream).ReadMetadata();
    }

    /// <summary>
    /// Produces the lightweight <see cref="DatasetMetadata"/> for this parsed
    /// dataset: declared specification and raw geographic extent (issue #460).
    /// </summary>
    public DatasetMetadata ReadMetadata() =>
        GmlDatasetMetadata.Create("S-411", DeclaredEdition, Features);
}

/// <summary>
/// A geographic feature parsed from an S-411 GML dataset.
/// </summary>
public sealed class S411Feature : IS100Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The feature type code (e.g. <c>"SeaIce"</c>, <c>"LakeIce"</c>,
    /// <c>"Iceberg"</c>, <c>"IceEdge"</c>).
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public S100GeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = [];

    /// <summary>Surface exterior ring coordinates.</summary>
    public IReadOnlyList<GeoPosition> ExteriorRing { get; init; } = [];

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups, each containing sub-attribute values.</summary>
    public required IReadOnlyList<S411ComplexAttribute> ComplexAttributes { get; init; }

    /// <inheritdoc/>
    IReadOnlyList<IS100ComplexAttribute> IS100Feature.ComplexAttributes =>
        ComplexAttributes.Cast<IS100ComplexAttribute>().ToArray();
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S411ComplexAttribute : IS100ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> SubAttributes { get; init; }
}
