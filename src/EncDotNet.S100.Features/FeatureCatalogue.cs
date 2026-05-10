using EncDotNet.S100.Core;

namespace EncDotNet.S100.Features;

public sealed class FeatureCatalogue
{
    public required string Name { get; init; }

    public string? Scope { get; init; }

    public string? FieldOfApplication { get; init; }

    /// <summary>
    /// The product specification this Feature Catalogue targets, as declared
    /// in <c>&lt;S100FC:productId&gt;</c> (e.g. "S-101"). May be <c>null</c>
    /// when the source XML omits the element (some legacy bundled catalogues
    /// such as S-421 do not declare it).
    /// </summary>
    public string? ProductId { get; init; }

    public required string VersionNumber { get; init; }

    public required string VersionDate { get; init; }

    /// <summary>
    /// The Feature Catalogue identity tuple <c>(ProductId, VersionNumber)</c>
    /// projected into a strongly-typed <see cref="Core.CatalogueRef"/>, or
    /// <c>null</c> when either field is missing or unparseable. This is the
    /// preferred way to identify a catalogue instance for caching and
    /// compatibility checks (S-100 Edition 5.2.1 Part 2 §6).
    /// </summary>
    public CatalogueRef? CatalogueRef { get; init; }

    public Producer? Producer { get; init; }

    public string? Classification { get; init; }

    public IReadOnlyList<SimpleAttribute> SimpleAttributes { get; init; } = [];

    public IReadOnlyList<ComplexAttribute> ComplexAttributes { get; init; } = [];

    public IReadOnlyList<Role> Roles { get; init; } = [];

    public IReadOnlyList<InformationAssociation> InformationAssociations { get; init; } = [];

    public IReadOnlyList<FeatureAssociation> FeatureAssociations { get; init; } = [];

    public IReadOnlyList<InformationType> InformationTypes { get; init; } = [];

    public IReadOnlyList<FeatureType> FeatureTypes { get; init; } = [];
}
