using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// SECOM signer trust (issue #823): the built-in MCP MCC anchors, and how a
/// signer's chain and stated thumbprint are judged.
/// </summary>
public sealed class SecomTrustAnchorsTests
{
    [Fact]
    public void The_built_in_anchors_are_the_recorded_mcp_mcc_chain()
    {
        // Fingerprints recorded in src/EncDotNet.S100.Collections/Secom/TrustAnchors/README.md.
        var builtIn = SecomTrustAnchors.BuiltIn;

        var root = Assert.Single(builtIn.Roots);
        Assert.Equal("MCP MCC", root.Name);
        Assert.Equal("ec1938782d8c8c228bc214d19fbf1e65e2db689675d4e4f27e2f6fbedcefd8db", Sha256(root.Certificate));
        var intermediate = Assert.Single(builtIn.Intermediates);
        Assert.Equal("45c34d53a13cff3338f6472502965c59a4ae16bd436daef8790357a53f628ac4", Sha256(intermediate));
        Assert.Contains("urn:mrn:mcp:ca:mcc:mcp-idreg-new", intermediate.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signer_issued_by_an_intermediate_is_trusted_through_it()
    {
        using var chain = TestChain.Create();
        var anchors = SecomTrustAnchors.FromPem("Test CA", chain.RootPem + "\n" + chain.IntermediatePem);
        Assert.Single(anchors.Roots);
        Assert.Single(anchors.Intermediates);

        // The service sends only the signer; the anchors supply the intermediate.
        Assert.Equal("Test CA", anchors.FindAnchor(chain.Leaf, [], statedThumbprint: null));

        // The stated thumbprint may name the root or the intermediate, by SHA-256 or SHA-1.
        Assert.Equal("Test CA", anchors.FindAnchor(chain.Leaf, [], Sha256(chain.Root)));
        Assert.Equal("Test CA", anchors.FindAnchor(chain.Leaf, [], Sha256(chain.Intermediate)));
        Assert.Equal("Test CA", anchors.FindAnchor(chain.Leaf, [], chain.Root.Thumbprint));

        // A thumbprint naming a CA outside the chain is not trusted.
        Assert.Null(anchors.FindAnchor(chain.Leaf, [], Sha256(chain.Leaf)));
        Assert.Null(anchors.FindAnchor(chain.Leaf, [], new string('0', 64)));
    }

    [Fact]
    public void A_signer_without_its_intermediate_or_root_is_not_trusted()
    {
        using var chain = TestChain.Create();
        var rootOnly = new SecomTrustAnchors([new SecomTrustAnchor("Test CA", chain.Root)]);

        Assert.Null(rootOnly.FindAnchor(chain.Leaf, [], null));
        // Unless the object carries the intermediate itself.
        Assert.Equal("Test CA", rootOnly.FindAnchor(chain.Leaf, [chain.Intermediate], null));

        Assert.Null(SecomTrustAnchors.None.FindAnchor(chain.Leaf, [chain.Intermediate], null));
        Assert.Null(SecomTrustAnchors.BuiltIn.FindAnchor(chain.Leaf, [chain.Intermediate], null));
    }

    [Fact]
    public void Anchors_can_be_added_to_the_built_in_set()
    {
        using var chain = TestChain.Create();
        var anchors = SecomTrustAnchors.BuiltIn.With(new SecomTrustAnchor("Test CA", chain.Root), [chain.Intermediate]);

        Assert.Equal(["MCP MCC", "Test CA"], anchors.Roots.Select(r => r.Name));
        Assert.Equal("Test CA", anchors.FindAnchor(chain.Leaf, [], null));
    }

    private static string Sha256(X509Certificate2 certificate) => Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));

    /// <summary>A root, an intermediate it issued, and a leaf the intermediate issued.</summary>
    private sealed class TestChain : IDisposable
    {
        private TestChain(X509Certificate2 root, X509Certificate2 intermediate, X509Certificate2 leaf)
        {
            Root = root;
            Intermediate = intermediate;
            Leaf = leaf;
        }

        public X509Certificate2 Root { get; }

        public X509Certificate2 Intermediate { get; }

        public X509Certificate2 Leaf { get; }

        public string RootPem => Root.ExportCertificatePem();

        public string IntermediatePem => Intermediate.ExportCertificatePem();

        public static TestChain Create()
        {
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow.AddDays(30);

            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = Authority("CN=Test Root", rootKey);
            var root = rootRequest.CreateSelfSigned(from, to);

            using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var intermediateRequest = Authority("CN=Test Identity Registry", intermediateKey);
            using var intermediatePublic = intermediateRequest.Create(root, from, to, [1, 2, 3]);
            var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);

            using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var leafRequest = new CertificateRequest("CN=Test Service", leafKey, HashAlgorithmName.SHA256);
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            var leaf = leafRequest.Create(intermediate, from, to, [4, 5, 6]);

            return new TestChain(root, intermediate, leaf);
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
            Leaf.Dispose();
        }
    }
}
