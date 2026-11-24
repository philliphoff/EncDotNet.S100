using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EncDotNet.S100.ExchangeSets.Tests;

public sealed class SchemeAdministratorRootsTests : IDisposable
{
    private readonly X509Certificate2 _root = Create("CN=Root A", ca: true);
    private readonly X509Certificate2 _other = Create("CN=Root B", ca: true);
    private readonly X509Certificate2 _leaf = Create("CN=Leaf", ca: false);
    private readonly string _dir = Directory.CreateTempSubdirectory("sa-roots-").FullName;

    public void Dispose()
    {
        _root.Dispose();
        _other.Dispose();
        _leaf.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Loads_a_DER_certificate()
    {
        var roots = SchemeAdministratorRoots.Load(_root.RawData);

        Assert.Equal(_root.RawData, Assert.Single(roots).RawData);
    }

    [Fact]
    public void Loads_every_certificate_in_a_PEM_bundle()
    {
        var pem = _root.ExportCertificatePem() + "\n" + _other.ExportCertificatePem();

        var roots = SchemeAdministratorRoots.Load(Encoding.ASCII.GetBytes(pem));

        Assert.Equal(["CN=Root A", "CN=Root B"], roots.Select(r => r.Subject));
    }

    [Fact]
    public void LoadFile_reads_the_file()
    {
        var path = Path.Combine(_dir, "sa.pem");
        File.WriteAllText(path, _root.ExportCertificatePem());

        Assert.Single(SchemeAdministratorRoots.LoadFile(path));
    }

    [Fact]
    public void Rejects_an_end_entity_certificate()
    {
        var ex = Assert.Throws<SchemeAdministratorRootException>(
            () => SchemeAdministratorRoots.Load(_leaf.RawData, "leaf.cer"));

        Assert.Equal(SchemeAdministratorRootError.NotCertificateAuthority, ex.Error);
        Assert.Equal("Leaf", ex.CertificateName);
        Assert.Contains("isn't a certificate authority", ex.Message);
        Assert.Contains("leaf.cer", ex.Message);
    }

    [Fact]
    public void Rejects_data_that_is_not_a_certificate()
    {
        var ex = Assert.Throws<SchemeAdministratorRootException>(
            () => SchemeAdministratorRoots.Load("not a certificate"u8));
        Assert.Equal(SchemeAdministratorRootError.NotCertificate, ex.Error);
    }

    [Fact]
    public void Rejects_a_missing_file()
    {
        var ex = Assert.Throws<SchemeAdministratorRootException>(
            () => SchemeAdministratorRoots.LoadFile(Path.Combine(_dir, "missing.pem")));
        Assert.Equal(SchemeAdministratorRootError.Unreadable, ex.Error);
    }

    [Fact]
    public void Fingerprint_is_colon_separated_SHA256()
    {
        var fingerprint = SchemeAdministratorRoots.Sha256Fingerprint(_root);

        Assert.Equal(32 * 3 - 1, fingerprint.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(_root.RawData)), fingerprint.Replace(":", ""));
    }

    [Fact]
    public void Built_in_IHO_root_is_the_Edition_5_2_root()
    {
        using var root = SchemeAdministratorRoots.LoadIhoRoot();

        Assert.Equal(SchemeAdministratorRoots.IhoRootFingerprint, SchemeAdministratorRoots.Sha256Fingerprint(root));
        Assert.Contains("O=International Hydrographic Organization", root.Subject);
        Assert.Equal(root.Subject, root.Issuer);
        Assert.NotNull(root.GetECDsaPublicKey());
    }

    [Fact]
    public void Built_in_IHO_testing_root_is_the_Edition_4_0_certificate()
    {
        using var root = SchemeAdministratorRoots.LoadIhoTestingRoot();

        Assert.Equal(SchemeAdministratorRoots.IhoTestingRootFingerprint, SchemeAdministratorRoots.Sha256Fingerprint(root));
        Assert.Contains("CN=IHO S-100 SCHEME ADMINISTRATOR", root.Subject);
        Assert.Equal(root.Subject, root.Issuer);
    }

    private static X509Certificate2 Create(string subject, bool ca)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        var now = DateTimeOffset.UtcNow;
        using var withKey = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        return X509CertificateLoader.LoadCertificate(withKey.RawData);
    }
}
