namespace EncDotNet.S100.Features;

/// <summary>
/// A complex attribute from the Feature Catalogue: a named group of simple and/or complex
/// sub-attributes, each bound through a <see cref="SubAttributeBinding"/>. Parsed from
/// <c>S100_FC_ComplexAttributes/S100_FC_ComplexAttribute</c>; bound to types like any other
/// attribute via <see cref="AttributeBinding"/>.
/// </summary>
public sealed class ComplexAttribute
{
    /// <summary>Human-readable name of the complex attribute (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the complex attribute (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>
    /// Code that identifies the complex attribute in the catalogue and in datasets (<c>code</c>); the key
    /// bindings use to reference it.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Alternative name (<c>alias</c>), typically the legacy S-57 acronym; <see langword="null"/> when absent.</summary>
    public string? Alias { get; init; }

    /// <summary>Additional explanatory remarks (<c>remarks</c>), or <see langword="null"/> when absent.</summary>
    public string? Remarks { get; init; }

    /// <summary>Member attributes of the complex attribute (<c>subAttributeBinding</c>), in catalogue order.</summary>
    public IReadOnlyList<SubAttributeBinding> SubAttributeBindings { get; init; } = [];
}
