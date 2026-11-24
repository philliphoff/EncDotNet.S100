using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The outcome of checking one signature: what happened, why, and the trusted
/// root its signer chained to when the outcome is <see cref="VerificationOutcome.Ok"/>.
/// </summary>
internal readonly record struct SignatureEvaluation(
    VerificationOutcome Outcome,
    string? Detail,
    X509Certificate2? TrustedRoot = null)
{
    public static implicit operator SignatureEvaluation((VerificationOutcome Outcome, string? Detail) value) =>
        new(value.Outcome, value.Detail);

    public void Deconstruct(out VerificationOutcome outcome, out string? detail)
    {
        outcome = Outcome;
        detail = Detail;
    }
}
