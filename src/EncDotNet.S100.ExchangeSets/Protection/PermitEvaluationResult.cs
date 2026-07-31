namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// The result of evaluating a dataset against its Part 15 permit.
/// </summary>
public sealed class PermitEvaluationResult
{
    internal PermitEvaluationResult(
        PermitEvaluationOutcome outcome,
        DatasetDiscoveryMetadata dataset,
        DataPermit? permit,
        string? detail = null)
    {
        Outcome = outcome;
        Dataset = dataset;
        Permit = permit;
        Detail = detail;
    }

    /// <summary>The permit-policy outcome.</summary>
    public PermitEvaluationOutcome Outcome { get; }

    /// <summary>The catalogue dataset that was evaluated.</summary>
    public DatasetDiscoveryMetadata Dataset { get; }

    /// <summary>The matching permit, when one was found.</summary>
    public DataPermit? Permit { get; }

    /// <summary>Additional detail for a rejected dataset.</summary>
    public string? Detail { get; }

    /// <summary>Whether the permit authorizes the dataset.</summary>
    public bool IsAllowed => Outcome == PermitEvaluationOutcome.Allowed;
}
