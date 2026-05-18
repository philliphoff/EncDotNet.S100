using System;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Shared helper for spec-specific <see cref="IDatasetProcessor.Validate"/>
/// overrides. Centralises projection + rule-pack invocation so each GML
/// processor's <c>Validate</c> override stays a one-liner and so that
/// projection failures (typically <see cref="InvalidOperationException"/>
/// from <c>S&lt;Spec&gt;Dataset.From</c> on an empty dataset) and rule-pack
/// exceptions never bubble out of <see cref="IDatasetProcessor.Validate"/>.
/// </summary>
internal static class ValidationRunner
{
    /// <summary>
    /// Projects <paramref name="rawDataset"/> with <paramref name="project"/>
    /// and runs <paramref name="ruleSet"/> against the result. Returns
    /// <see cref="ValidationReport.Empty"/> when projection throws (e.g. an
    /// empty dataset) or the rule pack itself throws.
    /// </summary>
    public static ValidationReport Run<TRaw, TTyped>(
        TRaw rawDataset,
        Func<TRaw, TTyped> project,
        ValidationRuleSet<TTyped> ruleSet)
    {
        ArgumentNullException.ThrowIfNull(rawDataset);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ruleSet);

        TTyped typed;
        try
        {
            typed = project(rawDataset);
        }
        catch (Exception)
        {
            // Projection failures (typically "dataset is empty") leave us
            // with no typed model to evaluate. Surface as an empty report
            // rather than a null — the loader treats null as "no rule pack
            // for this spec at all", which is a different condition.
            return ValidationReport.Empty;
        }

        return ruleSet.Run(typed!);
    }
}
