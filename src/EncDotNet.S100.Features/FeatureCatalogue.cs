namespace EncDotNet.S100.Features;

public sealed class FeatureCatalogue
{
    public required string Name { get; init; }

    public string? Scope { get; init; }

    public string? FieldOfApplication { get; init; }

    public required string VersionNumber { get; init; }

    public required string VersionDate { get; init; }

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
