namespace EncDotNet.S100.Features;

/// <summary>
/// Unit of measure for a simple attribute whose value type is numeric,
/// parsed from the Feature Catalogue's <c>&lt;S100FC:uom&gt;</c> element
/// (S-100 Part 5 / ISO 19103). For example, depth-valued S-101 attributes
/// such as <c>depthRangeMinimumValue</c> carry a uom of
/// <see cref="Name"/> = <c>"metre"</c>, <see cref="Symbol"/> = <c>"m"</c>.
/// </summary>
/// <param name="Name">The unit name (e.g. <c>"metre"</c>).</param>
/// <param name="Symbol">The unit symbol (e.g. <c>"m"</c>), or <c>null</c> when the catalogue omits it.</param>
public sealed record UnitOfMeasure(string Name, string? Symbol);
