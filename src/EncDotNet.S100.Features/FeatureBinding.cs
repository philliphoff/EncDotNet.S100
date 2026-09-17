namespace EncDotNet.S100.Features;

public sealed class FeatureBinding
{
    public required Multiplicity Multiplicity { get; init; }

    public required string AssociationRef { get; init; }

    public required string RoleRef { get; init; }

    /// <summary>The first feature type the binding admits (see <see cref="FeatureTypeRefs"/>).</summary>
    public required string FeatureTypeRef { get; init; }

    /// <summary>
    /// Every feature type the binding admits, in catalogue order. A binding may list
    /// several (S-401, for instance, lets one <c>AdditionalInformation</c>
    /// binding reach five information types); <see cref="FeatureTypeRef"/> is the first.
    /// </summary>
    public IReadOnlyList<string> FeatureTypeRefs { get; init; } = [];

    public string? RoleType { get; init; }
}
