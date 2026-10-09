using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// Signs SECOM 2.0 request envelopes (issue #838) with this client's MCP
/// identity: the <c>POST …/v2/object/search/summary</c> (GetSummary) and
/// <c>POST …/v2/object/search</c> (Get) filters that services such as KHRA
/// and KRISO list and serve objects through.
/// </summary>
/// <remarks>
/// <para>
/// An envelope carries its filter fields, then the signer's certificate
/// (base64 DER), a SHA-384 thumbprint of its root and the signing time. The
/// signature is over the envelope's <em>canonical string</em>: its fields in
/// declared order joined with <c>.</c>, where a missing value is empty, a time
/// is its Unix epoch seconds, a list is joined with <c>,</c> and a container
/// type is its number. The signature is hex-encoded DER ECDSA, as GLA's
/// SECOMLib produces and checks it.
/// </para>
/// <para>
/// A search envelope names no algorithm, so a service checks it with the one
/// it is configured for. The default follows the key: SHA3-384 for P-384
/// (the MCP curve; what DMA's SECOMLib services use), SHA-256 otherwise.
/// </para>
/// </remarks>
public sealed class SecomEnvelopeSigner
{
    private readonly SecomClientIdentity _identity;
    private readonly string _rootThumbprint;

    /// <summary>Creates a signer for <paramref name="identity"/>.</summary>
    /// <param name="identity">The identity whose key signs.</param>
    /// <param name="anchors">The anchors its root is found among; by default <see cref="SecomTrustAnchors.BuiltIn"/>.</param>
    /// <param name="signatureReference">
    /// A SECOM <c>digitalSignatureReference</c> (<c>ecdsa-384-sha3</c>,
    /// <c>ecdsa-384-sha2</c>, <c>ecdsa-256-sha2-256</c>, <c>ecdsa-256-sha3-256</c>),
    /// or <see langword="null"/> to follow the key.
    /// </param>
    /// <exception cref="InvalidDataException">The identity has no ECDSA key, or the reference is not an ECDSA one.</exception>
    public SecomEnvelopeSigner(SecomClientIdentity identity, SecomTrustAnchors? anchors = null, string? signatureReference = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
        using (var key = identity.Certificate.GetECDsaPrivateKey()
            ?? throw new InvalidDataException("The identity has no ECDSA key, so it cannot sign SECOM requests."))
        {
            SignatureReference = signatureReference is { Length: > 0 } stated
                ? stated.Trim().ToLowerInvariant()
                : key.KeySize >= 384 ? "ecdsa-384-sha3" : "ecdsa-256-sha2-256";
        }

        if (SecomSignatureVerifier.Algorithm(SignatureReference, identity.Certificate).Family != "ecdsa")
            throw new InvalidDataException($"'{SignatureReference}' is not an ECDSA signature reference.");

        // The root the identity chains to (MCP Root for an MCC identity); a
        // certificate under no anchor names itself, as a self-signed one would.
        var root = (anchors ?? SecomTrustAnchors.BuiltIn).FindRoot(identity.Certificate) ?? identity.Certificate;
        _rootThumbprint = Convert.ToHexString(SHA384.HashData(root.RawData)).ToLowerInvariant();
    }

    /// <summary>The signature reference used, e.g. <c>ecdsa-384-sha3</c>.</summary>
    public string SignatureReference { get; }

    /// <summary>The identity that signs.</summary>
    public SecomClientIdentity Identity => _identity;

    /// <summary>
    /// The body of a SECOM 2.0 POST GetSummary for one page:
    /// <c>{ "envelope": {…}, "envelopeSignature": "…" }</c>.
    /// </summary>
    public JsonObject SummaryRequest(SecomQuery query, int page, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Sign(
        [
            ("containerType", null),
            ("dataProductType", null),
            ("productVersion", null),
            ("geometry", query.GeometryWkt is { Length: > 0 } geometry ? geometry : null),
            ("unlocode", null),
            ("validFrom", query.ValidFrom),
            ("validTo", query.ValidTo),
            ("page", page),
            ("pageSize", Math.Max(1, query.PageSize)),
        ], now);
    }

    /// <summary>The body of a SECOM 2.0 POST Get for one object.</summary>
    public JsonObject GetRequest(string dataReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataReference);
        return Sign(
        [
            ("dataReference", dataReference),
            ("containerType", null),
            ("dataProductType", null),
            ("productVersion", null),
            ("geometry", null),
            ("unlocode", null),
            ("validFrom", null),
            ("validTo", null),
            ("page", null),
            ("pageSize", null),
        ], now);
    }

    /// <summary>
    /// The canonical string of an envelope's values, in declared order: what
    /// the signature covers.
    /// </summary>
    public static string CanonicalString(IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join('.', values.Select(Canonical));
    }

    private JsonObject Sign(IReadOnlyList<(string Name, object? Value)> filter, DateTimeOffset now)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        string[] certificates = [Convert.ToBase64String(_identity.Certificate.RawData)];
        (string Name, object? Value)[] signature =
        [
            ("envelopeSignatureCertificate", certificates),
            ("envelopeRootCertificateThumbprint", _rootThumbprint),
            ("envelopeSignatureTime", at),
        ];
        var fields = filter.Concat(signature).ToArray();

        var envelope = new JsonObject();
        foreach (var (name, value) in fields)
        {
            if (Json(value) is { } node)
                envelope[name] = node;
        }

        var canonical = CanonicalString(fields.Select(f => f.Value));
        return new JsonObject
        {
            ["envelope"] = envelope,
            ["envelopeSignature"] = Convert.ToHexString(SignBytes(Encoding.UTF8.GetBytes(canonical))),
        };
    }

    private byte[] SignBytes(byte[] data)
    {
        var (_, hash) = SecomSignatureVerifier.Algorithm(SignatureReference, _identity.Certificate);
        var digest = SecomSignatureVerifier.Digest(hash, data, SignatureReference);
        using var key = _identity.Certificate.GetECDsaPrivateKey()!;
        return key.SignHash(digest, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static string Canonical(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset time => time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        SecomContainerType container => ((int)container).ToString(CultureInfo.InvariantCulture),
        string[] list => string.Join(',', list),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static JsonNode? Json(object? value) => value switch
    {
        null => null,
        DateTimeOffset time => JsonValue.Create(time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        SecomContainerType container => JsonValue.Create((int)container),
        string[] list => new JsonArray([.. list.Select(s => (JsonNode?)JsonValue.Create(s))]),
        int number => JsonValue.Create(number),
        string text => JsonValue.Create(text),
        _ => JsonValue.Create(value.ToString()),
    };
}
