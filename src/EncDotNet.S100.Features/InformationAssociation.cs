namespace EncDotNet.S100.Features;

/// <summary>
/// An information association: a named relationship between a feature or information type
/// and an information type, parsed from
/// <c>S100_FC_InformationAssociations/S100_FC_InformationAssociation</c>. Types take part in it through a
/// <see cref="InformationBinding"/>, which names the association, one of its <see cref="Role"/>s and the
/// target type(s). See also <see cref="FeatureAssociation"/>.
/// </summary>
public sealed class InformationAssociation
{
    /// <summary>Human-readable name of the association (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the association (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>Code that identifies the association in the catalogue (<c>code</c>); the target of <see cref="InformationBinding.AssociationRef"/>.</summary>
    public required string Code { get; init; }

    /// <summary><see langword="true"/> when the element's <c>isAbstract</c> XML attribute is <c>"true"</c>.</summary>
    public bool IsAbstract { get; init; }

    /// <summary>
    /// Codes of the association's <see cref="Role"/>s, from the <c>ref</c> attribute of each
    /// <c>role</c> child element, in catalogue order.
    /// </summary>
    public IReadOnlyList<string> RoleRefs { get; init; } = [];
}
