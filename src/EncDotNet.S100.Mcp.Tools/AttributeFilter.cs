using System.Collections.Immutable;
using System.ComponentModel;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// A single attribute predicate used to narrow feature queries by
/// attribute content rather than geometry alone.
/// </summary>
/// <param name="Code">
/// The attribute code (the GML element local name, e.g.
/// <c>categoryOfRestrictedArea</c>). Matched case-insensitively against
/// both simple attributes and the sub-attributes of complex attributes.
/// </param>
/// <param name="Value">
/// The required value. When <c>null</c> or empty the predicate matches
/// any feature that <em>carries</em> the attribute (a presence test);
/// otherwise the feature's attribute value must equal this string,
/// compared case-insensitively after trimming surrounding whitespace.
/// </param>
public sealed record AttributePredicate(
    [property: Description("Attribute code (GML element local name); matched case-insensitively.")] string Code,
    [property: Description("Required value (case-insensitive, trimmed); null/empty means \"attribute is present with any value\".")] string? Value = null);

/// <summary>
/// A conjunction of <see cref="AttributePredicate"/>s. A feature
/// satisfies the filter only when it satisfies <em>every</em> predicate
/// (logical AND). An empty filter matches every feature.
/// </summary>
/// <param name="Predicates">The predicates to apply.</param>
public sealed record AttributeFilter(
    [property: Description("Attribute predicates combined with logical AND; a feature must satisfy all of them.")] ImmutableArray<AttributePredicate> Predicates)
{
    /// <summary><c>true</c> when there are no predicates to apply.</summary>
    public bool IsEmpty => Predicates.IsDefaultOrEmpty;
}
