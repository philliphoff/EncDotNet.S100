namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// The exception thrown when a protected dataset cannot be opened under its
/// Part 15 permit.
/// </summary>
public sealed class DatasetPermitException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception for a rejected permit evaluation.
    /// </summary>
    /// <param name="result">The permit evaluation that rejected the dataset.</param>
    public DatasetPermitException(PermitEvaluationResult result)
        : base(result?.Detail ?? "The dataset permit does not authorize this dataset.")
    {
        Evaluation = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>The permit evaluation that rejected the dataset.</summary>
    public PermitEvaluationResult Evaluation { get; }
}
