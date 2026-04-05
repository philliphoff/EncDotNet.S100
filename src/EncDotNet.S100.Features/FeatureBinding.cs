namespace EncDotNet.S100.Features;

public sealed class FeatureBinding
{
    public required Multiplicity Multiplicity { get; init; }

    public required string AssociationRef { get; init; }

    public required string RoleRef { get; init; }

    public required string FeatureTypeRef { get; init; }

    public string? RoleType { get; init; }
}
