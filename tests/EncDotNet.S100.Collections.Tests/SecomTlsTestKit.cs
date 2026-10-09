using System.ComponentModel;
using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// A root and intermediate CA, and the server and client leaves the
/// intermediate issues, for SECOM TLS tests (#829, #832). Making one is slow on
/// macOS (every signing and key operation goes through the keychain), so tests
/// share one per class.
/// </summary>
public sealed class TestPki : IDisposable
{
    private readonly ECDsa _intermediateKey;

    private TestPki(X509Certificate2 root, X509Certificate2 intermediate, ECDsa intermediateKey)
    {
        Root = root;
        Intermediate = intermediate;
        _intermediateKey = intermediateKey;
    }

    public X509Certificate2 Root { get; }

    public X509Certificate2 Intermediate { get; }

    public SecomTrustAnchors Anchors => new([new SecomTrustAnchor("Test MCP", Root)], [Intermediate]);

    public static TestPki Create()
    {
        // Wide enough to hold every leaf, including an expired one.
        var from = DateTimeOffset.UtcNow.AddDays(-60);
        var to = DateTimeOffset.UtcNow.AddDays(60);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var root = Authority("CN=Test MCP Root Certificate", rootKey).CreateSelfSigned(from, to);

        var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var intermediate = Authority("CN=Test MCP Identity Registry", intermediateKey)
            .Create(root, from, to, RandomNumberGenerator.GetBytes(8));
        return new TestPki(root, intermediate.CopyWithPrivateKey(intermediateKey), intermediateKey);
    }

    /// <summary>A server certificate.</summary>
    public X509Certificate2 Leaf(
        string[] dnsNames,
        string commonName = "SECOM service",
        string? mrn = null,
        IPAddress[]? ipAddresses = null,
        DateTimeOffset? notAfter = null,
        bool withKey = true)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in dnsNames)
            san.AddDnsName(name);
        foreach (var address in ipAddresses ?? [])
            san.AddIpAddress(address);
        if (mrn is not null)
            san.AddUserPrincipalName(mrn);  // stands in for the MCP MRN othername
        if (dnsNames.Length > 0 || mrn is not null || ipAddresses is { Length: > 0 })
            request.CertificateExtensions.Add(san.Build());

        return Issue(request, key, notAfter, withKey);
    }

    /// <summary>
    /// A client identity as an MCP Identity Registry issues one: the MRN as the
    /// subject <c>UID</c>, for client authentication.
    /// </summary>
    public X509Certificate2 ClientLeaf(string mrn, string commonName = "Test vessel", DateTimeOffset? notAfter = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedNameBuilder();
        subject.Add("0.9.2342.19200300.100.1.1", mrn, UniversalTagNumber.UTF8String);
        subject.AddCommonName(commonName);
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        return Issue(request, key, notAfter, withKey: true);
    }

    /// <summary>Whether <paramref name="client"/> chains to this PKI's root (what a test server accepts).</summary>
    public bool Issued(X509Certificate2? client)
    {
        if (client is null)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(Root);
        chain.ChainPolicy.ExtraStore.Add(Intermediate);
        return chain.Build(client);
    }

    private X509Certificate2 Issue(CertificateRequest request, ECDsa key, DateTimeOffset? notAfter, bool withKey)
    {
        var to = notAfter ?? DateTimeOffset.UtcNow.AddDays(10);
        var issued = request.Create(Intermediate, to.AddDays(-20), to, RandomNumberGenerator.GetBytes(8));
        if (!withKey)
            return issued;
        using (issued)
            return issued.CopyWithPrivateKey(key);
    }

    private static CertificateRequest Authority(string name, ECDsa key)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    public void Dispose()
    {
        Root.Dispose();
        Intermediate.Dispose();
        _intermediateKey.Dispose();
    }
}

/// <summary>How a <see cref="TlsServer"/> treats client certificates.</summary>
public enum ClientCertificateMode
{
    /// <summary>It never asks for one.</summary>
    None,

    /// <summary>It asks during the handshake and accepts any, then answers <c>GetSummary</c> only for an accepted one (401 otherwise), as KHRA and AMSA do.</summary>
    ForSummary,

    /// <summary>It refuses the handshake without an accepted one.</summary>
    AtHandshake,
}

/// <summary>
/// A local HTTPS server on a loopback port: "ok" for <c>/</c>, and minimal
/// SECOM Capability and GetSummary answers under any other path.
/// </summary>
internal sealed class TlsServer : IAsyncDisposable
{
    private const string Capability = """{"capability":[{"containerType":0,"dataProductType":"S124","implementedInterfaces":{"get":true,"getSummary":true}}]}""";
    private const string Summary = """{"summaryObject":[],"pagination":{"totalItems":0,"maxItemsPerPage":1}}""";

    private readonly TcpListener _listener;
    private readonly SslStreamCertificateContext _context;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly X509Certificate2 _certificate;
    private readonly ClientCertificateMode _clientMode;
    private readonly Func<X509Certificate2?, bool> _acceptsClient;

    private TlsServer(
        X509Certificate2 certificate, X509Certificate2? intermediate, ClientCertificateMode clientMode, Func<X509Certificate2?, bool>? acceptsClient)
    {
        // Round-trip through PKCS#12: macOS's TLS stack needs a persisted key.
        _certificate = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12, "t"), "t");
        _clientMode = clientMode;
        _acceptsClient = acceptsClient ?? (_ => true);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _context = SslStreamCertificateContext.Create(
            _certificate, intermediate is null ? null : new X509Certificate2Collection(intermediate), offline: true);
        _loop = Task.Run(AcceptAsync);
    }

    /// <summary>The client certificates presented to this server, in order (null for none).</summary>
    public List<X509Certificate2?> ClientCertificates { get; } = [];

    public static Task<TlsServer> StartAsync(
        X509Certificate2 certificate,
        X509Certificate2? intermediate = null,
        ClientCertificateMode clientMode = ClientCertificateMode.None,
        Func<X509Certificate2?, bool>? acceptsClient = null) =>
        Task.FromResult(new TlsServer(certificate, intermediate, clientMode, acceptsClient));

    public Uri Uri(string path, string host = "localhost") =>
        new($"https://{host}:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}");

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var ssl = new SslStream(client.GetStream());
                var options = new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = _context,
                    ClientCertificateRequired = _clientMode != ClientCertificateMode.None,
                    RemoteCertificateValidationCallback = (_, presented, _, _) =>
                    {
                        var certificate = presented is null ? null : new X509Certificate2(presented);
                        lock (ClientCertificates)
                            ClientCertificates.Add(certificate);
                        return _clientMode != ClientCertificateMode.AtHandshake || _acceptsClient(certificate);
                    },
                };
                await ssl.AuthenticateAsServerAsync(options, _stop.Token);
                var accepted = _acceptsClient(ssl.RemoteCertificate is { } remote ? new X509Certificate2(remote) : null);

                using var reader = new StreamReader(ssl, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_stop.Token) ?? string.Empty;
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token)))
                {
                }

                var path = requestLine.Split(' ') is [_, var target, ..] ? target.Split('?')[0] : "/";
                var (status, type, body) = path == "/" ? ("200 OK", "text/plain", "ok")
                    : path.EndsWith("/capability", StringComparison.Ordinal) ? ("200 OK", "application/json", Capability)
                    : _clientMode == ClientCertificateMode.ForSummary && !accepted ? ("401 Unauthorized", "text/plain", "certificate required")
                    : ("200 OK", "application/json", Summary);
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                await ssl.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
                await ssl.WriteAsync(bytes, _stop.Token);
                await ssl.FlushAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException or Win32Exception)
            {
                // A certificate was refused on either side, or the server is stopping.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
        _certificate.Dispose();
    }
}
