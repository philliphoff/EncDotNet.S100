using EncDotNet.S100.Core;

namespace EncDotNet.S100.Features;

/// <summary>
/// An S-100 Part 5 Feature Catalogue: the root of the model produced by
/// <see cref="FeatureCatalogueReader"/>. It carries the catalogue's identity and producer
/// plus every <see cref="FeatureType"/>, <see cref="InformationType"/>, attribute,
/// <see cref="Role"/> and association the product specification defines. Wrap it in a
/// <see cref="FeatureCatalogueDecoder"/> for code-to-name lookups.
/// </summary>
public sealed class FeatureCatalogue
{
    /// <summary>Title of the catalogue (<c>S100FC:name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Subject domain the catalogue covers (<c>S100FC:scope</c>), or <see langword="null"/> when absent.</summary>
    public string? Scope { get; init; }

    /// <summary>Intended use of the catalogue (<c>S100FC:fieldOfApplication</c>), e.g. <c>"Marine Navigation"</c>; <see langword="null"/> when absent.</summary>
    public string? FieldOfApplication { get; init; }

    /// <summary>
    /// The product specification this Feature Catalogue targets, as declared
    /// in <c>&lt;S100FC:productId&gt;</c> (e.g. "S-101"). May be <c>null</c>
    /// when the source XML omits the element (some legacy bundled catalogues
    /// such as S-421 do not declare it).
    /// </summary>
    public string? ProductId { get; init; }

    /// <summary>Catalogue version as written in <c>S100FC:versionNumber</c>, e.g. <c>"2.0.0"</c>.</summary>
    public required string VersionNumber { get; init; }

    /// <summary>Publication date of this version, verbatim from <c>S100FC:versionDate</c> (normally <c>yyyy-MM-dd</c>); not parsed.</summary>
    public required string VersionDate { get; init; }

    /// <summary>
    /// The Feature Catalogue identity tuple <c>(ProductId, VersionNumber)</c>
    /// projected into a strongly-typed <see cref="Core.CatalogueRef"/>, or
    /// <c>null</c> when either field is missing or unparseable. This is the
    /// preferred way to identify a catalogue instance for caching and
    /// compatibility checks (S-100 Edition 5.2.1 Part 2 §6).
    /// </summary>
    public CatalogueRef? CatalogueRef { get; init; }

    /// <summary>Organisation responsible for the catalogue (<c>S100FC:producer</c>), or <see langword="null"/> when absent.</summary>
    public Producer? Producer { get; init; }

    /// <summary>Security classification of the catalogue (<c>S100FC:classification</c>), or <see langword="null"/> when absent.</summary>
    public string? Classification { get; init; }

    /// <summary>Simple attributes (<c>S100_FC_SimpleAttributes</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<SimpleAttribute> SimpleAttributes { get; init; } = [];

    /// <summary>Complex attributes (<c>S100_FC_ComplexAttributes</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<ComplexAttribute> ComplexAttributes { get; init; } = [];

    /// <summary>Association roles (<c>S100_FC_Roles</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<Role> Roles { get; init; } = [];

    /// <summary>Information associations (<c>S100_FC_InformationAssociations</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<InformationAssociation> InformationAssociations { get; init; } = [];

    /// <summary>Feature associations (<c>S100_FC_FeatureAssociations</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<FeatureAssociation> FeatureAssociations { get; init; } = [];

    /// <summary>Information types (<c>S100_FC_InformationTypes</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<InformationType> InformationTypes { get; init; } = [];

    /// <summary>Feature types (<c>S100_FC_FeatureTypes</c>), in catalogue order; empty when the section is absent.</summary>
    public IReadOnlyList<FeatureType> FeatureTypes { get; init; } = [];
}
