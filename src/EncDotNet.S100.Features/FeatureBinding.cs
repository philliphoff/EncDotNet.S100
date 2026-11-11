namespace EncDotNet.S100.Features;

/// <summary>
/// Declares that a <see cref="FeatureType"/> may be linked, through a <see cref="FeatureAssociation"/> and one of
/// its <see cref="Role"/>s, to one or more feature types. Parsed from an
/// <c>S100FC:featureBinding</c> element.
/// </summary>
public sealed class FeatureBinding
{
    /// <summary>How many associated feature type instances the owning type may reference (<c>multiplicity</c>).</summary>
    public required Multiplicity Multiplicity { get; init; }

    /// <summary>
    /// Code of the <see cref="FeatureAssociation"/>, from the <c>ref</c> attribute of the
    /// <c>association</c> child element.
    /// </summary>
    public required string AssociationRef { get; init; }

    /// <summary>
    /// Code of the <see cref="Role"/> the target plays in the association, from the <c>ref</c>
    /// attribute of the <c>role</c> child element.
    /// </summary>
    public required string RoleRef { get; init; }

    /// <summary>The first feature type the binding admits (see <see cref="FeatureTypeRefs"/>).</summary>
    public required string FeatureTypeRef { get; init; }

    /// <summary>
    /// Every feature type the binding admits, in catalogue order. A binding may list
    /// several (S-401, for instance, lets one <c>AdditionalInformation</c>
    /// binding reach five information types); <see cref="FeatureTypeRef"/> is the first.
    /// </summary>
    public IReadOnlyList<string> FeatureTypeRefs { get; init; } = [];

    /// <summary>
    /// Kind of relationship, from the binding's <c>roleType</c> XML attribute (e.g.
    /// <c>"association"</c>, <c>"aggregation"</c> or <c>"composition"</c>), or
    /// <see langword="null"/> when the attribute is absent.
    /// </summary>
    public string? RoleType { get; init; }
}
