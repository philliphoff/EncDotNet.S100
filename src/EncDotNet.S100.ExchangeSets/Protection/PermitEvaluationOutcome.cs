namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Describes whether a dataset permit authorizes a catalogue dataset.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15 §15-7.4.4.</remarks>
public enum PermitEvaluationOutcome
{
    /// <summary>The permit authorizes the dataset.</summary>
    Allowed,

    /// <summary>The requested file is not declared as a protected dataset.</summary>
    NotProtected,

    /// <summary>No dataset permit matches the requested file.</summary>
    PermitNotFound,

    /// <summary>The base dataset needed to evaluate an update is absent.</summary>
    BaseDatasetMissing,

    /// <summary>The permit requires an edition number that the catalogue omits.</summary>
    EditionNumberMissing,

    /// <summary>The permit and catalogue declare different edition numbers.</summary>
    EditionMismatch,

    /// <summary>The permit requires an issue date that the catalogue omits.</summary>
    IssueDateMissing,

    /// <summary>The permit and catalogue declare different issue dates.</summary>
    IssueDateMismatch,

    /// <summary>The protected dataset was issued after the permit expired.</summary>
    IssuedAfterExpiry,
}
