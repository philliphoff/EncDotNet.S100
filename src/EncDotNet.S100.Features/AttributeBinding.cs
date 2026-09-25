namespace EncDotNet.S100.Features;

/// <summary>
/// Binds a <see cref="SimpleAttribute"/> or <see cref="ComplexAttribute"/> to a
/// <see cref="FeatureType"/> or <see cref="InformationType"/>, with the multiplicity
/// and value restrictions that apply to it on that type. Parsed from an
/// <c>S100FC:attributeBinding</c> element.
/// </summary>
public sealed class AttributeBinding
{
    /// <summary>How many values of the attribute the owning type may carry (<c>multiplicity</c>).</summary>
    public required Multiplicity Multiplicity { get; init; }

    /// <summary>
    /// Code of the bound simple or complex attribute, from the <c>ref</c> attribute of the
    /// <c>attribute</c> child element (e.g. <c>"depthRangeMinimumValue"</c>).
    /// </summary>
    public required string AttributeRef { get; init; }

    /// <summary>
    /// <see langword="true"/> when the binding's <c>sequential</c> XML attribute is
    /// <c>"true"</c>, i.e. the order of repeated values is significant; <see langword="false"/>
    /// when the attribute is absent or any other value.
    /// </summary>
    public bool Sequential { get; init; }

    /// <summary>
    /// Listed-value codes the owning type restricts an enumerated attribute to, from
    /// <c>permittedValues/value</c>. Empty when the binding places no restriction.
    /// </summary>
    public IReadOnlyList<string> PermittedValues { get; init; } = [];
}
