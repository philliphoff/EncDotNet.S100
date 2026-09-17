namespace EncDotNet.S100.Features;

public sealed class InformationBinding
{
    public required Multiplicity Multiplicity { get; init; }

    public required string AssociationRef { get; init; }

    public required string RoleRef { get; init; }

    /// <summary>The first information type the binding admits (see <see cref="InformationTypeRefs"/>).</summary>
    public required string InformationTypeRef { get; init; }

    /// <summary>
    /// Every information type the binding admits, in catalogue order. A binding may list
    /// several (S-401, for instance, lets one <c>AdditionalInformation</c>
    /// binding reach five information types); <see cref="InformationTypeRef"/> is the first.
    /// </summary>
    public IReadOnlyList<string> InformationTypeRefs { get; init; } = [];

    public string? RoleType { get; init; }
}
