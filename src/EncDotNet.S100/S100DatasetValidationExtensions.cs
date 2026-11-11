using EncDotNet.S100.Validation;

namespace EncDotNet.S100;

/// <summary>
/// Validation for <see cref="S100Dataset"/>: running a product's bundled rule
/// pack against a dataset.
/// </summary>
public static class S100DatasetValidationExtensions
{
    /// <summary>
    /// Runs the bundled validation rule pack for the dataset's product
    /// specification (for example <c>S124NavigationalWarningRules.Default</c>
    /// for S-124) against the parsed dataset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Validation is a pure function of the parsed dataset, and the report is
    /// cached: calling this again on the same dataset returns the same report
    /// without re-running the rules.
    /// </para>
    /// <para>
    /// To run your own rules, build them with
    /// <see cref="ValidationRuleBuilder"/> and run a
    /// <see cref="ValidationRuleSet{TModel}"/> against the product's typed model
    /// or dataset type instead.
    /// </para>
    /// </remarks>
    /// <param name="dataset">The dataset to validate.</param>
    /// <returns>
    /// The findings, or <see langword="null"/> when the product has no rule pack.
    /// A dataset that passes every rule returns a report with no findings
    /// (<see cref="ValidationReport.IsValid"/> is <see langword="true"/>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="dataset"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="dataset"/> has been disposed.</exception>
    public static ValidationReport? Validate(this S100Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return dataset.Processor.Validate();
    }
}
