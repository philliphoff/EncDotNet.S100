using Avalonia.Controls;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services.Secom;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tests.Keys;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Secom;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests.Secom;

/// <summary>
/// The per-source identity views (#883) load and lay out against their view
/// models: the wizard's Connection step, the Identity picker's menu, and a
/// SECOM source's Connection details in the Library. Set
/// <c>S100_VIEW_CAPTURE_DIR</c> to save each frame as a PNG for review.
/// </summary>
public sealed class SecomIdentityViewTests
{
    private static readonly KnownCatalogueSource Khra = KnownCatalogueSources.FromRegistry(new SecomRegistryService(
        "urn:mrn:mcp:service:mcc:khra:s-124", "KHRA S-124 navigational warnings", "1.0", SecomRegistryStatus.Released, "S-124",
        new Uri("https://secom.khra.example/api/secom/"), "urn:mrn:mcp:org:mcc:khra", null, null, null));

    [AvaloniaFact]
    public async Task The_connection_step_shows_the_service_the_picker_and_the_status_block()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        using var keys = new KeysTestSupport();
        var bridge = keys.Identities.Import(keys.Identities.Load(keys.IdentityFile("bridge", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge", 12), "secret"), "Bridge PC", makeDefault: false);
        keys.Identities.Import(keys.Identities.Load(keys.IdentityFile("org", "urn:mrn:mcp:org:mcc:soundcharts"), "secret"), "SoundCharts organisation", makeDefault: false);
        var directory = new SecomIdentityDirectory(keys.Identities, keys.Time);
        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel([Khra]),
            () => new AddToLibraryDialogViewModel(library, new LibraryCatalogueReaders
            {
                Secom = (_, _, _, _) => throw new HttpRequestException("403"),
            }),
            _ => { },
            (source, openKeys) => new SecomConnectionStepViewModel(source, directory, (_, _, _) => Task.FromResult(
                new SecomProbeResult(SecomReachability.CertificateRefused, "403") { Identity = bridge.Mrn, IdentityId = bridge.Id }), openKeys));
        wizard.Start(null);
        wizard.Directory.SelectedEntry = wizard.Directory.Entries.Single();
        using var host = ViewHost.Show(new AddOnlineCatalogueWizardView { DataContext = wizard }, width: 640, height: 640);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)wizard.NextCommand).ExecuteAsync(null);
        host.Settle();

        Assert.Equal(2, wizard.CurrentStep);
        Assert.True(host.IsShown("Secom.IdentityPicker"));
        Assert.True(host.IsShown("Wizard.Connection.Status"));
        Assert.True(host.IsShown("Wizard.Connection.WaitNote"));
        Assert.Equal("Add and wait", host.Find<Button>("Wizard.Next").Content);
        Save(host, "connection-refused");

        wizard.Connection!.Picker.IsOpen = true;
        host.Settle();
        Save(host, "connection-picker");
        wizard.Connection.Picker.IsOpen = false;

        // Add and wait: step 3 shows the waiting scope (it isn't read), not a blank step.
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)wizard.NextCommand).ExecuteAsync(null);
        host.Settle();
        Assert.Equal(3, wizard.CurrentStep);
        Assert.True(wizard.Scope!.IsSecomWaiting);
        Assert.True(host.IsShown("CatalogueScope.Everything"));
        Save(host, "include-waiting");
    }

    [AvaloniaFact]
    public async Task A_selected_secom_source_shows_its_connection_in_the_details()
    {
        using var context = new LibraryTestContext();
        using var keys = new KeysTestSupport();
        var bridge = keys.Identities.Import(keys.Identities.Load(keys.IdentityFile("bridge", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge"), "secret"), "Bridge PC", makeDefault: false);
        using var library = context.CreateService(new CollectionIndexer([new RefusingIndexer(bridge.Id, bridge.Mrn!)]));
        library.Initialize();
        library.AddCollection("SECOM", [new SecomSource(Guid.NewGuid(), "KHRA S-124", new Uri("https://secom.khra.example/api/secom/"), SecomFilter.All) { Identity = bridge.Id }]);
        await library.WhenIdle();
        using var panel = new LibraryPanelViewModel(library, new RecordingLibraryImporter(), new FakeLibraryLoader(), new FakeLibraryDownloader(), action => action());
        panel.Identities = new SecomIdentityDirectory(keys.Identities, keys.Time);
        panel.Sync();
        panel.SelectedNode = panel.Nodes.Single().Children.Single();
        using var host = ViewHost.Show(new LibraryPanelView { DataContext = panel }, width: 420, height: 900);

        Assert.True(panel.ShowsSecomDetails);
        Assert.True(host.IsShown("Library.SourceDetails"));
        Assert.True(host.IsShown("Library.SourceDetails.Presentation"));
        Assert.True(host.IsShown("Library.SourceDetails.Action"));
        Assert.True(host.IsShown("Library.Tree.SecomFix"));
        Save(host, "library-details-refused");
    }

    [AvaloniaFact]
    public async Task The_identities_tab_shows_the_default_who_uses_each_and_the_remove_confirm()
    {
        using var context = new LibraryTestContext();
        using var keys = new KeysTestSupport();
        using var library = context.CreateService(new CollectionIndexer([new RefusingIndexer("sc-ident:00000000", "urn:mrn:none")]));
        library.Initialize();
        keys.Identities.Library = library;
        var bridge = keys.Identities.Import(keys.Identities.Load(keys.IdentityFile("bridge", "urn:mrn:mcp:vessel:mcc:soundcharts:bridge", 12), "secret"), "Bridge PC", makeDefault: false);
        var org = keys.Identities.Import(keys.Identities.Load(keys.IdentityFile("org", "urn:mrn:mcp:org:mcc:soundcharts"), "secret"), "SoundCharts organisation", makeDefault: false);
        library.AddCollection("SECOM",
        [
            new SecomSource(Guid.NewGuid(), "KHOA S-101", new Uri("https://khoa.example/api/secom/"), SecomFilter.All),
            new SecomSource(Guid.NewGuid(), "KHOA S-102", new Uri("https://khoa2.example/api/secom/"), SecomFilter.All),
            new SecomSource(Guid.NewGuid(), "KHRA S-124", new Uri("https://khra.example/api/secom/"), SecomFilter.All) { Identity = org.Id },
        ]);
        await library.WhenIdle();
        using var page = keys.Page();
        using var host = ViewHost.Show(new EncDotNet.S100.Viewer.Views.Settings.KeysAndCertificatesSettingsPage { DataContext = page }, width: 760, height: 820);

        Assert.True(host.IsShown("Keys.Row.UsedBy"));
        Assert.True(host.IsShown("Keys.Row.MakeDefault"));
        Save(host, "identities-tab");

        var dialog = new EncDotNet.S100.Viewer.ViewModels.Keys.RemoveIdentityDialogViewModel(keys.Identities, bridge, keys.Time);
        using var confirm = ViewHost.Show(new RemoveIdentityDialogView { DataContext = dialog }, width: 440, height: 420);
        Assert.True(confirm.IsShown("RemoveIdentity.NewDefault"));
        Assert.True(confirm.IsShown("RemoveIdentity.Affected"));
        Save(confirm, "remove-identity");
    }

    private static void Save(ViewHost host, string name)
    {
        var frame = host.CaptureFrame();
        if (Environment.GetEnvironmentVariable("S100_VIEW_CAPTURE_DIR") is { Length: > 0 } dir)
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), frame);
    }

    /// <summary>A SECOM indexer whose service refuses the identity.</summary>
    private sealed class RefusingIndexer(string id, string mrn) : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is SecomSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [],
                [new IndexDiagnostic(IndexDiagnosticSeverity.Error, "403") { Code = SecomDiagnosticCodes.CertificateRefused }])
            {
                Presentation = new SecomPresentation(id, id, mrn, true, [], false, SecomPresentationOutcome.Refused, new DateTimeOffset(2026, 10, 11, 9, 12, 0, TimeSpan.Zero))
                {
                    Code = SecomDiagnosticCodes.CertificateRefused,
                    Refusals = [new SecomRefusal(id, DateTimeOffset.UtcNow)],
                },
            });
    }
}
