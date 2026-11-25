using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.ExchangeSets.Protection;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Datasets badge of a signed exchange set against the Scheme
/// administrators that are on: verified and named, verified (test),
/// unverified, and checked again when the roots change.
/// </summary>
public sealed class ExchangeSetServiceSchemeAdministratorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("s100-sa-viewer-").FullName;
    private readonly X509Certificate2 _schemeRoot;
    private readonly X509Certificate2 _dataServer;

    public ExchangeSetServiceSchemeAdministratorTests()
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var rootRequest = new CertificateRequest("CN=Test Scheme Administrator", rootKey, HashAlgorithmName.SHA384);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, true));
        _schemeRoot = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));

        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var serverRequest = new CertificateRequest("CN=Test Data Server", serverKey, HashAlgorithmName.SHA384);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var serverPublic = serverRequest.Create(_schemeRoot, now.AddDays(-1), now.AddDays(30), RandomNumberGenerator.GetBytes(8));
        _dataServer = serverPublic.CopyWithPrivateKey(serverKey);
    }

    public void Dispose()
    {
        _schemeRoot.Dispose();
        _dataServer.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_set_signed_under_a_trusted_root_is_verified_and_names_it()
    {
        var store = Store(isTest: false, enabled: true);
        var (datasets, service) = await OpenSignedAsync(store);
        using var _ = service;

        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal(SignatureStatus.Verified, await SettledAsync(header));
        Assert.Equal("Signatures verified against Example SA", header.SignatureTooltip);
        Assert.False(header.ShowsSchemeAdministratorsLink);
    }

    [Fact]
    public async Task A_set_signed_under_the_test_root_is_verified_test()
    {
        var store = Store(isTest: true, enabled: true);
        var (datasets, service) = await OpenSignedAsync(store);
        using var _ = service;

        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal(SignatureStatus.VerifiedTest, await SettledAsync(header));
        Assert.Contains("Test data, not for navigation", header.SignatureTooltip, StringComparison.Ordinal);
        Assert.True(header.ShowsSchemeAdministratorsLink);
    }

    [Fact]
    public async Task Turning_the_root_on_verifies_open_sets_again()
    {
        var store = Store(isTest: false, enabled: false);
        var (datasets, service) = await OpenSignedAsync(store);
        using var _ = service;

        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal(SignatureStatus.Unverified, await SettledAsync(header));
        Assert.Equal(
            "Signatures match the data, but no scheme administrator is trusted yet. Add one in Settings, Keys and certificates.",
            header.SignatureTooltip);
        Assert.True(header.ShowsSchemeAdministratorsLink);

        store.SetEnabled(store.Entries[0].Id, true);

        for (var attempt = 0; attempt < 250 && header.SignatureStatus != SignatureStatus.Verified; attempt++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(SignatureStatus.Verified, header.SignatureStatus);
    }

    private SchemeAdministratorStore Store(bool isTest, bool enabled)
    {
        var settings = new ViewerSettings { SettingsFilePath = Path.Combine(_root, "settings.json") };
        var store = new SchemeAdministratorStore(settings, [new SchemeAdministratorStore.BuiltInRoot("Example SA", _schemeRoot, isTest)]);
        if (enabled != !isTest)
            store.SetEnabled(store.Entries[0].Id, enabled);
        return store;
    }

    private async Task<(DatasetsViewModel Datasets, ExchangeSetService Service)> OpenSignedAsync(SchemeAdministratorStore store)
    {
        var folder = SignedCopy();
        var datasets = new DatasetsViewModel(new NoopLoader());
        var service = new ExchangeSetService(datasets, Notifications.TestNotifications.Create(),
            lazyCoordinator: null, metadataReader: null, s57CatalogCache: null, schemeAdministrators: store);
        await service.OpenAsync(folder, cancellationToken: TestContext.Current.CancellationToken);
        return (datasets, service);
    }

    private static async Task<SignatureStatus> SettledAsync(ExchangeSetHeader header)
    {
        for (var attempt = 0; attempt < 500 && header.SignatureStatus is SignatureStatus.Unknown or SignatureStatus.Checking; attempt++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        return header.SignatureStatus;
    }

    /// <summary>
    /// A copy of the synthetic exchange set with every dataset signed by the
    /// data server, as an S-100 Edition 5.2 catalogue.
    /// </summary>
    private string SignedCopy()
    {
        var source = LibraryTestContext.Datasets("ExchangeSets", "Synthetic-Renderable");
        var target = Path.Combine(_root, $"signed-{Guid.NewGuid():N}");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        XNamespace xc = "http://www.iho.int/s100/xc/5.2";
        XNamespace se = "http://www.iho.int/s100/se/5.2";
        var cataloguePath = Path.Combine(target, "CATALOG.XML");
        var document = XDocument.Parse(File.ReadAllText(cataloguePath).Replace("http://www.iho.int/s100/xc/5.0", xc.NamespaceName));
        var catalogue = document.Root!;
        catalogue.SetAttributeValue(XNamespace.Xmlns + "S100SE", se.NamespaceName);
        catalogue.Element(xc + "contact")!.AddAfterSelf(new XElement(xc + "certificates",
            new XElement(se + "schemeAdministrator", new XAttribute("id", "TEST-SA")),
            new XElement(se + "certificate", new XAttribute("id", "TEST-DS"), new XAttribute("issuer", "TEST-SA"),
                Convert.ToBase64String(_dataServer.RawData))));

        using var signer = new Part15Signer(_dataServer, "TEST-DS", "TEST-SA");
        var n = 0;
        foreach (var metadata in catalogue.Descendants(xc + "S100_DatasetDiscoveryMetadata"))
        {
            var fileName = metadata.Element(xc + "fileName")!.Value;
            var signature = signer.SignData(File.ReadAllBytes(Path.Combine(target, fileName)), $"sig-{++n}");
            metadata.Element(xc + "dataProtection")!.AddAfterSelf(
                new XElement(xc + "digitalSignatureReference", "ECDSA-384-SHA2"),
                new XElement(xc + "digitalSignatureValue",
                    new XElement(se + "S100_SE_SignatureOnData",
                        new XAttribute("id", signature.Id),
                        new XAttribute("certificateRef", signature.CertificateRef),
                        new XAttribute("dataStatus", "unencrypted"),
                        Convert.ToBase64String(signature.Value))));
        }

        document.Save(cataloguePath);
        return target;
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(MapViews views, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => [];
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => [];
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }
}
