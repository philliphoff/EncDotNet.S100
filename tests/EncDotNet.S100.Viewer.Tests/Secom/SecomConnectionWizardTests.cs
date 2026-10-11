using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services.Secom;
using EncDotNet.S100.Viewer.Tests.Keys;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Secom;

namespace EncDotNet.S100.Viewer.Tests.Secom;

/// <summary>
/// The Add online catalogue wizard's Connection step for a SECOM service
/// (#883 C): an identity per source, re-probed when it changes, and "Add and
/// wait" for a service that can't be read yet.
/// </summary>
public sealed class SecomConnectionWizardTests : IDisposable
{
    private static readonly KnownCatalogueSource Khra = KnownCatalogueSources.FromRegistry(new SecomRegistryService(
        "urn:mrn:mcp:service:mcc:khra:s-124", "KHRA S-124 navigational warnings", "1.0", SecomRegistryStatus.Released, "S-124",
        new Uri("https://secom.khra.example/api/secom/"), "urn:mrn:mcp:org:mcc:khra", null, null, null));

    private readonly LibraryTestContext _context = new();
    private readonly KeysTestSupport _keys = new();
    private readonly CollectionLibrary _library;
    private readonly List<(Uri Uri, string? Identity)> _probes = [];
    private readonly List<string?> _reads = [];
    private Func<string?, SecomProbeResult> _probe = _ => new SecomProbeResult(SecomReachability.NeedsCertificate, "401");

    public SecomConnectionWizardTests()
    {
        _library = _context.CreateService();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
        _keys.Dispose();
    }

    [Fact]
    public async Task A_service_that_needs_a_certificate_is_added_to_wait_with_its_directory_products()
    {
        var wizard = Wizard();
        await ChooseKhraAsync(wizard);

        Assert.Equal(2, wizard.CurrentStep);
        var connection = wizard.Connection!;
        Assert.Equal(("https://secom.khra.example/api/secom/", "default"), (_probes[^1].Uri.AbsoluteUri, _probes[^1].Identity));
        Assert.Equal(SecomConnectionState.NeedsCertificate, connection.State);
        Assert.Equal("Needs a certificate", connection.TagText);
        Assert.Equal("urn:mrn:mcp:service:mcc:khra:s-124", connection.Mrn);
        Assert.Equal("Import identity…", Assert.Single(connection.Actions).Label);
        Assert.Equal("Add and wait", wizard.NextLabel);
        Assert.Equal(
            "Add and wait adds KHRA S-124 navigational warnings with no datasets. It lists automatically once you import an identity and choose it.",
            connection.WaitNote);
        Assert.True(wizard.NextCommand.CanExecute(null));

        await NextAsync(wizard);

        Assert.Equal(3, wizard.CurrentStep);
        Assert.Empty(_reads);  // not read: added to wait
        Assert.True(wizard.Scope!.IsSecomWaiting);
        Assert.Equal("S-124", Assert.Single(wizard.Scope.Choices("Products")).Value);

        await NextAsync(wizard);
        wizard.AddCommand.Execute(null);

        var source = Assert.IsType<SecomSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(SecomIdentitySetting.Default, source.Identity);
        Assert.False(source.Sync);
    }

    [Fact]
    public async Task Changing_the_identity_probes_again_and_a_readable_service_continues_with_it()
    {
        var bridge = Import("bridge", "Bridge PC", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge");
        var organisation = Import("org", "SoundCharts organisation", "urn:mrn:mcp:org:mcc:soundcharts");
        _probe = identity => identity == organisation.Id
            ? new SecomProbeResult(SecomReachability.OpenWithCertificate, organisation.Mrn) { Identity = organisation.Mrn, IdentityId = organisation.Id }
            : identity == SecomIdentitySetting.None
                ? new SecomProbeResult(SecomReachability.NeedsCertificate, "401")
                : new SecomProbeResult(SecomReachability.CertificateRefused, "403") { Identity = bridge.Mrn, IdentityId = bridge.Id };
        var wizard = Wizard();
        await ChooseKhraAsync(wizard);

        // The default (the first import, Bridge PC) is presented and refused.
        var connection = wizard.Connection!;
        Assert.Equal("Default · Bridge PC", connection.Picker.Name);
        Assert.Equal(SecomConnectionState.CertificateRefused, connection.State);
        Assert.Equal("Choose another identity…", Assert.Single(connection.Actions).Label);
        Assert.Equal("Presented:", connection.NoteLabel);
        Assert.Contains("refused Bridge PC", connection.Body, StringComparison.Ordinal);
        Assert.Equal("Refused here", connection.Picker.Items.Single(i => i.Setting == bridge.Id).HintText);
        Assert.Equal("Refused here", connection.Picker.Items.Single(i => i.Setting == SecomIdentitySetting.Default).HintText);

        // Choosing the organisation probes again with it, and only it.
        connection.Picker.Items.Single(i => i.Setting == organisation.Id).ChooseCommand.Execute(null);
        await WaitForAsync(() => connection.HasState && connection.State == SecomConnectionState.ReadableWithCertificate);

        Assert.Equal(organisation.Id, _probes[^1].Identity);
        Assert.Equal("Continue", wizard.NextLabel);
        Assert.Null(connection.WaitNote);
        Assert.Equal(organisation.Mrn, connection.Note);

        await NextAsync(wizard);

        Assert.Equal(organisation.Id, Assert.Single(_reads));
        Assert.False(wizard.Scope!.IsSecomWaiting);
        await NextAsync(wizard);
        wizard.AddCommand.Execute(null);
        Assert.Equal(organisation.Id, ((SecomSource)_library.Collections[0].Sources[0].Definition).Identity);
    }

    [Fact]
    public async Task One_acceptable_identity_is_offered_by_name_but_never_chosen_for_the_user()
    {
        var only = Import("org", "SoundCharts organisation", "urn:mrn:mcp:org:mcc:soundcharts");
        _keys.Identities.MakeDefault(null);
        var wizard = Wizard();
        await ChooseKhraAsync(wizard);

        var connection = wizard.Connection!;
        Assert.Equal(SecomConnectionState.NeedsCertificate, connection.State);
        var use = Assert.Single(connection.Actions);
        Assert.Equal("Use SoundCharts organisation", use.Label);
        Assert.Equal(SecomIdentitySetting.Default, connection.IdentitySetting);

        use.Command.Execute(null);
        await WaitForAsync(() => _probes.Count == 2);
        Assert.Equal(only.Id, _probes[^1].Identity);
    }

    [Fact]
    public async Task Catalogues_that_are_not_secom_skip_the_connection_step()
    {
        var known = new KnownCatalogueSource("noaa", "NOAA", "NOAA", ["North America"], KnownCatalogueFormat.NoaaEnc,
            new Uri("https://noaa.test/catalog.xml"), null, KnownCatalogueCoverage.Polygons, true, true);
        var wizard = Wizard(known);
        wizard.Start(null);
        wizard.Directory.SelectedEntry = wizard.Directory.Entries.Single(e => e.Source.Id == "noaa");

        await NextAsync(wizard);

        Assert.Equal(3, wizard.CurrentStep);
        Assert.Null(wizard.Connection);
        Assert.Empty(_probes);
    }

    private SecomIdentityReference Import(string file, string name, string mrn) =>
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(file, mrn), "secret"), name, makeDefault: false);

    private AddOnlineCatalogueWizardViewModel Wizard(params KnownCatalogueSource[] sources)
    {
        var directory = new SecomIdentityDirectory(_keys.Identities, _keys.Time);
        return new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(sources.Length > 0 ? sources : [Khra]),
            () => new AddToLibraryDialogViewModel(_library, new LibraryCatalogueReaders
            {
                Secom = (_, _, identity, _) =>
                {
                    _reads.Add(identity);
                    return Task.FromResult(new SecomServiceDescription([new CatalogFacetValue("S-124", 3, 3_000)], 3, 3, false, SecomApiVersion.V2));
                },
                NoaaEnc = (_, _) => Task.FromResult(Collections.Noaa.NoaaEncProductCatalogReader.Read(LibraryTestContext.RepoFile(
                    "tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml"))),
            }),
            _ => { },
            (source, openKeys) => new SecomConnectionStepViewModel(source, directory, (uri, identity, _) =>
            {
                _probes.Add((uri, identity));
                return Task.FromResult(_probe(identity));
            }, openKeys));
    }

    private static async Task ChooseKhraAsync(AddOnlineCatalogueWizardViewModel wizard)
    {
        wizard.Start(null);
        wizard.Directory.SelectedEntry = wizard.Directory.Entries.Single(e => e.Source.Id == Khra.Id);
        await NextAsync(wizard);
    }

    private static Task NextAsync(AddOnlineCatalogueWizardViewModel wizard) =>
        ((IAsyncRelayCommand)wizard.NextCommand).ExecuteAsync(null);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
