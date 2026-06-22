namespace EncDotNet.S100.Features;

public sealed class SimpleAttribute
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }

    public string? Alias { get; init; }

    public string? Remarks { get; init; }

    public required string ValueType { get; init; }

    /// <summary>
    /// The unit of measure declared for this attribute in the Feature
    /// Catalogue (<c>&lt;S100FC:uom&gt;</c>), or <c>null</c> when the
    /// attribute carries no unit (e.g. enumerations, text, or unitless
    /// quantities). Numeric depth/height attributes such as
    /// <c>depthRangeMinimumValue</c> resolve to <c>metre</c> / <c>m</c>.
    /// </summary>
    public UnitOfMeasure? Uom { get; init; }

    public IReadOnlyList<ListedValue> ListedValues { get; init; } = [];
}
