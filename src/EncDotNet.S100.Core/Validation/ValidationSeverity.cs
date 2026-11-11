namespace EncDotNet.S100.Validation;

/// <summary>
/// Severity of a <see cref="ValidationFinding"/> emitted while evaluating a
/// dataset against an S-100 product specification's normative rules.
/// </summary>
/// <remarks>
/// Severity is informational only — the validation runner never short-circuits
/// on encountering an <see cref="Error"/>. All rules in a
/// <see cref="ValidationRuleSet{TModel}"/> are evaluated and all findings are
/// collected, in the spirit of a linting pass rather than fail-fast validation.
/// </remarks>
public enum ValidationSeverity
{
    /// <summary>Informational finding (e.g. an optional attribute is absent).</summary>
    Info,

    /// <summary>A finding that does not violate a normative requirement but warrants attention.</summary>
    Warning,

    /// <summary>A finding that violates a normative requirement of the product specification.</summary>
    Error,
}
