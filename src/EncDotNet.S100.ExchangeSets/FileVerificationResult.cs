namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The verification result for a single file in an exchange set.
/// </summary>
public sealed class FileVerificationResult
{
    /// <summary>The file name as declared in the catalogue discovery metadata.</summary>
    public required string FileName { get; init; }

    /// <summary>The verification outcome for this file.</summary>
    public required VerificationOutcome Outcome { get; init; }

    /// <summary>Optional detail message (e.g. exception message on <see cref="VerificationOutcome.Error"/>).</summary>
    public string? Detail { get; init; }
}
