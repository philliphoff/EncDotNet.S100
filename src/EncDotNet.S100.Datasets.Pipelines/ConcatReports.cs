using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using EncDotNet.S100.Validation;

[assembly: InternalsVisibleTo("EncDotNet.S100.Pipelines.Tests")]

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Helper for joining two <see cref="ValidationReport"/>s into a single
/// aggregated report. Lives alongside <see cref="ValidationRunner"/> so
/// per-spec dataset processors that run more than one rule pack (for
/// example, S-57 running both <c>S57PreTranslationRules</c> against the
/// raw document and <c>S101DatasetRules</c> against the translated
/// document — see <c>docs/design/non-gml-validation.md</c> §9.3) can
/// concatenate their results uniformly.
/// </summary>
/// <remarks>
/// V-1 (S-102) does not itself produce two reports per dataset; this
/// helper lands in V-1 only so that V-5 (S-57 pre-translation pack)
/// remains a leaf PR with no framework prerequisites.
/// </remarks>
internal static class ConcatReports
{
    /// <summary>
    /// Returns a new <see cref="ValidationReport"/> whose findings are
    /// the concatenation of <paramref name="a"/> followed by
    /// <paramref name="b"/>, preserving each report's internal order.
    /// </summary>
    /// <param name="a">First report. Its rule ids are kept verbatim.</param>
    /// <param name="b">Second report. When <paramref name="rebadgePrefix"/>
    /// is non-null, every finding from <paramref name="b"/> has its
    /// <see cref="ValidationFinding.RuleId"/> rewritten as
    /// <c>rebadgePrefix + originalRuleId</c>.</param>
    /// <param name="rebadgePrefix">Optional prefix applied to every
    /// <paramref name="b"/> finding's rule id. Used by S-57 to mark
    /// findings inherited from the translated S-101 document
    /// (<c>"S101-as-S57/"</c>) so downstream filters can distinguish
    /// native vs. translated rule sources.</param>
    /// <returns>
    /// A combined report whose <see cref="ValidationReport.RulesEvaluated"/>
    /// and <see cref="ValidationReport.RulesWithFindings"/> are the
    /// arithmetic sums of the inputs'.
    /// </returns>
    public static ValidationReport Concat(
        ValidationReport a,
        ValidationReport b,
        string? rebadgePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var builder = ImmutableArray.CreateBuilder<ValidationFinding>();

        if (!a.Findings.IsDefaultOrEmpty)
            builder.AddRange(a.Findings);

        if (!b.Findings.IsDefaultOrEmpty)
        {
            foreach (var finding in b.Findings)
            {
                if (rebadgePrefix is null)
                {
                    builder.Add(finding);
                }
                else
                {
                    builder.Add(finding with { RuleId = rebadgePrefix + finding.RuleId });
                }
            }
        }

        return new ValidationReport(
            builder.ToImmutable(),
            RulesEvaluated: a.RulesEvaluated + b.RulesEvaluated,
            RulesWithFindings: a.RulesWithFindings + b.RulesWithFindings);
    }
}
