namespace EncDotNet.S100.Validation;

/// <summary>
/// A single S-100 validation rule that examines an instance of
/// <typeparamref name="TModel"/> and yields zero or more
/// <see cref="ValidationFinding"/> entries.
/// </summary>
/// <typeparam name="TModel">
/// The typed data model the rule operates on, typically a typed dataset
/// projection such as <c>S421RoutePlan</c>, <c>S124Dataset</c>, or
/// <c>S102Dataset</c>.
/// </typeparam>
/// <remarks>
/// <para>
/// Implementations must be pure with respect to the input — repeated
/// evaluation of the same model with the same context must yield equivalent
/// findings. Rules must not throw for malformed input; they should surface
/// suspicions as findings instead. (The runner catches exceptions defensively
/// to guarantee report aggregation even when a single rule misbehaves.)
/// </para>
/// <para>
/// The fluent <see cref="ValidationRuleBuilder.RuleFor{TModel}"/> entry
/// point is the recommended way to author simple property-style rules.
/// More complex rules — for example Tier-3 rules that need to reach a
/// sibling dataset via <see cref="ValidationContext.Services"/> — should
/// implement this interface directly.
/// </para>
/// </remarks>
public interface IValidationRule<in TModel>
{
    /// <summary>
    /// Stable identifier traceable to a clause of the relevant IHO product
    /// specification (e.g. <c>"S421-R-3.1"</c>).
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Human-readable summary of the normative requirement this rule
    /// enforces. Surfaced in tooling and documentation.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The default severity used by findings this rule emits when the
    /// rule itself does not override it per-finding.
    /// </summary>
    ValidationSeverity DefaultSeverity { get; }

    /// <summary>
    /// Evaluates the rule against <paramref name="model"/>, returning all
    /// findings produced. Returns an empty sequence when no issues are
    /// found.
    /// </summary>
    IEnumerable<ValidationFinding> Evaluate(TModel model, ValidationContext context);
}
