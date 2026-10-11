using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services.Secom;
using EncDotNet.S100.Viewer.Tests.Keys;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Keys;
using EncDotNet.S100.Viewer.ViewModels.Secom;

namespace EncDotNet.S100.Viewer.Tests.Secom;

/// <summary>
/// SECOM sources in the Library with an identity each (#883 B, D): the
/// picker, the row's sub-line and link, and the source's Connection details.
/// </summary>
public sealed class SecomLibraryIdentityTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly KeysTestSupport _keys = new();
    private readonly CannedSecomIndexer _indexer = new();
    private readonly CollectionLibrary _library;
    private readonly SecomIdentityDirectory _directory;
    private readonly List<KeysTab> _opened = [];

    public SecomLibraryIdentityTests()
    {
        _library = _context.CreateService(new CollectionIndexer([_indexer]));
        _library.Initialize();
        _directory = new SecomIdentityDirectory(_keys.Identities, _keys.Time);
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
        _keys.Dispose();
    }

    [Fact]
    public void With_no_identities_the_picker_offers_only_none_and_the_way_to_import()
    {
        var picker = new IdentityPickerViewModel(_directory, () => _opened.Add(KeysTab.Identities));

        var only = Assert.Single(picker.Items);
        Assert.Equal(("none", "None · anonymous", "No identities yet"), (only.Setting, only.Name, only.Sub));
        Assert.Equal("None · anonymous", picker.Name);
        Assert.Equal("No certificate is presented", picker.Sub);
        Assert.True(picker.IsAnonymous);

        picker.ManageCommand.Execute(null);
        Assert.Equal([KeysTab.Identities], _opened);
    }

    [Fact]
    public void The_picker_lists_default_none_and_each_identity_with_hints_when_the_service_asks()
    {
        var bridge = Import("bridge", "Bridge PC", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge", daysLeft: 12);
        var dev = Import("dev", "Dev laptop", "urn:mrn:mcp:device:mcc-test:soundcharts:dev");
        var picker = new IdentityPickerViewModel(_directory) { Setting = "default" };

        Assert.Equal(["default", "none", bridge.Id, dev.Id], picker.Items.Select(i => i.Setting));
        Assert.Equal("Default · Bridge PC", picker.Name);
        Assert.Equal($"Vessel · MCC · ends in 12 days · {bridge.Id}", picker.Sub);
        Assert.Equal("Follows the default in Keys & certificates", picker.Items[0].Sub);
        Assert.Equal("Test · MCC Test", picker.Items[3].Sub);
        Assert.True(picker.Items[0].IsSelected);
        Assert.All(picker.Items, i => Assert.Null(i.HintText));

        picker.Hints = (id, _) => id == bridge.Id ? SecomIdentityHint.RefusedHere : SecomIdentityHint.IssuerNotAccepted;

        Assert.Equal("Refused here", picker.Items[0].HintText);  // Default takes its identity's hint
        Assert.Equal("Refused here", picker.Items[2].HintText);
        Assert.Equal("Issuer not accepted", picker.Items[3].HintText);
        Assert.False(picker.Items[3].ChooseCommand.CanExecute(null));
        Assert.True(picker.Items[2].ChooseCommand.CanExecute(null));

        string? chosen = null;
        picker.Chosen += (_, setting) => chosen = setting;
        picker.Items[1].ChooseCommand.Execute(null);
        Assert.Equal("none", chosen);
        Assert.Equal("None · anonymous", picker.Name);
    }

    [Fact]
    public async Task A_refused_row_names_its_identity_and_its_link_opens_the_sources_picker()
    {
        var bridge = Import("bridge", "Bridge PC", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge");
        var organisation = Import("org", "SoundCharts organisation", "urn:mrn:mcp:org:mcc:soundcharts");
        _indexer.Refuse(bridge.Id, bridge.Mrn!);
        var source = new SecomSource(Guid.NewGuid(), "KHRA S-124", new Uri("https://secom.khra.example/api/secom/"), SecomFilter.All) { Identity = bridge.Id };
        _library.AddCollection("SECOM", [source]);
        await _library.WhenIdle();
        using var panel = Panel();

        var row = panel.Nodes.Single().Children.Single();
        Assert.Equal("SECOM · Bridge PC · no datasets yet", row.SecondaryName);
        Assert.Equal("Your certificate was refused", row.StatusLine);
        Assert.Equal("Choose another identity…", row.SecomFixText);

        panel.SelectedNode = panel.Nodes.Single();
        panel.OpenSecomFixCommand.Execute(row);

        var details = panel.SecomDetails!;
        Assert.Same(row, panel.SelectedNode);
        Assert.True(panel.ShowsSecomDetails);
        Assert.True(details.Picker.IsOpen);
        Assert.StartsWith("Presented Bridge PC · refused ", details.PresentationLine, StringComparison.Ordinal);
        Assert.True(details.PresentationIsDestructive);
        Assert.Equal(["Certificate refused", "no datasets yet", $"identity: {bridge.Id}"], details.Fields.Select(f => f.Value));
        Assert.Equal("Choose another identity…", details.ActionText);
        Assert.Equal("Refused here", details.Picker.Items.Single(i => i.Setting == bridge.Id).HintText);

        // Choosing another identity saves the setting; only this source is read again, and the details say so.
        _indexer.Accept(organisation.Id, organisation.Mrn!);
        details.Picker.Items.Single(i => i.Setting == organisation.Id).ChooseCommand.Execute(null);
        await _library.WhenIdle();
        panel.Sync();

        Assert.Equal(organisation.Id, ((SecomSource)_library.Collections[0].Sources[0].Definition).Identity);
        Assert.Equal("Presented SoundCharts organisation · accepted", details.PresentationLine);
        Assert.Equal("Re-read with SoundCharts organisation · 1 dataset", details.RereadNote);
        Assert.Equal("SECOM · SoundCharts organisation · 1 dataset", panel.Nodes.Single().Children.Single().SecondaryName);
        Assert.Null(details.ActionText);
    }

    [Fact]
    public async Task A_waiting_source_with_no_identities_offers_to_set_one_up()
    {
        _indexer.NeedCertificate();
        _library.AddCollection("SECOM", [new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.khra.example/api/secom/"), SecomFilter.All)]);
        await _library.WhenIdle();
        using var panel = Panel();

        var row = panel.Nodes.Single();
        Assert.Equal("SECOM · Default (not set) · no datasets yet", row.SecondaryName);
        Assert.Equal("Needs a certificate", row.StatusLine);
        Assert.Equal("Set up an identity…", row.SecomFixText);
        panel.OpenSecomFixCommand.Execute(row);
        Assert.Equal([KeysTab.Identities], _opened);

        Assert.Equal("Service asked for a certificate · none presented", panel.SecomDetails!.PresentationLine);
        Assert.Equal("identity: default", panel.SecomDetails.Fields.Single(f => f.IsMono).Value);
    }

    [Fact]
    public async Task A_readable_source_whose_identity_ends_soon_says_so()
    {
        var bridge = Import("bridge", "Bridge PC", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge", daysLeft: 12);
        _indexer.Accept(bridge.Id, bridge.Mrn!);
        _library.AddCollection("SECOM", [new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.khoa.example/api/secom/"), SecomFilter.All)]);
        await _library.WhenIdle();
        using var panel = Panel();

        var row = panel.Nodes.Single();
        Assert.Equal("SECOM · Default (Bridge PC) · 1 dataset", row.SecondaryName);
        Assert.Equal("Certificate ends in 12 days", row.StatusLine);
        Assert.Equal("Manage identities…", row.SecomFixText);
        Assert.Null(panel.SecomDetails!.ActionText);  // the details don't repeat "Manage identities…"
    }

    private LibraryPanelViewModel Panel()
    {
        var panel = new LibraryPanelViewModel(_library, new RecordingLibraryImporter(), new FakeLibraryLoader(), new FakeLibraryDownloader(), action => action())
        {
            OpenKeys = _opened.Add,
        };
        panel.Identities = _directory;
        panel.Sync();
        return panel;
    }

    private SecomIdentityReference Import(string file, string name, string mrn, int daysLeft = 300) =>
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(file, mrn, daysLeft), "secret"), name, makeDefault: false);

    /// <summary>Indexes a SECOM source as told: refusing, accepting or needing a certificate.</summary>
    private sealed class CannedSecomIndexer : ICollectionSourceIndexer
    {
        private Func<SecomSource, SourceIndex>? _index;

        public void Refuse(string id, string mrn) => _index = source => Index(source, [], SecomDiagnosticCodes.CertificateRefused,
            new SecomPresentation(source.Identity, id, mrn, true, [], false, SecomPresentationOutcome.Refused, DateTimeOffset.UtcNow)
            {
                Code = SecomDiagnosticCodes.CertificateRefused,
                Refusals = [new SecomRefusal(id, DateTimeOffset.UtcNow)],
            });

        public void Accept(string id, string mrn)
        {
            var refused = _index;
            _index = source => source.Identity == id || source.Identity == SecomIdentitySetting.Default
                ? Index(source, [Item()], null, new SecomPresentation(source.Identity, id, mrn, true, [], false, SecomPresentationOutcome.Accepted, DateTimeOffset.UtcNow))
                : refused!(source);
        }

        public void NeedCertificate() => _index = source => Index(source, [], SecomDiagnosticCodes.NeedsCertificate,
            new SecomPresentation(source.Identity, null, null, true, [], false, SecomPresentationOutcome.NotPresented, DateTimeOffset.UtcNow)
            {
                Code = SecomDiagnosticCodes.NeedsCertificate,
            });

        public bool CanIndex(CollectionSource source) => source is SecomSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_index!((SecomSource)source));

        private static SourceIndex Index(SecomSource source, IReadOnlyList<CollectionItem> items, string? code, SecomPresentation presentation) =>
            new(source.Id, DateTimeOffset.UtcNow, null, items,
                code is null ? [] : [new IndexDiagnostic(IndexDiagnosticSeverity.Error, "refused") { Code = code }])
            {
                Presentation = presentation,
            };

        private static CollectionItem Item() => new()
        {
            Key = "ref-1",
            ProductSpec = "S-124",
            Name = "NW-1",
            Location = new RemoteItemLocation(new Uri("https://secom.example/v2/object?dataReference=ref-1")),
        };
    }
}
