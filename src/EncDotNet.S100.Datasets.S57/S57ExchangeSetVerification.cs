using EncDotNet.S100.ExchangeSets;
using EncDotNet.S57.ExchangeSets;
using S57Outcome = EncDotNet.S57.ExchangeSets.S57VerificationOutcome;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Adapts the upstream <c>EncDotNet.S57</c> exchange-set verifier (S-57 / S-63
/// <c>CATALOG.031</c> CRC + digital-signature checks) onto this repository's
/// S-100 <see cref="ExchangeSetVerificationResult"/> model, so S-57 exchange
/// sets are integrity-verified and surfaced exactly like S-100 ones.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately thin <em>adapter</em> rather than a shared interface:
/// the S-57 verifier is rooted at a directory path (<c>CATALOG.031</c> + the
/// files it references), whereas the S-100 verifier is
/// <see cref="EncDotNet.S100.Core.IAssetSource"/>-based. Forcing both behind one
/// interface would distort both, so the two verifiers stay independent and this
/// type only maps the <em>result</em>.
/// </para>
/// <para>
/// The mapping is safe because the two outcome enumerations are kept
/// byte-identical (same names, same ordinals); the mapping is nonetheless done
/// by name so a future divergence fails loudly rather than silently
/// mis-mapping. S-57 integrity uses a CRC-32 (<c>CATALOG.031</c> field), not the
/// SHA-256 digest the S-100 model carries, so the per-file CRC values are
/// surfaced through <see cref="FileVerificationResult.Detail"/> and
/// <see cref="FileVerificationResult.ComputedSha256"/> is left
/// <see langword="null"/>.
/// </para>
/// </remarks>
public static class S57ExchangeSetVerification
{
    /// <summary>
    /// Verifies the S-57 exchange set rooted at <paramref name="rootPath"/>
    /// (the directory containing its <c>CATALOG.031</c>) and returns the result
    /// mapped onto the S-100 <see cref="ExchangeSetVerificationResult"/> model.
    /// </summary>
    /// <param name="rootPath">
    /// The exchange-set root directory (the folder that contains
    /// <c>CATALOG.031</c>).
    /// </param>
    /// <param name="allowUntrustedCertificates">
    /// When <see langword="true"/> (the default), signature checks do not fail
    /// solely because the signing certificate chains to an untrusted root —
    /// mirrors the S-100 verifier's
    /// <see cref="TrustAnchorOptions.AllowUntrustedCertificates"/>.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The verification result in the S-100 result model.</returns>
    /// <exception cref="ArgumentException"><paramref name="rootPath"/> is null or empty.</exception>
    public static async Task<ExchangeSetVerificationResult> VerifyAsync(
        string rootPath,
        bool allowUntrustedCertificates = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);

        var s57Result = await S57ExchangeSetReader.Read(rootPath)
            .VerifyAsync(
                rootPath,
                new S63TrustAnchorOptions { AllowUntrustedCertificates = allowUntrustedCertificates },
                logger: null,
                cancellationToken)
            .ConfigureAwait(false);

        return Map(s57Result);
    }

    /// <summary>
    /// Maps an <see cref="S57ExchangeSetVerificationResult"/> onto the S-100
    /// <see cref="ExchangeSetVerificationResult"/> model. Exposed (rather than
    /// private) so it can be unit-tested directly against synthetic S-57
    /// results.
    /// </summary>
    /// <param name="result">The S-57 verification result to map.</param>
    /// <returns>The equivalent S-100 result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public static ExchangeSetVerificationResult Map(S57ExchangeSetVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var files = new List<FileVerificationResult>(result.FileResults.Length);
        foreach (var file in result.FileResults)
            files.Add(MapFile(file));

        return new ExchangeSetVerificationResult { FileResults = files };
    }

    /// <summary>
    /// Maps a single <see cref="S57FileVerificationResult"/> onto the S-100
    /// <see cref="FileVerificationResult"/>, preserving both the signature and
    /// checksum dimensions independently.
    /// </summary>
    /// <param name="file">The S-57 per-file result.</param>
    /// <returns>The equivalent S-100 per-file result.</returns>
    public static FileVerificationResult MapFile(S57FileVerificationResult file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new FileVerificationResult
        {
            FileName = file.FileName,
            Outcome = MapOutcome(file.SignatureOutcome),
            SignatureResults = [],
            ChecksumOutcome = MapOutcome(file.ChecksumOutcome),
            Detail = ComposeDetail(file),
        };
    }

    /// <summary>
    /// Maps an <see cref="S57Outcome"/> onto the S-100
    /// <see cref="VerificationOutcome"/> by name. The two enumerations are kept
    /// byte-identical so the cross-repo bridge can drive both uniformly.
    /// </summary>
    /// <param name="outcome">The S-57 outcome.</param>
    /// <returns>The equivalent S-100 outcome.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The S-57 outcome has no S-100 counterpart (indicates the two
    /// enumerations have diverged).
    /// </exception>
    public static VerificationOutcome MapOutcome(S57Outcome outcome) => outcome switch
    {
        S57Outcome.Ok => VerificationOutcome.Ok,
        S57Outcome.NotSigned => VerificationOutcome.NotSigned,
        S57Outcome.SignatureInvalid => VerificationOutcome.SignatureInvalid,
        S57Outcome.CertificateUntrusted => VerificationOutcome.CertificateUntrusted,
        S57Outcome.CertificateExpired => VerificationOutcome.CertificateExpired,
        S57Outcome.CertificateNotFound => VerificationOutcome.CertificateNotFound,
        S57Outcome.FileMissing => VerificationOutcome.FileMissing,
        S57Outcome.Error => VerificationOutcome.Error,
        S57Outcome.NoChecksum => VerificationOutcome.NoChecksum,
        S57Outcome.ChecksumMismatch => VerificationOutcome.ChecksumMismatch,
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome), outcome,
            "S-57 verification outcome has no S-100 counterpart; the outcome enumerations have diverged."),
    };

    /// <summary>
    /// Builds a human-readable detail string carrying the S-57 CRC values (which
    /// the S-100 SHA-256 model has no field for) plus any upstream detail.
    /// </summary>
    private static string? ComposeDetail(S57FileVerificationResult file)
    {
        string? crc = (file.ExpectedCrc, file.ActualCrc) switch
        {
            (null or "", null or "") => null,
            var (expected, actual) => $"CRC expected={Display(expected)} actual={Display(actual)}",
        };

        if (string.IsNullOrEmpty(file.Detail))
            return crc;
        return crc is null ? file.Detail : $"{file.Detail}; {crc}";

        static string Display(string? value) => string.IsNullOrEmpty(value) ? "—" : value;
    }
}
