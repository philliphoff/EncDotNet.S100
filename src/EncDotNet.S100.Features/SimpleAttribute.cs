namespace EncDotNet.S100.Features;

/// <summary>
/// A simple (scalar) attribute from the Feature Catalogue, parsed from
/// <c>S100_FC_SimpleAttributes/S100_FC_SimpleAttribute</c>. Types bind it through an
/// <see cref="AttributeBinding"/>, and a <see cref="ComplexAttribute"/> through a
/// <see cref="SubAttributeBinding"/>; enumerated attributes carry <see cref="ListedValue"/>s.
/// </summary>
public sealed class SimpleAttribute
{
    /// <summary>Human-readable name of the attribute (<c>name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Prose definition of the attribute (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>
    /// Code that identifies the attribute in the catalogue and in datasets (<c>code</c>); the key
    /// bindings use to reference it.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Alternative name (<c>alias</c>), typically the legacy S-57 acronym; <see langword="null"/> when absent.</summary>
    public string? Alias { get; init; }

    /// <summary>Additional explanatory remarks (<c>remarks</c>), or <see langword="null"/> when absent.</summary>
    public string? Remarks { get; init; }

    /// <summary>
    /// The attribute's value type (<c>valueType</c>) as written in the catalogue, e.g.
    /// <c>"boolean"</c>, <c>"enumeration"</c>, <c>"integer"</c>, <c>"real"</c>, <c>"text"</c>
    /// or <c>"date"</c>.
    /// </summary>
    public required string ValueType { get; init; }

    /// <summary>
    /// The unit of measure declared for this attribute in the Feature
    /// Catalogue (<c>&lt;S100FC:uom&gt;</c>), or <c>null</c> when the
    /// attribute carries no unit (e.g. enumerations, text, or unitless
    /// quantities). Numeric depth/height attributes such as
    /// <c>depthRangeMinimumValue</c> resolve to <c>metre</c> / <c>m</c>.
    /// </summary>
    public UnitOfMeasure? Uom { get; init; }

    /// <summary>
    /// Enumerants of an enumerated attribute (<c>listedValues/listedValue</c>), in catalogue order.
    /// Empty for non-enumerated attributes.
    /// </summary>
    public IReadOnlyList<ListedValue> ListedValues { get; init; } = [];
}
