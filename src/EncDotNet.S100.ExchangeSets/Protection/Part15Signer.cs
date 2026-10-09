using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Produces Part 15 digital signatures with a data server's (or other signing
/// authority's) ECDSA P-384 key.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Edition 5.2.1 Part 15 §15-8.4 and §15-8.7: signatures are ECDSA over
/// a SHA-384 hash using the NIST P-384 curve, encoded as an ASN.1 DER
/// <c>SEQUENCE { r, s }</c>. This is the producer-side counterpart of
/// <see cref="ExchangeSetVerifier"/> and <see cref="PermitSignatureVerifier"/>;
/// the signatures it produces verify with them.
/// </para>
/// <para>
/// The signer does not own <see cref="Certificate"/>; dispose the signer to
/// release the private key it extracted from the certificate.
/// </para>
/// </remarks>
public sealed class Part15Signer : IDisposable
{
    private const int RequiredKeySize = 384;

    private readonly ECDsa _key;

    /// <summary>
    /// Creates a signer from a certificate that carries an ECDSA P-384 private key.
    /// </summary>
    /// <param name="certificate">The signing certificate, with its private key.</param>
    /// <param name="certificateId">
    /// The identifier signatures use to reference the certificate
    /// (the <c>certificateRef</c> attribute), for example <c>urn:mrn:iho:s62:xx:key1</c>.
    /// </param>
    /// <param name="schemeAdministratorId">
    /// The identity of the Scheme Administrator that issued the certificate,
    /// written as the <c>schemeAdministrator</c> <c>id</c> (§15-8.11.1), for example <c>IHO</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The certificate has no ECDSA private key, or its key is not P-384.
    /// </exception>
    public Part15Signer(X509Certificate2 certificate, string certificateId, string schemeAdministratorId)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeAdministratorId);

        var key = certificate.GetECDsaPrivateKey()
            ?? throw new ArgumentException(
                "The signing certificate must carry an ECDSA private key.", nameof(certificate));
        if (key.KeySize != RequiredKeySize)
        {
            key.Dispose();
            throw new ArgumentException(
                "Part 15 signatures require an ECDSA P-384 key.", nameof(certificate));
        }

        _key = key;
        Certificate = certificate;
        CertificateId = certificateId;
        SchemeAdministratorId = schemeAdministratorId;
    }

    /// <summary>The signing certificate.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The identifier signatures use to reference <see cref="Certificate"/>.</summary>
    public string CertificateId { get; }

    /// <summary>The identity of the Scheme Administrator that issued <see cref="Certificate"/>.</summary>
    public string SchemeAdministratorId { get; }

    /// <summary>
    /// Creates the certificate block that authenticates this signer's
    /// signatures: the Scheme Administrator identity and the signing certificate.
    /// </summary>
    /// <returns>A new certificate block.</returns>
    /// <remarks>
    /// The Scheme Administrator certificate itself is not included; a client
    /// verifies against its own configured copy (§15-8.11.1).
    /// </remarks>
    public CertificateBlock CreateCertificateBlock() => new()
    {
        SchemeAdministratorId = SchemeAdministratorId,
        Certificates =
        [
            new CertificateEntry
            {
                Id = CertificateId,
                Issuer = SchemeAdministratorId,
                Value = Certificate.RawData,
            },
        ],
    };

    /// <summary>
    /// Signs a resource's bytes, producing an <c>S100_SE_SignatureOnData</c>.
    /// </summary>
    /// <param name="content">
    /// The resource bytes in the representation <paramref name="dataStatus"/> names.
    /// </param>
    /// <param name="signatureId">The unique identifier of the signature.</param>
    /// <param name="dataStatus">
    /// Which representation of the resource <paramref name="content"/> is (§15-8.11.6).
    /// Protected datasets are normally signed <see cref="SignatureDataStatus.Unencrypted"/>.
    /// </param>
    /// <returns>The signature.</returns>
    /// <remarks>This method is synchronous and CPU-bound.</remarks>
    public DigitalSignatureValue SignData(
        ReadOnlySpan<byte> content,
        string signatureId,
        SignatureDataStatus dataStatus = SignatureDataStatus.Unencrypted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureId);
        return OnData(SHA384.HashData(content), signatureId, dataStatus);
    }

    /// <summary>
    /// Signs a resource read from a stream, producing an <c>S100_SE_SignatureOnData</c>.
    /// </summary>
    /// <param name="content">
    /// A stream of the resource bytes in the representation <paramref name="dataStatus"/> names.
    /// It is read to its end and not disposed.
    /// </param>
    /// <param name="signatureId">The unique identifier of the signature.</param>
    /// <param name="dataStatus">Which representation of the resource <paramref name="content"/> is.</param>
    /// <param name="cancellationToken">A token to cancel reading the stream.</param>
    /// <returns>The signature.</returns>
    public async Task<DigitalSignatureValue> SignDataAsync(
        Stream content,
        string signatureId,
        SignatureDataStatus dataStatus = SignatureDataStatus.Unencrypted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureId);
        var hash = await SHA384.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return OnData(hash, signatureId, dataStatus);
    }

    /// <summary>
    /// Countersigns another signature, producing an <c>S100_SE_SignatureOnSignature</c>
    /// (§15-8.11.5), for example a distributor endorsing a producer's signature.
    /// </summary>
    /// <param name="signature">The signature to countersign.</param>
    /// <param name="signatureId">The unique identifier of the new signature.</param>
    /// <returns>The signature, referencing <paramref name="signature"/> by its identifier.</returns>
    /// <remarks>This method is synchronous and CPU-bound.</remarks>
    public DigitalSignatureValue SignSignature(DigitalSignatureValue signature, string signatureId)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureId);
        return new DigitalSignatureValue
        {
            Kind = DigitalSignatureKind.SignatureOnSignature,
            Id = signatureId,
            CertificateRef = CertificateId,
            SignatureRef = signature.Id,
            Value = SignHash(SHA384.HashData(signature.Value)),
        };
    }

    /// <summary>
    /// Signs a file's bytes as a standalone signature document, the form of
    /// <c>PERMIT.SIGN</c> and <c>CATALOG.SIGN</c> (§15-8.11.2).
    /// </summary>
    /// <param name="content">The exact bytes of the signed file.</param>
    /// <param name="fileName">The name of the signed file, for example <c>PERMIT.XML</c>.</param>
    /// <param name="signatureId">The unique identifier of the signature.</param>
    /// <returns>The standalone signature; write it with <see cref="StandaloneDigitalSignatureWriter"/>.</returns>
    /// <remarks>This method is synchronous and CPU-bound.</remarks>
    public StandaloneDigitalSignature SignStandalone(
        ReadOnlySpan<byte> content,
        string fileName,
        string signatureId = "signature")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureId);
        return new StandaloneDigitalSignature
        {
            FileName = fileName,
            Certificates = CreateCertificateBlock(),
            Signature = new DigitalSignatureValue
            {
                Id = signatureId,
                CertificateRef = CertificateId,
                Value = SignHash(SHA384.HashData(content)),
            },
        };
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();

    private DigitalSignatureValue OnData(byte[] hash, string signatureId, SignatureDataStatus dataStatus) => new()
    {
        Kind = DigitalSignatureKind.SignatureOnData,
        Id = signatureId,
        CertificateRef = CertificateId,
        DataStatus = dataStatus,
        Value = SignHash(hash),
    };

    private byte[] SignHash(byte[] hash) =>
        _key.SignHash(hash, DSASignatureFormat.Rfc3279DerSequence);
}
