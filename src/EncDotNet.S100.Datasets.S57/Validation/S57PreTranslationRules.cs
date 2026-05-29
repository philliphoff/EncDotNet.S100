using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Validation;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of pre-translation
/// normative rules for a legacy S-57 ENC dataset (parsed
/// <see cref="S57Document"/>). Captures the small set of checks that
/// do <b>not</b> survive translation to S-101 and therefore must run
/// before <c>S57ToS101Translator</c> hands off to the S-101 rule pack.
/// </summary>
/// <remarks>
/// <para>
/// This is the V-5 rule pack as defined in
/// <c>docs/design/non-gml-validation.md</c> §6.5. Most S-57 quality is
/// best assessed <i>after</i> translation: <c>S57DatasetProcessor.Validate()</c>
/// composes findings from this pack with findings from
/// <c>EncDotNet.S100.Datasets.S101.Validation.S101DatasetRules.Default</c>
/// run against the translated <c>S101Document</c>, rebadging the latter
/// with the prefix <c>"S101-as-S57/"</c> (design §9.3, Q-s57-rebadge).
/// </para>
/// <para>
/// Rule identifiers follow <c>S57-R-{clause}</c> for normative rules
/// and <c>S57-PROJ-{kind}</c> for projection-diagnostic surrogates
/// (design §5.3).
/// </para>
/// </remarks>
public static class S57PreTranslationRules
{
    /// <summary>
    /// <c>S57-R-1.1</c> — The dataset must carry a Data Set
    /// Identification (DSID) record AND a Data Set Parameters (DSPM)
    /// record whose compilation scale denominator (CSCL) is greater
    /// than zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec reference: IHO Publication S-57 Edition 3.1 Supplement,
    /// §3.5.1 (DSID — Data Set Identification field) and §3.5.2
    /// (DSPM — Data Set Parameter field, attribute <c>CSCL</c>).
    /// Without a DSID the dataset has no producer identity / edition
    /// metadata; without a positive CSCL the translator cannot
    /// populate the S-101 <c>compilationScale</c> equivalent.
    /// </para>
    /// <para>
    /// Both conditions are folded into a single rule because they
    /// fail together (a malformed leader typically loses both fields)
    /// and because both are produced by the same ISO 8211 leader
    /// pass — emitting one finding per missing field would duplicate
    /// noise without adding actionable detail.
    /// </para>
    /// </remarks>
    public static IValidationRule<S57Document> DatasetIdentificationPresent { get; } =
        ValidationRuleBuilder.RuleFor<S57Document>("S57-R-1.1")
            .WithDescription("Dataset must carry a DSID record and a DSPM record with CSCL > 0.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((doc, _) =>
            {
                var datasetId = doc.DataSetIdentification?.DataSetName;
                var findings = new List<ValidationFinding>();

                if (doc.DataSetIdentification is null)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S57-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message = "S-57 dataset is missing its DSID (Data Set Identification) record " +
                            "(S-57 §3.5.1).",
                        DatasetId = datasetId,
                    });
                }

                if (doc.DataSetParameters is null)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S57-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message = "S-57 dataset is missing its DSPM (Data Set Parameter) record " +
                            "(S-57 §3.5.2).",
                        DatasetId = datasetId,
                    });
                }
                else if (doc.DataSetParameters.CompilationScale <= 0)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S57-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"S-57 DSPM compilation scale denominator (CSCL) is " +
                            $"{doc.DataSetParameters.CompilationScale}; must be greater than 0 " +
                            "(S-57 §3.5.2).",
                        DatasetId = datasetId,
                    });
                }

                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S57-R-1.2</c> — The dataset should contain at least one
    /// <c>M_COVR</c> (Coverage) meta-feature delineating the data
    /// coverage extent.
    /// </summary>
    /// <remarks>
    /// Spec reference: IHO Publication S-57 Edition 3.1 Supplement,
    /// §4.6.1 (Meta object coverage). Without an <c>M_COVR</c> feature
    /// the translator cannot derive a reliable dataset bounding extent
    /// from the chart; downstream ECDIS rendering then falls back to
    /// the union of spatial primitives, which is typically larger
    /// than the producer-asserted coverage.
    /// </remarks>
    public static IValidationRule<S57Document> CoverageMetaFeaturePresent { get; } =
        ValidationRuleBuilder.RuleFor<S57Document>("S57-R-1.2")
            .WithDescription("Dataset should contain at least one M_COVR coverage meta-feature.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((doc, _) =>
            {
                var hasCoverage = doc.FeatureRecords
                    .Any(f => f.ObjectCode == S57ObjectCode.M_COVR);

                if (hasCoverage)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S57-R-1.2",
                        Severity = ValidationSeverity.Warning,
                        Message = "S-57 dataset contains no M_COVR (Coverage) meta-feature " +
                            "(S-57 §4.6.1). Coverage extent will be inferred from the union of " +
                            "spatial primitives, which is typically larger than the producer's " +
                            "asserted coverage.",
                        DatasetId = doc.DataSetIdentification?.DataSetName,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S57-PROJ-PARSE</c> — Placeholder for parser warnings emitted
    /// by <c>EncDotNet.S57.S57DocumentReader</c>. The rule body is
    /// intentionally empty in v1; this entry commits the rule-id
    /// namespace so a future change that surfaces non-fatal reader
    /// diagnostics can populate findings without breaking downstream
    /// consumers.
    /// </summary>
    /// <remarks>
    /// Spec reference: design note §5.2 (Stance A) and §5.3 — the
    /// reader does not yet surface non-fatal warnings; promoting
    /// Stance B is a separate PR. Mirrors the analogous
    /// <c>S101-PROJ-PARSE</c> placeholder shipped in V-4.
    /// </remarks>
    public static IValidationRule<S57Document> ParserDiagnosticPlaceholder { get; } =
        ValidationRuleBuilder.RuleFor<S57Document>("S57-PROJ-PARSE")
            .WithDescription("S-57 parser warnings (placeholder for future reader diagnostics surface).")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((_, _) => Array.Empty<ValidationFinding>())
            .Build();

    /// <summary>The canonical default pre-translation rule set for S-57 datasets.</summary>
    public static ValidationRuleSet<S57Document> Default { get; } = new(
        DatasetIdentificationPresent,
        CoverageMetaFeaturePresent,
        ParserDiagnosticPlaceholder);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S57Document document, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Default.Run(document, context);
    }
}
