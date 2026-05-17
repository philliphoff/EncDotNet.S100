using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Validation;

/// <summary>
/// Fluent entry point for authoring <see cref="IValidationRule{TModel}"/>
/// instances. Intended for the common case of a single property-style rule
/// — Tier-1 schema rules and most Tier-2 spec-semantic rules. More complex
/// rules (multi-finding emitters, Tier-3 cross-dataset rules) should
/// implement <see cref="IValidationRule{TModel}"/> directly.
/// </summary>
/// <example>
/// <code>
/// var rule = ValidationRuleBuilder.RuleFor&lt;S421RoutePlan&gt;("S421-R-3.1")
///     .WithDescription("A route must contain at least two waypoints.")
///     .WithSeverity(ValidationSeverity.Error)
///     .Check(plan =&gt; plan.Route.Waypoints.Length &gt;= 2,
///            failureMessage: "Route contains fewer than two waypoints.")
///     .Build();
/// </code>
/// </example>
public static class ValidationRuleBuilder
{
    /// <summary>
    /// Starts a fluent definition of a rule with the given identifier.
    /// </summary>
    /// <typeparam name="TModel">The data-model type the rule operates on.</typeparam>
    /// <param name="ruleId">Stable rule identifier (e.g. <c>"S421-R-3.1"</c>).</param>
    public static IRuleBuilder<TModel> RuleFor<TModel>(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        return new RuleBuilder<TModel>(ruleId);
    }

    /// <summary>Fluent builder for a single validation rule.</summary>
    public interface IRuleBuilder<TModel>
    {
        /// <summary>Sets the human-readable description of the rule.</summary>
        IRuleBuilder<TModel> WithDescription(string description);

        /// <summary>Sets the default severity used by findings the rule emits.</summary>
        IRuleBuilder<TModel> WithSeverity(ValidationSeverity severity);

        /// <summary>
        /// Defines the rule as a single boolean predicate. The predicate
        /// must return <c>true</c> when the model conforms; the rule will
        /// emit a finding when it returns <c>false</c>.
        /// </summary>
        /// <param name="predicate">Returns <c>true</c> when the model conforms.</param>
        /// <param name="failureMessage">
        /// Message attached to the emitted finding when the predicate fails.
        /// Defaults to the rule's description.
        /// </param>
        /// <param name="locator">
        /// Optional function returning the point location to attach to the
        /// finding when the predicate fails. Useful for findings tied to a
        /// specific feature.
        /// </param>
        IRuleBuilder<TModel> Check(
            Func<TModel, bool> predicate,
            string? failureMessage = null,
            Func<TModel, GeoPosition?>? locator = null);

        /// <summary>
        /// Defines the rule as a multi-finding producer. Use this form when
        /// a single rule may emit several findings (for example, one finding
        /// per offending waypoint).
        /// </summary>
        IRuleBuilder<TModel> Yield(Func<TModel, ValidationContext, IEnumerable<ValidationFinding>> producer);

        /// <summary>Builds the configured <see cref="IValidationRule{TModel}"/>.</summary>
        IValidationRule<TModel> Build();
    }

    private sealed class RuleBuilder<TModel> : IRuleBuilder<TModel>
    {
        private readonly string _ruleId;
        private string _description = string.Empty;
        private ValidationSeverity _severity = ValidationSeverity.Error;
        private Func<TModel, ValidationContext, IEnumerable<ValidationFinding>>? _producer;

        public RuleBuilder(string ruleId) => _ruleId = ruleId;

        public IRuleBuilder<TModel> WithDescription(string description)
        {
            ArgumentNullException.ThrowIfNull(description);
            _description = description;
            return this;
        }

        public IRuleBuilder<TModel> WithSeverity(ValidationSeverity severity)
        {
            _severity = severity;
            return this;
        }

        public IRuleBuilder<TModel> Check(
            Func<TModel, bool> predicate,
            string? failureMessage = null,
            Func<TModel, GeoPosition?>? locator = null)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            _producer = (model, _) =>
            {
                if (predicate(model))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = _ruleId,
                        Severity = _severity,
                        Message = failureMessage ?? _description,
                        Point = locator?.Invoke(model),
                    },
                };
            };
            return this;
        }

        public IRuleBuilder<TModel> Yield(Func<TModel, ValidationContext, IEnumerable<ValidationFinding>> producer)
        {
            ArgumentNullException.ThrowIfNull(producer);
            _producer = producer;
            return this;
        }

        public IValidationRule<TModel> Build()
        {
            if (_producer is null)
                throw new InvalidOperationException(
                    $"Rule '{_ruleId}' has no body — call Check(...) or Yield(...) before Build().");

            return new DelegateValidationRule<TModel>(_ruleId, _description, _severity, _producer);
        }
    }

    private sealed class DelegateValidationRule<TModel> : IValidationRule<TModel>
    {
        private readonly Func<TModel, ValidationContext, IEnumerable<ValidationFinding>> _producer;

        public DelegateValidationRule(
            string ruleId,
            string description,
            ValidationSeverity defaultSeverity,
            Func<TModel, ValidationContext, IEnumerable<ValidationFinding>> producer)
        {
            RuleId = ruleId;
            Description = description;
            DefaultSeverity = defaultSeverity;
            _producer = producer;
        }

        public string RuleId { get; }
        public string Description { get; }
        public ValidationSeverity DefaultSeverity { get; }

        public IEnumerable<ValidationFinding> Evaluate(TModel model, ValidationContext context)
            => _producer(model, context);
    }
}
