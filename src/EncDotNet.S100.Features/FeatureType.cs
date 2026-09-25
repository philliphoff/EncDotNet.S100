namespace EncDotNet.S100.Features;

/// <summary>
/// A feature type from the Feature Catalogue: a class of real-world phenomenon (e.g. a depth
/// area or a light) whose instances are normally located by geometry (see
/// <see cref="PermittedPrimitives"/>), parsed from
/// <c>S100_FC_FeatureTypes/S100_FC_FeatureType</c>. It binds attributes via
/// <see cref="AttributeBinding"/>s and relates to other types via <see cref="FeatureBinding"/>s
/// and <see cref="InformationBinding"/>s. Compare <see cref="InformationType"/>, which has no
/// geometry.
/// </summary>
public sealed class FeatureType
{
    /// <summary>Human-readable name of the feature type (<c>name</c>), e.g. <c>"Depth Area"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the feature type (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>
    /// Code that identifies the feature type in the catalogue and in datasets (<c>code</c>), e.g.
    /// <c>"DepthArea"</c>; the key other catalogue entries use to reference it.
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
    /// Attributes declared directly on this feature type (<c>attributeBinding</c>), in catalogue
    /// order; bindings inherited from <see cref="SuperType"/> are not merged in.
    /// </summary>
    public IReadOnlyList<AttributeBinding> AttributeBindings { get; init; } = [];

    /// <summary>Feature associations this feature type may take part in (<c>featureBinding</c>), in catalogue order.</summary>
    public IReadOnlyList<FeatureBinding> FeatureBindings { get; init; } = [];

    /// <summary>Information associations this feature type may take part in (<c>informationBinding</c>), in catalogue order.</summary>
    public IReadOnlyList<InformationBinding> InformationBindings { get; init; } = [];

    /// <summary>
    /// Category of use (<c>featureUseType</c>), e.g. <c>"geographic"</c>, <c>"meta"</c>,
    /// <c>"cartographic"</c> or <c>"theme"</c>; <see langword="null"/> when absent.
    /// </summary>
    public string? FeatureUseType { get; init; }

    /// <summary>
    /// Spatial primitives instances may use (<c>permittedPrimitives</c>), e.g. <c>"point"</c>,
    /// <c>"curve"</c>, <c>"surface"</c> or <c>"noGeometry"</c>. Empty when none are declared.
    /// </summary>
    public IReadOnlyList<string> PermittedPrimitives { get; init; } = [];
}
