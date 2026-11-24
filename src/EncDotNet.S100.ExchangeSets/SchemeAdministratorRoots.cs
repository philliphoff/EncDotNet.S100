using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Loads Scheme Administrator (SA) root certificates for
/// <see cref="TrustAnchorOptions.TrustedRoots"/>.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. The SA public key is the root of trust and has
/// to reach the verifier out of band, never from the exchange set itself. A
/// file holds either one DER certificate or one or more PEM certificates.
/// Check each root's <see cref="Sha256Fingerprint"/> against the one the SA
/// publishes before trusting it.
/// </remarks>
public static class SchemeAdministratorRoots
{
    private const string PemHeader = "-----BEGIN CERTIFICATE-----";

    /// <summary>
    /// SHA-256 fingerprint of <see cref="LoadIhoRoot"/>: the IHO S-100
    /// Edition 5.2 Scheme Administrator root, valid 2025 to 2075, from the
    /// IHO's Data Protection (S-100/S-57) page.
    /// </summary>
    public const string IhoRootFingerprint =
        "24:51:A0:B2:72:27:EF:24:BB:61:F2:DE:44:C7:82:15:01:F9:14:10:42:E2:A1:16:D2:2A:DE:6E:3B:31:A9:45";

    /// <summary>
    /// SHA-256 fingerprint of <see cref="LoadIhoTestingRoot"/>: the IHO S-100
    /// Edition 4.0 Scheme Administrator certificate, valid 2019 to 2051, which
    /// the IHO labels its testing and implementation version.
    /// </summary>
    public const string IhoTestingRootFingerprint =
        "A7:10:95:7D:E9:C4:5F:AC:64:E9:D0:8E:F1:1B:B9:46:8A:8C:D9:2E:7A:6D:2C:7B:36:54:BE:C4:6D:29:6B:4B";

    /// <summary>
    /// Loads the IHO S-100 Edition 5.2 Scheme Administrator root (ECDSA
    /// P-384), the root of trust for S-100 data protected under Edition 5.2
    /// and later. Each call returns a new certificate the caller disposes.
    /// </summary>
    public static X509Certificate2 LoadIhoRoot() =>
        LoadBuiltIn("iho-s100-ed5.2-root.pem", IhoRootFingerprint);

    /// <summary>
    /// Loads the IHO S-100 Edition 4.0 Scheme Administrator certificate
    /// (DSA), which the IHO publishes for testing and implementation. Don't
    /// trust it for data used to navigate. Each call returns a new
    /// certificate the caller disposes.
    /// </summary>
    public static X509Certificate2 LoadIhoTestingRoot() =>
        LoadBuiltIn("iho-s100-ed4.0-sa.pem", IhoTestingRootFingerprint);

    private static X509Certificate2 LoadBuiltIn(string name, string fingerprint)
    {
        using var stream = typeof(SchemeAdministratorRoots).Assembly
            .GetManifestResourceStream("EncDotNet.S100.ExchangeSets.SchemeAdministrators." + name)
            ?? throw new InvalidOperationException($"The built-in certificate '{name}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var certificate = Single(Load(buffer.ToArray(), name));
        if (Sha256Fingerprint(certificate) != fingerprint)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"The built-in certificate '{name}' doesn't match its pinned fingerprint.");
        }

        return certificate;

        static X509Certificate2 Single(IReadOnlyList<X509Certificate2> certificates) =>
            certificates.Count == 1
                ? certificates[0]
                : throw new InvalidOperationException("A built-in root file must hold exactly one certificate.");
    }

    /// <summary>Loads the root certificates in the file at <paramref name="path"/>.</summary>
    /// <exception cref="SchemeAdministratorRootException">
    /// The file can't be read, holds no certificate, or holds a certificate
    /// that can't be a root.
    /// </exception>
    public static IReadOnlyList<X509Certificate2> LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SchemeAdministratorRootException(
                SchemeAdministratorRootError.Unreadable, $"Can't read '{path}': {ex.Message}", ex);
        }

        return Load(data, Path.GetFileName(path));
    }

    /// <summary>Loads the root certificates in <paramref name="data"/> (DER or PEM).</summary>
    /// <param name="data">The certificate bytes.</param>
    /// <param name="sourceName">A name for the source, used in error messages.</param>
    /// <exception cref="SchemeAdministratorRootException">
    /// The data holds no certificate, or a certificate that can't be a root.
    /// </exception>
    public static IReadOnlyList<X509Certificate2> Load(ReadOnlySpan<byte> data, string sourceName = "certificate")
    {
        var certificates = new List<X509Certificate2>();
        try
        {
            var text = LooksLikeText(data) ? Encoding.ASCII.GetString(data) : null;
            if (text is not null && text.Contains(PemHeader, StringComparison.Ordinal))
            {
                var collection = new X509Certificate2Collection();
                collection.ImportFromPem(text);
                certificates.AddRange(collection);
            }
            else
            {
                certificates.Add(X509CertificateLoader.LoadCertificate(data));
            }
        }
        catch (CryptographicException ex)
        {
            DisposeAll(certificates);
            throw new SchemeAdministratorRootException(
                SchemeAdministratorRootError.NotCertificate, $"'{sourceName}' isn't a certificate.", ex);
        }

        if (certificates.Count == 0)
            throw new SchemeAdministratorRootException(
                SchemeAdministratorRootError.NoCertificate, $"'{sourceName}' holds no certificate.");

        foreach (var certificate in certificates)
        {
            if (!CanBeRoot(certificate))
            {
                var subject = certificate.Subject;
                var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                DisposeAll(certificates);
                throw new SchemeAdministratorRootException(
                    SchemeAdministratorRootError.NotCertificateAuthority,
                    $"'{subject}' in '{sourceName}' isn't a certificate authority, so it can't be a Scheme Administrator root.")
                {
                    CertificateSubject = subject,
                    CertificateName = commonName,
                };
            }
        }

        return certificates;
    }

    /// <summary>
    /// The SHA-256 fingerprint of <paramref name="certificate"/> as colon-separated
    /// upper-case hex pairs, the form SAs and tools usually print.
    /// </summary>
    public static string Sha256Fingerprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Convert.ToHexString(SHA256.HashData(certificate.RawData))
            .Chunk(2)
            .Select(pair => new string(pair))
            .Aggregate((a, b) => a + ":" + b);
    }

    /// <summary>
    /// Whether <paramref name="certificate"/> can anchor a chain: a certificate
    /// that says it isn't a CA can't. One with no basic constraints (X.509 v1)
    /// is accepted.
    /// </summary>
    private static bool CanBeRoot(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault()
            is not { CertificateAuthority: false };

    private static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        foreach (var b in data[..Math.Min(data.Length, 512)])
        {
            if (b is 0 or > 0x7F)
                return false;
        }

        return true;
    }

    private static void DisposeAll(List<X509Certificate2> certificates)
    {
        foreach (var certificate in certificates)
            certificate.Dispose();
    }
}

/// <summary>Why a Scheme Administrator root couldn't be loaded.</summary>
public enum SchemeAdministratorRootError
{
    /// <summary>The file couldn't be read.</summary>
    Unreadable,

    /// <summary>The data isn't a DER or PEM certificate.</summary>
    NotCertificate,

    /// <summary>The data holds no certificate.</summary>
    NoCertificate,

    /// <summary>A certificate says it isn't a certificate authority.</summary>
    NotCertificateAuthority,
}

/// <summary>A Scheme Administrator root certificate couldn't be loaded.</summary>
public sealed class SchemeAdministratorRootException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SchemeAdministratorRootException(SchemeAdministratorRootError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>Creates the exception with its cause.</summary>
    public SchemeAdministratorRootException(SchemeAdministratorRootError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>Why the root couldn't be loaded.</summary>
    public SchemeAdministratorRootError Error { get; }

    /// <summary>
    /// For <see cref="SchemeAdministratorRootError.NotCertificateAuthority"/>,
    /// the subject of the certificate at fault.
    /// </summary>
    public string? CertificateSubject { get; init; }

    /// <summary>
    /// For <see cref="SchemeAdministratorRootError.NotCertificateAuthority"/>,
    /// the common name of the certificate at fault.
    /// </summary>
    public string? CertificateName { get; init; }
}
