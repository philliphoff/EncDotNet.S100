namespace EncDotNet.S100.Features;

/// <summary>
/// An information type from the Feature Catalogue: a class of non-spatial information (e.g.
/// contact details or a nautical publication reference) that features and other information
/// types reference through <see cref="InformationBinding"/>s. Parsed from
/// <c>S100_FC_InformationTypes/S100_FC_InformationType</c>; compare <see cref="FeatureType"/>.
/// </summary>
public sealed class InformationType
{
    /// <summary>Human-readable name of the information type (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the information type (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>
    /// Code that identifies the information type in the catalogue and in datasets (<c>code</c>);
    /// the key other catalogue entries use to reference it.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Alternative name (<c>alias</c>), typically the legacy S-57 acronym; <see langword="null"/> when absent.</summary>
    public string? Alias { get; init; }

    /// <summary>Additional explanatory remarks (<c>remarks</c>), or <see langword="null"/> when absent.</summary>
    public string? Remarks { get; init; }

    /// <summary>
    /// <see langword="true"/> when the element's <c>isAbstract</c> XML attribute is <c>"true"</c>;
    /// abstract types serve only as a <c>superType</c> for others and have no instances.
    /// </summary>
    public bool IsAbstract { get; init; }

    /// <summary>The code of the super type, if any.</summary>
    public string? SuperType { get; init; }

    /// <summary>
    /// Attributes declared directly on this information type (<c>attributeBinding</c>), in
    /// catalogue order; bindings inherited from <see cref="SuperType"/> are not merged in.
    /// </summary>
    public IReadOnlyList<AttributeBinding> AttributeBindings { get; init; } = [];

    /// <summary>Information associations this information type may take part in (<c>informationBinding</c>), in catalogue order.</summary>
    public IReadOnlyList<InformationBinding> InformationBindings { get; init; } = [];
}
