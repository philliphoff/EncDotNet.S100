using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Gml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S412;

/// <summary>
/// Root data model for an S-412 Marine Weather Warnings dataset, parsed from
/// S-100 Part 10b GML encoding via <see cref="S412DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-412 is a draft WMO product specification: the reader follows the
/// Feature Catalogue 1.1.0 draft (v009) and the upstream sample encoding.
/// Enumeration values are written as <c>&lt;windWarningThreshold
/// code="3"&gt;storm force&lt;/windWarningThreshold&gt;</c>; the reader keeps
/// the code, which the Feature Catalogue and the portrayal rules use, as the
/// attribute's value.
/// </remarks>
public sealed class S412Dataset
{
    /// <summary>The product specification identifier (e.g. "S-412").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// The declared product-specification edition (e.g. <c>"1.0.0"</c>) read
    /// from <c>DatasetIdentificationInformation/productEdition</c>, or
    /// <c>null</c> when the dataset declares none. S-100 Part 10b.
    /// </summary>
    public string? DeclaredEdition { get; init; }

    /// <summary>The dataset identifier (the root element's <c>gml:id</c>).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>
    /// The outcome of the dataset-level axis-order check (issue #650), or
    /// <c>null</c> when the dataset has no positions. When it is
    /// <see cref="AxisOrderOutcome.Swapped"/> the reader has already read
    /// every position longitude first.
    /// </summary>
    public AxisOrderAssessment? AxisOrder { get; init; }

    /// <summary>
    /// The weather warning features in the dataset (<c>WindWarning</c>,
    /// <c>SignificantWaveHeightWarning</c>, <c>RestrictedVisibilityWarning</c>,
    /// <c>ThunderstormWarning</c> and <c>IceAccretionWarning</c>).
    /// </summary>
    public required IReadOnlyList<S412Feature> Features { get; init; }

    /// <summary>The information types in the dataset (<c>DataProvider</c>).</summary>
    public required IReadOnlyList<S412InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-412 dataset from a file path.</summary>
    /// <param name="path">The path to the GML file.</param>
    /// <returns>The parsed dataset.</returns>
    public static S412Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S412DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-412 dataset from a stream.</summary>
    /// <param name="stream">The stream holding the GML document.</param>
    /// <returns>The parsed dataset.</returns>
    public static S412Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S412DatasetReader.Read(stream);
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-412
    /// dataset at <paramref name="path"/>: its declared specification and
    /// geographic extent.
    /// </summary>
    /// <param name="path">The path to the GML file.</param>
    /// <returns>The dataset's metadata.</returns>
    public static DatasetMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(path).ReadMetadata();
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-412
    /// dataset from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream holding the GML document.</param>
    /// <returns>The dataset's metadata.</returns>
    public static DatasetMetadata ReadMetadata(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(stream).ReadMetadata();
    }

    /// <summary>
    /// Produces the lightweight <see cref="DatasetMetadata"/> for this parsed
    /// dataset: declared specification and raw geographic extent.
    /// </summary>
    /// <returns>The dataset's metadata.</returns>
    public DatasetMetadata ReadMetadata() =>
        GmlDatasetMetadata.Create("S-412", DeclaredEdition, Features);
}

/// <summary>
/// A weather warning feature parsed from an S-412 GML dataset.
/// </summary>
public sealed class S412Feature : IS100Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (e.g. "WindWarning", "IceAccretionWarning").</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type. S-412 warnings are surfaces.</summary>
    public S100GeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = [];

    /// <summary>Surface exterior ring coordinates.</summary>
    public IReadOnlyList<GeoPosition> ExteriorRing { get; init; } = [];

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>
    /// Simple attributes keyed by code. An enumeration's value is its code
    /// (for example <c>windWarningThreshold</c> = <c>"3"</c>).
    /// </summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attributes, such as <c>fixedDateRange</c> and <c>weatherWarningMessage</c>.</summary>
    public required IReadOnlyList<S412ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>
    /// References to information types resolved via <c>xlink:href</c>. The
    /// role is the local name of the element that carried the reference
    /// (<c>dataProviderInformation</c> in FC 1.1.0).
    /// </summary>
    public IReadOnlyList<GmlReference> References { get; init; } = [];

    /// <inheritdoc/>
    IReadOnlyList<IS100ComplexAttribute> IS100Feature.ComplexAttributes => ComplexAttributes;
}

/// <summary>
/// An information type instance parsed from an S-412 GML dataset.
/// </summary>
public sealed class S412InformationType : IS100InformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (<c>DataProvider</c> in FC 1.1.0).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>
    /// Complex attributes, such as <c>issuingService</c>,
    /// <c>contactInformation</c> and <c>preparationService</c>.
    /// </summary>
    public required IReadOnlyList<S412ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>References to other information types resolved via <c>xlink:href</c>.</summary>
    public IReadOnlyList<GmlReference> References { get; init; } = [];
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
/// <remarks>
/// S-412 nests complex attributes: <c>weatherWarningMessage</c> holds one
/// <c>information</c> per language, and <c>issuingService</c> holds an
/// <c>issuingServiceName</c> that holds <c>information</c>. Each nested one is
/// kept whole, in document order, in <see cref="Groups"/>.
/// <see cref="SubAttributes"/> holds the leaf values, with a nested leaf keyed
/// by its dotted path (for example <c>information.text</c>), the first of a
/// repeated one winning.
/// </remarks>
public sealed class S412ComplexAttribute : IS100ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Sub-attribute values keyed by code, with nested leaves keyed by their
    /// dotted path. An enumeration's value is its code.
    /// </summary>
    public required IReadOnlyDictionary<string, string> SubAttributes { get; init; }

    /// <summary>
    /// The nested complex sub-attributes, one per occurrence in document
    /// order. Empty when the attribute nests none.
    /// </summary>
    public IReadOnlyList<S412ComplexAttribute> Groups { get; init; } = [];
}
