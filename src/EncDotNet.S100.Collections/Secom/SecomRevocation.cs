using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>What a revocation check found (issue #833).</summary>
public enum SecomRevocationStatus
{
    /// <summary>
    /// Revocation was not checked: no checker was given, or no current CRL
    /// could be had for a certificate in the chain (soft-fail).
    /// </summary>
    NotChecked = 0,

    /// <summary>A current CRL from each issuer in the chain was read, and none lists its certificate.</summary>
    NotRevoked,

    /// <summary>A certificate in the chain is listed on its issuer's CRL.</summary>
    Revoked,
}

/// <summary>The result of <see cref="SecomRevocation.Check"/>.</summary>
/// <param name="Status">What was found.</param>
/// <param name="Detail">Which certificate was revoked, or why the check could not be made.</param>
public sealed record SecomRevocationResult(SecomRevocationStatus Status, string? Detail = null)
{
    /// <summary>When the CRL says the certificate was revoked, for <see cref="SecomRevocationStatus.Revoked"/>.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}

/// <summary>
/// Checks SECOM certificate chains against the certificate revocation lists
/// (CRLs) their certificates name (issue #833). It is used for chains that reach
/// a <see cref="SecomTrustAnchors"/> root: data signers (#823) and TLS servers
/// (#829).
/// </summary>
/// <remarks>
/// <para>
/// CRLs are read here rather than by the platform: platform revocation differs
/// by OS, and the MCP CAs serve PEM CRLs, which RFC 5280 does not allow and some
/// platforms reject. One CRL covers every certificate its CA issued, so
/// indexing many objects costs one fetch per CA. OCSP is not used.
/// </para>
/// <para>
/// A CRL is used only when its issuer name matches, its signature verifies
/// against the issuer's key, and it is current (<c>thisUpdate</c> passed and
/// <c>nextUpdate</c> not). CRLs are kept in memory and, given a cache
/// directory, on disk. A copy is fetched again after <see cref="RefreshAfter"/>
/// or once it is no longer current. Each fetch is bounded by
/// <see cref="FetchTimeout"/>. After a fetch fails, that URL is not tried again
/// for <see cref="RetryAfterFailure"/>, so an unreachable responder costs at
/// most one timeout per interval.
/// </para>
/// <para>
/// The check soft-fails. When no current CRL can be had, the result is
/// <see cref="SecomRevocationStatus.NotChecked"/>, and callers show it and
/// carry on. A listed certificate is always <see cref="SecomRevocationStatus.Revoked"/>.
/// </para>
/// </remarks>
public sealed class SecomRevocation
{
    /// <summary>The largest CRL read; MCP's are about 10 KB.</summary>
    private const int MaxCrlBytes = 8 * 1024 * 1024;

    private readonly Shared _shared;
    private readonly bool _fetch;

    /// <summary>Creates a checker.</summary>
    /// <param name="httpClient">
    /// The client CRLs are fetched with. Distribution points are plain HTTP, so
    /// this needs no SECOM server trust.
    /// </param>
    /// <param name="cacheDirectory">Where fetched CRLs are kept between runs; <see langword="null"/> for memory only.</param>
    /// <param name="timeProvider">The clock CRL currency is judged by.</param>
    public SecomRevocation(HttpClient httpClient, string? cacheDirectory = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _shared = new Shared(httpClient, cacheDirectory, timeProvider ?? TimeProvider.System);
        _fetch = true;
    }

    private SecomRevocation(Shared shared)
    {
        _shared = shared;
        _fetch = false;
    }

    /// <summary>
    /// The same checker, limited to CRLs already held in memory or on disk: it
    /// never waits on the network. Use it on read paths (recorded downloads),
    /// which may run on a UI thread.
    /// </summary>
    public SecomRevocation CacheOnly => _shared.CacheOnly ??= new SecomRevocation(_shared);

    /// <summary>How long one CRL fetch may take before it is abandoned. The default is 5 seconds.</summary>
    public TimeSpan FetchTimeout
    {
        get => _shared.FetchTimeout;
        init => _shared.FetchTimeout = value;
    }

    /// <summary>
    /// How long a fetched CRL is used before it is fetched again, even when it
    /// is still current. The default is 24 hours. MCP re-signs its CRL on each
    /// request with a <c>nextUpdate</c> a week ahead, so a week-old copy would
    /// miss that week's revocations.
    /// </summary>
    public TimeSpan RefreshAfter
    {
        get => _shared.RefreshAfter;
        init => _shared.RefreshAfter = value;
    }

    /// <summary>How long a URL whose fetch failed is left before it is tried again. The default is 5 minutes.</summary>
    public TimeSpan RetryAfterFailure
    {
        get => _shared.RetryAfterFailure;
        init => _shared.RetryAfterFailure = value;
    }

    /// <summary>
    /// Checks every certificate in <paramref name="chain"/> but the root
    /// against the CRL its issuer publishes.
    /// </summary>
    /// <param name="chain">The chain, leaf first and root last, as built against the trust anchors.</param>
    /// <returns>
    /// <see cref="SecomRevocationStatus.Revoked"/> when any certificate is
    /// listed; <see cref="SecomRevocationStatus.NotChecked"/> when one could not
    /// be checked; otherwise <see cref="SecomRevocationStatus.NotRevoked"/>.
    /// </returns>
    public SecomRevocationResult Check(IReadOnlyList<X509Certificate2> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count < 2)
            return new SecomRevocationResult(SecomRevocationStatus.NotChecked, "The certificate has no issuer to ask.");

        SecomRevocationResult? notChecked = null;
        for (var i = 0; i < chain.Count - 1; i++)
        {
            var result = CheckOne(chain[i], chain[i + 1]);
            if (result.Status == SecomRevocationStatus.Revoked)
                return result;
            if (result.Status == SecomRevocationStatus.NotChecked)
                notChecked ??= result;
        }

        return notChecked ?? new SecomRevocationResult(SecomRevocationStatus.NotRevoked);
    }

    private SecomRevocationResult CheckOne(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        var name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var urls = DistributionPoints(certificate);
        if (urls.Count == 0)
            return new SecomRevocationResult(SecomRevocationStatus.NotChecked, $"The certificate of {name} names no CRL.");

        string? error = null;
        foreach (var url in urls)
        {
            if (_shared.Current(url, issuer, _fetch, out error) is not { } crl)
                continue;
            return crl.Revoked.TryGetValue(Serial(certificate.GetSerialNumber(), littleEndian: true), out var at)
                ? new SecomRevocationResult(SecomRevocationStatus.Revoked, $"The certificate of {name} was revoked on {at:yyyy-MM-dd}.") { RevokedAt = at }
                : new SecomRevocationResult(SecomRevocationStatus.NotRevoked);
        }

        return new SecomRevocationResult(SecomRevocationStatus.NotChecked, $"No current CRL could be read for {name}: {error ?? "none is cached"}");
    }

    /// <summary>The HTTP(S) CRL distribution points <paramref name="certificate"/> names.</summary>
    internal static IReadOnlyList<Uri> DistributionPoints(X509Certificate2 certificate)
    {
        const string crlDistributionPoints = "2.5.29.31";
        var extension = certificate.Extensions[crlDistributionPoints];
        if (extension is null)
            return [];

        var urls = new List<Uri>();
        try
        {
            // CRLDistributionPoints ::= SEQUENCE OF DistributionPoint; distributionPoint [0] { fullName [0] GeneralNames }.
            var points = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (points.HasData)
            {
                var point = points.ReadSequence();
                if (!point.HasData || !point.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    continue;
                var name = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
                if (!name.HasData || !name.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    continue;
                var fullName = name.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
                while (fullName.HasData)
                {
                    var tag = fullName.PeekTag();
                    if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 6)))
                    {
                        var text = fullName.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 6));
                        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                            urls.Add(uri);
                    }
                    else
                    {
                        fullName.ReadEncodedValue();
                    }
                }
            }
        }
        catch (AsnContentException)
        {
        }

        return urls;
    }

    /// <summary>A serial number as upper-case hex without leading zero bytes.</summary>
    private static string Serial(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        var bigEndian = bytes.ToArray();
        if (littleEndian)
            Array.Reverse(bigEndian);
        var start = 0;
        while (start < bigEndian.Length - 1 && bigEndian[start] == 0)
            start++;
        return Convert.ToHexString(bigEndian, start, bigEndian.Length - start);
    }

    /// <summary>A parsed CRL.</summary>
    /// <param name="Issuer">The issuer name, DER.</param>
    /// <param name="ThisUpdate">When it was issued.</param>
    /// <param name="NextUpdate">When the next one is due, if stated.</param>
    /// <param name="Revoked">Revoked serial numbers (hex) and when each was revoked.</param>
    /// <param name="Signed">The DER of the signed part.</param>
    /// <param name="SignatureAlgorithm">The signature algorithm's OID.</param>
    /// <param name="Signature">The signature.</param>
    internal sealed record Crl(
        byte[] Issuer,
        DateTimeOffset ThisUpdate,
        DateTimeOffset? NextUpdate,
        IReadOnlyDictionary<string, DateTimeOffset> Revoked,
        byte[] Signed,
        string SignatureAlgorithm,
        byte[] Signature)
    {
        /// <summary>Reads a CRL sent as DER or as PEM (as MCP serves it).</summary>
        /// <exception cref="InvalidDataException">The bytes are not a CRL.</exception>
        public static Crl Parse(byte[] bytes)
        {
            var der = bytes;
            if (bytes.Length > 0 && bytes[0] == (byte)'-')
            {
                var text = Encoding.ASCII.GetString(bytes);
                if (!PemEncoding.TryFind(text, out var fields) || text[fields.Label] is not "X509 CRL")
                    throw new InvalidDataException("The response is PEM but holds no CRL.");
                der = Convert.FromBase64String(text[fields.Base64Data]);
            }

            try
            {
                var list = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
                var signed = list.ReadEncodedValue().ToArray();
                var algorithm = list.ReadSequence().ReadObjectIdentifier();
                var signature = list.ReadBitString(out _);

                var tbs = new AsnReader(signed, AsnEncodingRules.DER).ReadSequence();
                if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
                    tbs.ReadInteger();
                tbs.ReadSequence();
                var issuer = tbs.ReadEncodedValue().ToArray();
                var thisUpdate = ReadTime(tbs);
                DateTimeOffset? nextUpdate = tbs.HasData && IsTime(tbs.PeekTag()) ? ReadTime(tbs) : null;

                var revoked = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                {
                    var entries = tbs.ReadSequence();
                    while (entries.HasData)
                    {
                        var entry = entries.ReadSequence();
                        var serial = Serial(entry.ReadIntegerBytes().Span, littleEndian: false);
                        revoked[serial] = ReadTime(entry);
                    }
                }

                return new Crl(issuer, thisUpdate, nextUpdate, revoked, signed, algorithm, signature);
            }
            catch (AsnContentException ex)
            {
                throw new InvalidDataException("The response is not a CRL.", ex);
            }
        }

        /// <summary>Whether <paramref name="issuer"/> issued and signed this CRL.</summary>
        public bool IsFrom(X509Certificate2 issuer)
        {
            if (!Issuer.AsSpan().SequenceEqual(issuer.SubjectName.RawData))
                return false;
            try
            {
                switch (SignatureAlgorithm)
                {
                    case "1.2.840.10045.4.3.2" or "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4":
                        {
                            using var ecdsa = issuer.GetECDsaPublicKey();
                            return ecdsa is not null && ecdsa.VerifyData(Signed, Signature, Hash(SignatureAlgorithm), DSASignatureFormat.Rfc3279DerSequence);
                        }

                    case "1.2.840.113549.1.1.11" or "1.2.840.113549.1.1.12" or "1.2.840.113549.1.1.13":
                        {
                            using var rsa = issuer.GetRSAPublicKey();
                            return rsa is not null && rsa.VerifyData(Signed, Signature, Hash(SignatureAlgorithm), RSASignaturePadding.Pkcs1);
                        }

                    default:
                        return false;
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>Whether this CRL may be relied on at <paramref name="now"/>.</summary>
        public bool IsCurrent(DateTimeOffset now) =>
            ThisUpdate <= now + ClockSkew && (NextUpdate is { } next ? now <= next : now - ThisUpdate <= TimeSpan.FromDays(7));

        private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

        private static HashAlgorithmName Hash(string oid) => oid switch
        {
            "1.2.840.10045.4.3.2" or "1.2.840.113549.1.1.11" => HashAlgorithmName.SHA256,
            "1.2.840.10045.4.3.3" or "1.2.840.113549.1.1.12" => HashAlgorithmName.SHA384,
            _ => HashAlgorithmName.SHA512,
        };

        private static bool IsTime(Asn1Tag tag) =>
            tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.UtcTime))
            || tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.GeneralizedTime));

        private static DateTimeOffset ReadTime(AsnReader reader) =>
            reader.PeekTag().HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.UtcTime))
                ? reader.ReadUtcTime()
                : reader.ReadGeneralizedTime();
    }

    /// <summary>State shared by a checker and its <see cref="CacheOnly"/> view.</summary>
    private sealed class Shared(HttpClient httpClient, string? cacheDirectory, TimeProvider time)
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public SecomRevocation? CacheOnly { get; set; }

        public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan RefreshAfter { get; set; } = TimeSpan.FromHours(24);

        public TimeSpan RetryAfterFailure { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// A current CRL from <paramref name="url"/> issued by <paramref name="issuer"/>,
        /// fetching one when allowed and needed; <see langword="null"/> when there is none.
        /// </summary>
        public Crl? Current(Uri url, X509Certificate2 issuer, bool fetch, out string? error)
        {
            var entry = _entries.GetOrAdd(url.AbsoluteUri, _ => new Entry());

            // One fetch per URL at a time; others wait for it rather than fetching too.
            lock (entry)
            {
                var now = time.GetUtcNow();
                if (entry.Crl is null && !entry.LoadedFromDisk)
                {
                    entry.LoadedFromDisk = true;
                    LoadFromDisk(url, entry);
                }

                var usable = entry.Crl is { } held && held.IsCurrent(now) && entry.IsFrom(issuer);
                var due = !usable || now - entry.FetchedAt >= RefreshAfter;
                var resting = entry.FailedAt is { } failed && now - failed < RetryAfterFailure;
                if (due && fetch && !resting)
                {
                    try
                    {
                        var bytes = Fetch(url);
                        var crl = Crl.Parse(bytes);
                        if (!crl.IsFrom(issuer))
                            throw new InvalidDataException("The CRL is not signed by the certificate's issuer.");
                        if (!crl.IsCurrent(now))
                            throw new InvalidDataException("The CRL is out of date.");
                        entry.Replace(crl, issuer);
                        entry.FetchedAt = now;
                        entry.FailedAt = null;
                        entry.Error = null;
                        SaveToDisk(url, bytes, now);
                        usable = true;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException
                        or IOException or FormatException or NotSupportedException or CryptographicException)
                    {
                        entry.FailedAt = now;
                        entry.Error = ex is OperationCanceledException ? $"The CRL took longer than {FetchTimeout.TotalSeconds:0} s." : ex.Message;
                    }
                }

                error = entry.Error;
                return usable ? entry.Crl : null;
            }
        }

        private byte[] Fetch(Uri url)
        {
            using var timeout = new CancellationTokenSource(FetchTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxCrlBytes)
                throw new InvalidDataException("The CRL is too large.");

            using var stream = response.Content.ReadAsStream(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (buffer.Length + read > MaxCrlBytes)
                    throw new InvalidDataException("The CRL is too large.");
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        private void LoadFromDisk(Uri url, Entry entry)
        {
            if (CachePath(url) is not { } path)
                return;
            try
            {
                if (!File.Exists(path))
                    return;
                entry.Replace(Crl.Parse(File.ReadAllBytes(path)), verifiedBy: null);
                entry.FetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
            {
                entry.Replace(null, verifiedBy: null);
            }
        }

        private void SaveToDisk(Uri url, byte[] bytes, DateTimeOffset fetchedAt)
        {
            if (CachePath(url) is not { } path)
                return;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(temporary, bytes);
                File.SetLastWriteTimeUtc(temporary, fetchedAt.UtcDateTime);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The cache is an optimisation; the CRL is still held in memory.
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        private string? CachePath(Uri url) => cacheDirectory is null
            ? null
            : Path.Combine(cacheDirectory, "crl-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)))[..16].ToLowerInvariant() + ".crl");

        private sealed class Entry
        {
            /// <summary>Issuers (by SHA-256 of the certificate) whose signature on <see cref="Crl"/> has been verified.</summary>
            private readonly HashSet<string> _verifiedBy = new(StringComparer.Ordinal);

            public Crl? Crl { get; private set; }

            public void Replace(Crl? crl, X509Certificate2? verifiedBy)
            {
                Crl = crl;
                _verifiedBy.Clear();
                if (crl is not null && verifiedBy is not null)
                    _verifiedBy.Add(Key(verifiedBy));
            }

            /// <summary>Whether <paramref name="issuer"/> signed <see cref="Crl"/>; verified once per issuer.</summary>
            public bool IsFrom(X509Certificate2 issuer)
            {
                var key = Key(issuer);
                if (_verifiedBy.Contains(key))
                    return true;
                if (Crl is null || !Crl.IsFrom(issuer))
                    return false;
                _verifiedBy.Add(key);
                return true;
            }

            private static string Key(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

            public DateTimeOffset FetchedAt { get; set; }

            public bool LoadedFromDisk { get; set; }

            public DateTimeOffset? FailedAt { get; set; }

            public string? Error { get; set; }
        }
    }
}
