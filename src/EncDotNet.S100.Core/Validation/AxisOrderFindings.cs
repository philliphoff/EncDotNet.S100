using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Validation;

/// <summary>
/// Turns a dataset's <see cref="AxisOrderAssessment"/> (issue #650) into a
/// validation finding, so <c>s100 validate</c>, the viewer and MCP report
/// swapped or inconsistent axis order alongside the product's rule pack.
/// </summary>
/// <remarks>
/// The rule id is <c>{product}-PROJ-AXIS</c> (for example
/// <c>S104-PROJ-AXIS</c>), following the <c>-PROJ-</c> convention for
/// findings the reader, rather than a normative rule, produces. The finding is
/// a <see cref="ValidationSeverity.Warning"/>, raised when the positions were
/// swapped or fit the declared extent in neither order
/// (<see cref="AxisOrderAssessment.IsWarning"/>). An unverifiable order is not
/// a defect (the GML envelope is optional), so it yields no finding; hosts
/// report it from <see cref="AxisOrderAssessment"/> directly.
/// </remarks>
public static class AxisOrderFindings
{
    /// <summary>
    /// The rule id for <paramref name="productName"/> (e.g. <c>"S-104"</c>
    /// gives <c>"S104-PROJ-AXIS"</c>).
    /// </summary>
    public static string RuleIdFor(string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        return $"{productName.Replace("-", string.Empty, StringComparison.Ordinal)}-PROJ-AXIS";
    }

    /// <summary>
    /// Returns the finding for <paramref name="assessment"/>, or <c>null</c>
    /// when there is nothing to report.
    /// </summary>
    /// <param name="assessment">The dataset's axis-order assessment, or <c>null</c>.</param>
    /// <param name="productName">The product, e.g. <c>"S-104"</c>.</param>
    public static ValidationFinding? ToFinding(AxisOrderAssessment? assessment, string productName)
    {
        if (assessment is null || !assessment.IsWarning)
            return null;

        return new ValidationFinding
        {
            RuleId = RuleIdFor(productName),
            Severity = ValidationSeverity.Warning,
            Message = assessment.BuildMessage(),
            BoundingBox = assessment.DeclaredExtent,
        };
    }

    /// <summary>
    /// Appends the axis-order finding to <paramref name="report"/>, counting
    /// the check as one evaluated rule whenever an assessment exists.
    /// </summary>
    /// <param name="report">The product rule pack's report, or <c>null</c> when the product has none.</param>
    /// <param name="assessment">The dataset's axis-order assessment, or <c>null</c>.</param>
    /// <param name="productName">The product, e.g. <c>"S-104"</c>.</param>
    /// <returns>
    /// <paramref name="report"/> unchanged when there is no assessment;
    /// otherwise a report that includes the check.
    /// </returns>
    public static ValidationReport? Append(ValidationReport? report, AxisOrderAssessment? assessment, string productName)
    {
        if (assessment is null)
            return report;

        var finding = ToFinding(assessment, productName);
        var baseReport = report ?? ValidationReport.Empty;
        return new ValidationReport(
            finding is null ? baseReport.Findings : [.. baseReport.Findings, finding],
            baseReport.RulesEvaluated + 1,
            baseReport.RulesWithFindings + (finding is null ? 0 : 1));
    }
}
