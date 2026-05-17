using System.Collections.Immutable;

namespace EncDotNet.S100.Validation;

/// <summary>
/// Aggregated outcome of running a <see cref="ValidationRuleSet{TModel}"/>
/// against a typed dataset model.
/// </summary>
/// <param name="Findings">All findings emitted across all rules, preserving the order in which they were produced.</param>
/// <param name="RulesEvaluated">The number of rules that were evaluated.</param>
/// <param name="RulesWithFindings">The number of rules that emitted at least one finding.</param>
public sealed record ValidationReport(
    ImmutableArray<ValidationFinding> Findings,
    int RulesEvaluated,
    int RulesWithFindings)
{
    /// <summary>True when no rule emitted a finding.</summary>
    public bool IsValid => Findings.IsDefaultOrEmpty;

    /// <summary>True when any finding has severity <see cref="ValidationSeverity.Error"/>.</summary>
    public bool HasErrors => !Findings.IsDefaultOrEmpty && Findings.Any(f => f.Severity == ValidationSeverity.Error);

    /// <summary>True when any finding has severity <see cref="ValidationSeverity.Warning"/>.</summary>
    public bool HasWarnings => !Findings.IsDefaultOrEmpty && Findings.Any(f => f.Severity == ValidationSeverity.Warning);

    /// <summary>Returns findings filtered to a particular severity.</summary>
    public IEnumerable<ValidationFinding> FindingsOfSeverity(ValidationSeverity severity)
        => Findings.IsDefaultOrEmpty
            ? Enumerable.Empty<ValidationFinding>()
            : Findings.Where(f => f.Severity == severity);

    /// <summary>An empty report (no rules, no findings).</summary>
    public static ValidationReport Empty { get; } =
        new(ImmutableArray<ValidationFinding>.Empty, RulesEvaluated: 0, RulesWithFindings: 0);
}
