namespace EncDotNet.S100.Features;

/// <summary>
/// A named end of a <see cref="FeatureAssociation"/> or <see cref="InformationAssociation"/>,
/// parsed from <c>S100_FC_Roles/S100_FC_Role</c>. Associations list their roles by code in
/// <see cref="FeatureAssociation.RoleRefs"/>, and bindings select one via
/// <see cref="FeatureBinding.RoleRef"/> / <see cref="InformationBinding.RoleRef"/>.
/// </summary>
public sealed class Role
{
    /// <summary>Human-readable name of the role (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the role (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>Code that identifies the role in the catalogue (<c>code</c>); the target of role references.</summary>
    public required string Code { get; init; }
}
