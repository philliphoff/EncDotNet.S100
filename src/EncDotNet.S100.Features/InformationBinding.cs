namespace EncDotNet.S100.Features;

/// <summary>
/// Declares that a <see cref="FeatureType"/> or <see cref="InformationType"/> may be linked,
/// through an <see cref="InformationAssociation"/> and one of its <see cref="Role"/>s, to one
/// or more information types. Parsed from an
/// <c>S100FC:informationBinding</c> element.
/// </summary>
public sealed class InformationBinding
{
    /// <summary>How many associated information type instances the owning type may reference (<c>multiplicity</c>).</summary>
    public required Multiplicity Multiplicity { get; init; }

    /// <summary>
    /// Code of the <see cref="InformationAssociation"/>, from the <c>ref</c> attribute of the
    /// <c>association</c> child element.
    /// </summary>
    public required string AssociationRef { get; init; }

    /// <summary>
    /// Code of the <see cref="Role"/> the target plays in the association, from the <c>ref</c>
    /// attribute of the <c>role</c> child element.
    /// </summary>
    public required string RoleRef { get; init; }

    /// <summary>The first information type the binding admits (see <see cref="InformationTypeRefs"/>).</summary>
    public required string InformationTypeRef { get; init; }

    /// <summary>
    /// Every information type the binding admits, in catalogue order. A binding may list
    /// several (S-401, for instance, lets one <c>AdditionalInformation</c>
    /// binding reach five information types); <see cref="InformationTypeRef"/> is the first.
    /// </summary>
    public IReadOnlyList<string> InformationTypeRefs { get; init; } = [];

    /// <summary>
    /// Kind of relationship, from the binding's <c>roleType</c> XML attribute (e.g.
    /// <c>"association"</c>, <c>"aggregation"</c> or <c>"composition"</c>), or
    /// <see langword="null"/> when the attribute is absent.
    /// </summary>
    public string? RoleType { get; init; }
}
