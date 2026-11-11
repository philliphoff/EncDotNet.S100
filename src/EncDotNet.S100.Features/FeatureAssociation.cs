namespace EncDotNet.S100.Features;

/// <summary>
/// A feature association: a named relationship between two feature types, parsed from
/// <c>S100_FC_FeatureAssociations/S100_FC_FeatureAssociation</c>. Types take part in it through a
/// <see cref="FeatureBinding"/>, which names the association, one of its <see cref="Role"/>s and the
/// target type(s). See also <see cref="InformationAssociation"/>.
/// </summary>
public sealed class FeatureAssociation
{
    /// <summary>Human-readable name of the association (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the association (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>Code that identifies the association in the catalogue (<c>code</c>); the target of <see cref="FeatureBinding.AssociationRef"/>.</summary>
    public required string Code { get; init; }

    /// <summary><see langword="true"/> when the element's <c>isAbstract</c> XML attribute is <c>"true"</c>.</summary>
    public bool IsAbstract { get; init; }

    /// <summary>
    /// Codes of the association's <see cref="Role"/>s, from the <c>ref</c> attribute of each
    /// <c>role</c> child element, in catalogue order.
    /// </summary>
    public IReadOnlyList<string> RoleRefs { get; init; } = [];
}
