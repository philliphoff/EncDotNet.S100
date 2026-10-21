using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Collections.Net;

/// <summary>What <see cref="HardenedTls.CheckRevocation"/> found.</summary>
public enum TlsRevocationStatus
{
    /// <summary>Revocation could not be determined (no CRL or OCSP answer in time): soft-fail.</summary>
    NotChecked = 0,

    /// <summary>Every certificate below the root was checked, and none is revoked.</summary>
    NotRevoked,

    /// <summary>A certificate in the chain is revoked.</summary>
    Revoked,
}

/// <summary>How <see cref="HardenedTls"/> validates server certificates.</summary>
public sealed record HardenedTlsOptions
{
    /// <summary>The defaults: revocation checked online, soft-fail, 10-second fetch budget.</summary>
    public static HardenedTlsOptions Default { get; } = new();

    /// <summary>
    /// True (the default) to check the server's chain for revocation (CRL or
    /// OCSP, as the platform supports). A revoked chain is refused; one whose
    /// status cannot be had is allowed, as browsers do.
    /// </summary>
    public bool CheckRevocation { get; init; } = true;

    /// <summary>The most time spent fetching revocation data for one chain.</summary>
    public TimeSpan RevocationTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Server-certificate validation layered over the operating system's: the
/// platform's decision stands, and a chain it accepts is still refused when
/// it carries a <see cref="DistrustedKeys">distrusted key</see> or is revoked.
/// </summary>
/// <remarks>
/// .NET's own defaults check neither: <see cref="SocketsHttpHandler"/> leaves
/// <see cref="SslClientAuthenticationOptions.CertificateRevocationCheckMode"/>
/// at <see cref="X509RevocationMode.NoCheck"/>, and on Linux trust comes from
/// the distribution's CA bundle, which can lag a CA compromise by days.
/// Revocation is soft-fail: an unreachable responder never blocks a download,
/// but a CA that has published a revocation is heard.
/// </remarks>
public static class HardenedTls
{
    /// <summary>
    /// A handler for general HTTPS clients (feeds, catalogues, downloads,
    /// update checks) that validates server certificates with <see cref="Validate"/>.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(HardenedTlsOptions? options = null)
    {
        options ??= HardenedTlsOptions.Default;
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback =
            (_, certificate, chain, errors) => Validate(certificate as X509Certificate2, chain, errors, options);
        return handler;
    }

    /// <summary>
    /// Whether to allow a server certificate: the platform found nothing wrong
    /// (<paramref name="errors"/> is <see cref="SslPolicyErrors.None"/>), no
    /// certificate in the chain carries a distrusted key, and (when
    /// <see cref="HardenedTlsOptions.CheckRevocation"/>) none is revoked.
    /// </summary>
    public static bool Validate(X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors, HardenedTlsOptions? options = null)
    {
        options ??= HardenedTlsOptions.Default;
        if (errors != SslPolicyErrors.None || certificate is null)
            return false;
        if (DistrustedKeys.FindDistrusted(certificate, chain) is not null)
            return false;
        if (!options.CheckRevocation)
            return true;

        return CheckRevocation(certificate, Presented(chain), options.RevocationTimeout) != TlsRevocationStatus.Revoked;
    }

    /// <summary>
    /// Builds <paramref name="certificate"/>'s chain against the system trust
    /// store with online revocation checking, and reports what it found. A
    /// chain that reaches a distrusted key through any path is reported
    /// <see cref="TlsRevocationStatus.Revoked"/>.
    /// </summary>
    /// <param name="certificate">The end-entity certificate.</param>
    /// <param name="presented">Intermediates the server sent, to help build the chain.</param>
    /// <param name="timeout">The most time spent fetching revocation data.</param>
    public static TlsRevocationStatus CheckRevocation(X509Certificate2 certificate, IEnumerable<X509Certificate2> presented, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(presented);

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.UrlRetrievalTimeout = timeout;
        chain.ChainPolicy.ExtraStore.AddRange(presented.ToArray());

        try
        {
            chain.Build(certificate);
        }
        catch (CryptographicException)
        {
            return TlsRevocationStatus.NotChecked;
        }

        if (DistrustedKeys.FindDistrusted(certificate, chain) is not null)
            return TlsRevocationStatus.Revoked;
        return StatusOf(chain);
    }

    /// <summary>The revocation status <paramref name="chain"/>'s build reported.</summary>
    internal static TlsRevocationStatus StatusOf(X509Chain chain)
    {
        var flags = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, s) => all | s.Status);
        foreach (var element in chain.ChainElements)
        {
            foreach (var status in element.ChainElementStatus)
                flags |= status.Status;
        }

        if (flags.HasFlag(X509ChainStatusFlags.Revoked))
            return TlsRevocationStatus.Revoked;
        if ((flags & (X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation)) != 0)
            return TlsRevocationStatus.NotChecked;
        return TlsRevocationStatus.NotRevoked;
    }

    /// <summary>The certificates above the end-entity in the platform's chain.</summary>
    internal static X509Certificate2[] Presented(X509Chain? chain) =>
        chain?.ChainElements.Skip(1).Select(e => e.Certificate).ToArray() ?? [];
}
