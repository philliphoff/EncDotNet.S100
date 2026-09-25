namespace EncDotNet.S100.Features;

/// <summary>
/// Binds a member attribute into a <see cref="ComplexAttribute"/>, with the multiplicity
/// that applies to it there. Parsed from an <c>S100FC:subAttributeBinding</c> element;
/// the counterpart of <see cref="AttributeBinding"/> for complex-attribute members.
/// </summary>
public sealed class SubAttributeBinding
{
    /// <summary>How many values of the sub-attribute the complex attribute may carry (<c>multiplicity</c>).</summary>
    public required Multiplicity Multiplicity { get; init; }

    /// <summary>
    /// Code of the bound simple or complex attribute, from the <c>ref</c> attribute of the
    /// <c>attribute</c> child element.
    /// </summary>
    public required string AttributeRef { get; init; }

    /// <summary>
    /// <see langword="true"/> when the binding's <c>sequential</c> XML attribute is
    /// <c>"true"</c>, i.e. the order of repeated values is significant; <see langword="false"/>
    /// otherwise.
    /// </summary>
    public bool Sequential { get; init; }
}
