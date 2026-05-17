using System.Collections.Immutable;

namespace EncDotNet.S100.Validation;

/// <summary>
/// A composable, ordered collection of <see cref="IValidationRule{TModel}"/>
/// for the same typed data model. Each per-spec library exposes a default
/// rule set via a static <c>SxxxRules.Default()</c> factory; consumers may
/// build custom sets by adding, removing, or replacing rules.
/// </summary>
/// <typeparam name="TModel">The typed data-model type the rules operate on.</typeparam>
public sealed class ValidationRuleSet<TModel>
{
    /// <summary>The rules in this set, in evaluation order.</summary>
    public ImmutableArray<IValidationRule<TModel>> Rules { get; }

    /// <summary>Creates a rule set from the supplied rules.</summary>
    public ValidationRuleSet(IEnumerable<IValidationRule<TModel>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules.ToImmutableArray();
    }

    /// <summary>Creates a rule set from the supplied rules.</summary>
    public ValidationRuleSet(params IValidationRule<TModel>[] rules)
        : this((IEnumerable<IValidationRule<TModel>>)rules) { }

    /// <summary>An empty rule set — evaluating it always returns <see cref="ValidationReport.Empty"/>.</summary>
    public static ValidationRuleSet<TModel> Empty { get; } = new(Array.Empty<IValidationRule<TModel>>());

    /// <summary>Returns a new rule set with <paramref name="rule"/> appended.</summary>
    public ValidationRuleSet<TModel> Add(IValidationRule<TModel> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new ValidationRuleSet<TModel>(Rules.Add(rule));
    }

    /// <summary>
    /// Returns a new rule set with every rule whose <see cref="IValidationRule{TModel}.RuleId"/>
    /// equals <paramref name="ruleId"/> removed.
    /// </summary>
    public ValidationRuleSet<TModel> Remove(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        return new ValidationRuleSet<TModel>(
            Rules.Where(r => !string.Equals(r.RuleId, ruleId, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Evaluates every rule in this set against <paramref name="model"/> and
    /// returns the aggregated <see cref="ValidationReport"/>. All rules are
    /// evaluated regardless of any errors found by earlier rules (lint-pass
    /// semantics). Exceptions thrown from a rule are caught and surfaced as
    /// a synthetic <see cref="ValidationSeverity.Error"/> finding so that a
    /// single faulty rule cannot abort the report.
    /// </summary>
    public ValidationReport Run(TModel model, ValidationContext? context = null)
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));

        var ctx = context ?? ValidationContext.Default;
        var findings = ImmutableArray.CreateBuilder<ValidationFinding>();
        var rulesWithFindings = 0;

        foreach (var rule in Rules)
        {
            var beforeCount = findings.Count;
            try
            {
                foreach (var finding in rule.Evaluate(model, ctx))
                {
                    if (finding is null) continue;
                    findings.Add(finding);
                }
            }
            catch (Exception ex)
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = rule.RuleId,
                    Severity = ValidationSeverity.Error,
                    Message = $"Rule '{rule.RuleId}' threw {ex.GetType().Name}: {ex.Message}",
                });
            }

            if (findings.Count > beforeCount)
                rulesWithFindings++;
        }

        return new ValidationReport(findings.ToImmutable(), Rules.Length, rulesWithFindings);
    }
}
