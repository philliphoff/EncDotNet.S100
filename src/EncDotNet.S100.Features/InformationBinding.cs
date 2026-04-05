namespace EncDotNet.S100.Features;

public sealed class InformationBinding
{
    public required Multiplicity Multiplicity { get; init; }

    public required string AssociationRef { get; init; }

    public required string RoleRef { get; init; }

    public required string InformationTypeRef { get; init; }

    public string? RoleType { get; init; }
}
