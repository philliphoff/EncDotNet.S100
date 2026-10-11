using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.Tests.Keys;

/// <summary>
/// The stored MCP identities (#845 H): references in settings, keys in the key
/// store, one in use, a command-line identity winning for its run.
/// </summary>
public sealed class SecomIdentityStoreTests : IDisposable
{
    private readonly KeysTestSupport _keys = new();

    public void Dispose() => _keys.Dispose();

    [Fact]
    public void Importing_stores_a_reference_and_the_key_and_uses_the_identity_and_writes_no_secret()
    {
        var identity = _keys.Identities.Load(_keys.IdentityFile(), "secret");
        var reference = _keys.Identities.Import(identity, "Bridge PC", makeDefault: true);

        Assert.Matches("^sc-ident:[0-9a-f]{8}$", reference.Id);
        Assert.Equal("Bridge PC", reference.DisplayName);
        Assert.Equal(KeysTestSupport.Mrn, reference.Mrn);
        Assert.Equal("Example MCP Root Certificate", reference.Issuer);
        Assert.Equal("Device", reference.Kind);
        Assert.Equal("memory", reference.KeyStore);
        Assert.Equal(1, _keys.Keys.Count);
        Assert.Equal(reference.Id, _keys.Settings.SecomDefaultIdentity);
        Assert.Equal(KeysTestSupport.Mrn, _keys.Trust.Identity?.Mrn);
        Assert.True(_keys.Trust.Identity?.Certificate.HasPrivateKey);

        var json = File.ReadAllText(_keys.Settings.SettingsFilePath!);
        Assert.Contains(reference.Thumbprint, json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_password_stores_nothing()
    {
        Assert.Throws<InvalidDataException>(() => _keys.Identities.Load(_keys.IdentityFile(), "wrong"));

        Assert.Empty(_keys.Settings.SecomIdentities);
        Assert.Equal(0, _keys.Keys.Count);
        Assert.Null(_keys.Trust.Identity);
    }

    [Fact]
    public void Removing_the_identity_in_use_clears_it_and_deletes_the_key()
    {
        var reference = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", makeDefault: true);

        _keys.Identities.Remove(reference.Id);

        Assert.Null(_keys.Trust.Identity);
        Assert.Null(_keys.Settings.SecomDefaultIdentity);
        Assert.Empty(_keys.Settings.SecomIdentities);
        Assert.Equal(0, _keys.Keys.Count);
    }

    [Fact]
    public void Several_identities_are_stored_and_one_is_used_at_a_time()
    {
        var bridge = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("bridge"), "secret"), "Bridge PC", makeDefault: true);
        var org = _keys.Identities.Import(
            _keys.Identities.Load(_keys.IdentityFile("org", "urn:mrn:mcp:org:mcc:soundcharts"), "secret"), "SoundCharts", makeDefault: false);

        Assert.Equal("Organisation", org.Kind);
        Assert.Equal(bridge.Id, _keys.Identities.DefaultId);

        _keys.Identities.MakeDefault(org.Id);
        Assert.Equal("urn:mrn:mcp:org:mcc:soundcharts", _keys.Trust.Identity?.Mrn);

        _keys.Identities.MakeDefault(null);
        Assert.Null(_keys.Trust.Identity);
        Assert.Equal(2, _keys.Settings.SecomIdentities.Count);
    }

    [Fact]
    public void At_start_the_identity_in_use_is_loaded_from_the_key_store()
    {
        var reference = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", makeDefault: true);
        var restarted = new SecomServerTrust(_keys.Anchors, _keys.Time);

        new SecomIdentityStore(_keys.Settings, restarted, _keys.Keys).Restore(commandLine: null);

        Assert.Equal(reference.Thumbprint, restarted.Identity?.Certificate.Thumbprint);
        Assert.Equal("Example MCP", restarted.Identity?.Anchor);
    }

    [Fact]
    public void A_command_line_identity_wins_for_its_run_and_the_stored_choice_is_kept()
    {
        var stored = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", makeDefault: true);
        var restarted = new SecomServerTrust(_keys.Anchors, _keys.Time);
        var store = new SecomIdentityStore(_keys.Settings, restarted, _keys.Keys);
        var commandLine = SecomClientIdentity.Load(_keys.IdentityFile("cli", "urn:mrn:mcp:device:mcc:soundcharts:cli"), "secret", _keys.Anchors);

        store.Restore(commandLine);
        store.MakeDefault(null);

        Assert.Same(commandLine, restarted.Identity);
        Assert.Null(_keys.Settings.SecomDefaultIdentity);
        store.MakeDefault(stored.Id);
        Assert.Same(commandLine, restarted.Identity);
        Assert.Equal(stored.Id, _keys.Settings.SecomDefaultIdentity);
    }

    [Fact]
    public void Replacing_an_identity_in_use_keeps_it_in_use_and_removes_the_old_one()
    {
        var old = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("old"), "secret"), "Bridge PC", makeDefault: true);

        var renewed = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("new"), "secret"), "Bridge PC", makeDefault: false, replacing: old.Id);

        Assert.Single(_keys.Settings.SecomIdentities);
        Assert.Equal(renewed.Id, _keys.Identities.DefaultId);
        Assert.Equal(renewed.Thumbprint, _keys.Trust.Identity?.Certificate.Thumbprint);
        Assert.Equal(1, _keys.Keys.Count);
    }

    [Fact]
    public void Round_one_s_identity_in_use_becomes_the_default()
    {
        var path = Path.Combine(_keys.Folder, "round1.json");
        File.WriteAllText(path, """{"SecomIdentityInUse":"sc-ident:4f9c2a7e"}""");

        var settings = ViewerSettings.Load(path);

        Assert.Equal("sc-ident:4f9c2a7e", settings.SecomDefaultIdentity);
        Assert.Null(settings.SecomIdentityInUse);
        settings.Save();
        Assert.DoesNotContain("SecomIdentityInUse", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void At_start_every_stored_identity_is_registered_and_the_default_restored()
    {
        var bridge = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("bridge"), "secret"), "Bridge PC", makeDefault: false);
        var org = _keys.Identities.Import(
            _keys.Identities.Load(_keys.IdentityFile("org", "urn:mrn:mcp:org:mcc:soundcharts"), "secret"), "SoundCharts", makeDefault: true);
        var restarted = new SecomServerTrust(_keys.Anchors, _keys.Time);

        new SecomIdentityStore(_keys.Settings, restarted, _keys.Keys).Restore(commandLine: null);

        Assert.Equal(new[] { bridge.Id, org.Id }.Order(StringComparer.Ordinal), restarted.Identities.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(org.Id, restarted.DefaultIdentityId);
        Assert.Equal(bridge.Thumbprint, restarted.Resolve(bridge.Id).Identity?.Certificate.Thumbprint);
    }

    [Fact]
    public async Task Removing_the_default_sets_the_new_one_and_moves_the_sources_that_named_it_to_none()
    {
        var bridge = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("bridge"), "secret"), "Bridge PC", makeDefault: true);
        var org = _keys.Identities.Import(
            _keys.Identities.Load(_keys.IdentityFile("org", "urn:mrn:mcp:org:mcc:soundcharts"), "secret"), "SoundCharts", makeDefault: false);
        using var library = Library();
        var direct = Secom(bridge.Id);
        var viaDefault = Secom(EncDotNet.S100.Collections.Secom.SecomIdentitySetting.Default);
        library.AddCollection("SECOM", [direct, viaDefault]);
        await library.WhenIdle();
        _keys.Identities.Library = library;

        _keys.Identities.Remove(bridge.Id, newDefault: org.Id);

        Assert.Equal(org.Id, _keys.Identities.DefaultId);
        Assert.Equal(org.Id, _keys.Trust.DefaultIdentityId);
        Assert.False(_keys.Trust.Identities.ContainsKey(bridge.Id));
        Assert.Equal(1, _keys.Keys.Count);
        var sources = library.Collections[0].Sources.Select(s => (EncDotNet.S100.Collections.SecomSource)s.Definition).ToArray();
        Assert.Equal(["none", "default"], sources.Select(s => s.Identity));
    }

    [Fact]
    public async Task Replacing_an_identity_moves_the_sources_that_named_it_to_the_new_one()
    {
        var old = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("old"), "secret"), "Bridge PC", makeDefault: false);
        using var library = Library();
        library.AddCollection("SECOM", [Secom(old.Id)]);
        await library.WhenIdle();
        _keys.Identities.Library = library;

        var renewed = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("new"), "secret"), "Bridge PC", makeDefault: false, replacing: old.Id);

        Assert.Equal(renewed.Id, ((EncDotNet.S100.Collections.SecomSource)library.Collections[0].Sources[0].Definition).Identity);
        Assert.Equal(renewed.Id, _keys.Identities.DefaultId);  // the old one was the default (the first import)
    }

    private EncDotNet.S100.Collections.Library.CollectionLibrary Library()
    {
        var library = new EncDotNet.S100.Collections.Library.CollectionLibrary(
            EncDotNet.S100.Collections.Indexing.CollectionIndexer.CreateDefault(),
            new EncDotNet.S100.Collections.Library.CollectionLibraryOptions(
                Path.Combine(_keys.Folder, "collections.json"), Path.Combine(_keys.Folder, "index-cache")));
        library.Initialize();
        return library;
    }

    private static EncDotNet.S100.Collections.SecomSource Secom(string identity) =>
        new(Guid.NewGuid(), null, new Uri("https://secom.invalid/api/secom/"), EncDotNet.S100.Collections.SecomFilter.All) { Identity = identity };

    [Fact]
    public void A_vessel_certificate_is_a_vessel_and_a_test_registrys_is_a_test()
    {
        var vessel = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("vessel", attributes: new Dictionary<string, string>
        {
            ["2.25.291283622413876360871493815653100799259"] = "9876543",
        }), "secret"), "Bridge PC", makeDefault: false);
        var test = _keys.Identities.Import(
            _keys.Identities.Load(_keys.IdentityFile("dev", "urn:mrn:mcp:device:mcc-test:soundcharts:dev"), "secret"), "Dev laptop", makeDefault: false);

        Assert.Equal("Vessel", vessel.Kind);
        Assert.Equal("Test", test.Kind);
    }

    [Fact]
    public void Reference_ids_are_recognised_and_paths_are_not()
    {
        Assert.Equal("sc-ident:4f9c2a7e", SecomIdentityStore.ReferenceIdOf("4F9C2A7E0011"));
        Assert.True(SecomIdentityStore.IsReferenceId("sc-ident:4f9c2a7e"));
        Assert.False(SecomIdentityStore.IsReferenceId("/tmp/identity.p12"));
    }

    [Fact]
    public void A_revoked_identity_in_use_is_recorded_and_no_longer_used()
    {
        using var leaf = _keys.Leaf(KeysTestSupport.Mrn, "bridge-pc", 300, withCrl: true);
        var crl = _keys.Crl(leaf);
        var trust = new SecomServerTrust(_keys.Anchors, _keys.Time,
            new SecomRevocation(new HttpClient(new CrlHandler(crl)), timeProvider: _keys.Time));
        var store = new SecomIdentityStore(_keys.Settings, trust, _keys.Keys);
        var reference = store.Import(SecomClientIdentity.FromCertificate(
            System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                leaf.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, "pw"), "pw",
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable), _keys.Anchors), "Bridge PC", makeDefault: true);

        Assert.Equal(SecomRevocationStatus.Revoked, Assert.Single(store.CheckRevocation()).Value.Status);

        Assert.Null(trust.Identity);
        Assert.NotNull(reference.RevokedAt);
        Assert.Null(_keys.Settings.SecomDefaultIdentity);
        Assert.Throws<InvalidOperationException>(() => store.MakeDefault(reference.Id));
    }

    private sealed class CrlHandler(byte[] crl) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri?.AbsoluteUri == KeysTestSupport.CrlUri
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(crl) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    [Fact]
    public async Task Set_secom_identity_chooses_a_stored_identity_by_reference_id()
    {
        var reference = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", makeDefault: false);
        var tool = new EncDotNet.S100.Mcp.Tools.Library.SetSecomIdentityTool(_keys.Trust, _keys.Time, new StoreReferences(_keys.Identities));
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await tool.InvokeAsync(reference.Id, null, null, ct: ct)).TryGetValue(out var set));
        Assert.Equal(reference.Id, set.Reference);
        Assert.Equal(KeysTestSupport.Mrn, set.Mrn);
        Assert.Equal(reference.Id, _keys.Identities.DefaultId);
        Assert.False((await tool.InvokeAsync("sc-ident:00000000", null, null, ct: ct)).TryGetValue(out _));

        var withoutStore = new EncDotNet.S100.Mcp.Tools.Library.SetSecomIdentityTool(_keys.Trust, _keys.Time);
        Assert.False((await withoutStore.InvokeAsync(reference.Id, null, null, ct: ct)).TryGetValue(out _));
    }

    /// <summary>The viewer's adapter without its UI-thread hop.</summary>
    private sealed class StoreReferences(SecomIdentityStore store) : EncDotNet.S100.Mcp.Tools.Library.ISecomIdentityReferences
    {
        public SecomClientIdentity Use(string referenceId) => store.MakeDefault(referenceId)!;

        public string? ReferenceOf(SecomClientIdentity identity) =>
            store.Identities.FirstOrDefault(r => r.Thumbprint == identity.Certificate.Thumbprint)?.Id;
    }
}
