using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// One row in a feature's pick / object-info attribute display. Carries
/// both the raw on-the-wire shape (<see cref="Code"/> /
/// <see cref="RawValue"/>) and a Feature-Catalogue-decoded shape
/// (<see cref="Name"/> / <see cref="DisplayValue"/>) so a viewer can show
/// the friendly form by default while keeping the raw values available
/// in tooltips.
/// </summary>
/// <remarks>
/// Trees are formed by populating <see cref="Children"/>: complex
/// attributes are surfaced as a parent <c>PickAttribute</c> (with the
/// complex-attribute code/name and an empty value) plus one child per
/// sub-attribute. Decoding obeys the S-100 Edition 5.2.1 Part 5 Feature
/// Catalogue (ISO 19110): simple-attribute codes resolve to their
/// human-readable name, listed-value codes resolve to their label.
/// </remarks>
public sealed class PickAttribute
{
    /// <summary>The raw on-the-wire attribute or sub-attribute code (e.g. <c>"OBJNAM"</c>, <c>"CATPIB"</c>).</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable attribute name resolved through the dataset's
    /// Feature Catalogue, or <c>null</c> when no FC was available or the
    /// code is not defined.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The unmodified value as written in the dataset.</summary>
    public required string RawValue { get; init; }

    /// <summary>
    /// Friendly display value resolved through the dataset's Feature
    /// Catalogue (e.g. listed-value label), or <c>null</c> when no
    /// decoding applies.
    /// </summary>
    public string? DisplayValue { get; init; }

    /// <summary>
    /// Optional typed timestamp value backing this attribute. When set,
    /// presentation layers should re-format the value through their own
    /// date/time formatter rather than relying on <see cref="RawValue"/>
    /// or <see cref="DisplayValue"/>. <see cref="RawValue"/> still
    /// carries an ISO 8601 representation for non-UI consumers
    /// (serialization, MCP, tests).
    /// </summary>
    public DateTime? DateTimeValue { get; init; }

    /// <summary>
    /// Optional typed time-range backing this attribute (start, end).
    /// Same contract as <see cref="DateTimeValue"/>.
    /// </summary>
    public (DateTime Start, DateTime End)? DateTimeRangeValue { get; init; }

    /// <summary>
    /// Sub-attributes of a complex attribute. Empty for simple attributes
    /// and for leaf rows of a complex attribute.
    /// </summary>
    public IReadOnlyList<PickAttribute> Children { get; init; } = [];

    /// <summary>
    /// The label most viewers want to render: <see cref="Name"/> when
    /// available, otherwise <see cref="Code"/>.
    /// </summary>
    public string DisplayName => Name ?? Code;

    /// <summary>
    /// The value most viewers want to render: <see cref="DisplayValue"/>
    /// when available, otherwise <see cref="RawValue"/>.
    /// </summary>
    public string DisplayText => DisplayValue ?? RawValue;
}
