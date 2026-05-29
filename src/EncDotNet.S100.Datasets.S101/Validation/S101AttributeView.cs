namespace EncDotNet.S100.Datasets.S101.Validation;

/// <summary>
/// A spec-vocabulary view over a single <see cref="S101Attribute"/> row
/// of a feature or information record.
/// </summary>
/// <remarks>
/// <para>
/// Implements the façade contract described in
/// <c>docs/design/non-gml-validation.md</c> §3.1 (input model option (b),
/// "thin spec-aligned façade"). Rule authors read attribute acronyms
/// (e.g. <c>"DRVAL1"</c>) and string values; numeric-code-to-acronym
/// resolution against <see cref="S101Document.AttributeTypeCatalogue"/>
/// happens once at façade construction, not in rule bodies.
/// </para>
/// <para>
/// A view with a <c>null</c> <see cref="Acronym"/> represents an
/// attribute whose numeric code did not resolve against the dataset's
/// embedded attribute catalogue — this is the failure mode reported by
/// <c>S101-R-1.2</c>. The raw <see cref="NumericCode"/> remains
/// available so the finding can cite it.
/// </para>
/// </remarks>
public sealed class S101AttributeView
{
    /// <summary>
    /// Attribute acronym resolved through the dataset's
    /// <see cref="S101Document.AttributeTypeCatalogue"/> — for example
    /// <c>"DRVAL1"</c> or <c>"OBJNAM"</c>. <c>null</c> when the numeric
    /// code is not present in the catalogue.
    /// </summary>
    public string? Acronym { get; init; }

    /// <summary>The raw numeric attribute code from the ATTR field (FRID record).</summary>
    public ushort NumericCode { get; init; }

    /// <summary>
    /// Sequence index within a complex-attribute instance. The marker
    /// row uses <c>1</c>; subsequent sub-rows belonging to the same
    /// complex-attribute instance carry monotonically increasing
    /// indices. Simple attributes always carry <c>1</c>.
    /// </summary>
    public ushort Index { get; init; }

    /// <summary>
    /// Attribute value as a string. Numeric and enumerated values are
    /// stringified by the parser. The empty string is preserved as
    /// <see cref="string.Empty"/> so rules can distinguish "set but
    /// empty" from "absent". <c>null</c> only when the parser produced
    /// no value at all.
    /// </summary>
    public string? Value { get; init; }
}
