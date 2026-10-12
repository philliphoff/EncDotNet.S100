using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Tests.Keys;

/// <summary>
/// The Identities tab with SECOM sources (#883 E, F): the default, which
/// sources use each identity, what removing one does, and setting waiting
/// sources to a newly imported identity.
/// </summary>
public sealed class IdentitiesTabSourcesTests : IDisposable
{
    private readonly KeysTestSupport _keys = new();
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly WaitingIndexer _indexer = new();

    public IdentitiesTabSourcesTests()
    {
        _library = _context.CreateService(new CollectionIndexer([_indexer]));
        _library.Initialize();
        _keys.Identities.Library = _library;
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
        _keys.Dispose();
    }

    [Fact]
    public async Task Rows_say_which_sources_use_each_identity_and_how()
    {
        var bridge = Import("bridge", "Bridge PC");
        var org = Import("org", "SoundCharts organisation");
        await AddAsync(("KHOA S-101", SecomIdentitySetting.Default), ("KHOA S-102", SecomIdentitySetting.Default), ("KHRA S-124", org.Id), ("CCG S-124", "none"));
        using var page = _keys.Page();

        var bridgeRow = page.IdentityRows.Single(r => r.Id == bridge.Id);
        Assert.Contains(bridgeRow.Badges, b => b.Text == "DEFAULT");
        Assert.Equal("Used by 2 sources", bridgeRow.UsedByLabel);
        Assert.Equal([("KHOA S-101", "via default"), ("KHOA S-102", "via default")], bridgeRow.UsedBy.Select(u => (u.Source, u.How)));
        var orgRow = page.IdentityRows.Single(r => r.Id == org.Id);
        Assert.Equal("Used by 1 source", orgRow.UsedByLabel);
        Assert.Equal(("KHRA S-124", "chosen"), (orgRow.UsedBy[0].Source, orgRow.UsedBy[0].How));
        Assert.Equal(["Details", "MakeDefault", "Replace", "Remove"], orgRow.Actions.Select(a => a.Id));

        // Make default moves the "via default" sources to it.
        orgRow.Actions.Single(a => a.Id == "MakeDefault").Command.Execute(null);

        Assert.Equal(org.Id, _keys.Identities.DefaultId);
        orgRow = page.IdentityRows.Single(r => r.Id == org.Id);
        Assert.Contains(orgRow.Badges, b => b.Text == "DEFAULT");
        Assert.Equal("Used by 3 sources", orgRow.UsedByLabel);
        Assert.Equal("Not used by any source", page.IdentityRows.Single(r => r.Id == bridge.Id).UsedByLabel);
    }

    [Fact]
    public async Task Removing_the_default_lists_each_source_and_switches_them_as_listed()
    {
        var bridge = Import("bridge", "Bridge PC");
        var org = Import("org", "SoundCharts organisation");
        await AddAsync(("KHOA S-101", SecomIdentitySetting.Default), ("UKHO test", bridge.Id), ("KHRA S-124", org.Id));
        using var page = _keys.Page();

        page.IdentityRows.Single(r => r.Id == bridge.Id).Actions.Single(a => a.Id == "Remove").Command.Execute(null);
        var dialog = Assert.IsType<RemoveIdentityDialogViewModel>(Assert.Single(_keys.Dialogs.Shown));

        Assert.Equal("2 sources use this identity, directly or as the default. Each switches as listed. It's your default, so choose a new default first.", dialog.Body);
        Assert.Equal(["SoundCharts organisation", "None"], dialog.NewDefaults.Select(c => c.Label));
        Assert.True(dialog.NewDefaults[0].IsSelected);
        Assert.Equal(
            [("KHOA S-101", "Default · switches to SoundCharts organisation"), ("UKHO test", "switches to None")],
            dialog.Effects.Select(e => (e.Source, e.Outcome)));

        dialog.NewDefaults[1].SelectCommand.Execute(null);
        Assert.Equal("Default · switches to None", dialog.Effects[0].Outcome);
        dialog.NewDefaults[0].SelectCommand.Execute(null);

        dialog.RemoveCommand.Execute(null);

        Assert.Equal(org.Id, _keys.Identities.DefaultId);
        Assert.Equal(1, _keys.Keys.Count);
        Assert.Equal(["default", "none", org.Id], _library.Collections.Single().Sources.Select(s => ((SecomSource)s.Definition).Identity));
    }

    [Fact]
    public async Task Importing_offers_the_waiting_sources_ticked_and_sets_only_those_kept()
    {
        Import("org", "SoundCharts organisation");
        _keys.Identities.MakeDefault(null);
        await AddAsync(("KHRA S-124", "none"), ("KHOA S-128", "none"), ("CCG S-124", "none"));
        _indexer.Waiting.UnionWith(["KHRA S-124", "KHOA S-128"]);
        _library.Refresh();
        await _library.WhenIdle();
        var dialog = new ImportIdentityDialogViewModel(_keys.Identities, () => Task.FromResult<string?>(_keys.IdentityFile("bridge")), time: _keys.Time);
        Assert.True(await dialog.PickFileAsync());
        dialog.Password = "secret";
        dialog.Primary();
        dialog.Name = "Bridge PC";
        dialog.Primary();

        Assert.True(dialog.IsUseStep);
        Assert.Equal("2 sources need a certificate from an issuer Bridge PC chains to. Set them to Bridge PC? Other sources keep their setting.", dialog.WaitingSourcesText);
        Assert.Equal([("KHRA S-124", "Needs a certificate"), ("KHOA S-128", "Needs a certificate")], dialog.WaitingSources.Select(c => (c.Name, c.Why)));
        Assert.All(dialog.WaitingSources, c => Assert.True(c.IsChecked));

        dialog.WaitingSources[1].IsChecked = false;
        dialog.Primary();

        var bridge = _keys.Identities.Identities.Single(r => r.DisplayName == "Bridge PC");
        Assert.Equal([bridge.Id, "none", "none"], _library.Collections.Single().Sources.Select(s => ((SecomSource)s.Definition).Identity));
    }

    private SecomIdentityReference Import(string file, string name) =>
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(file, $"urn:mrn:mcp:device:mcc:soundcharts:{file}"), "secret"), name, makeDefault: false);

    private async Task AddAsync(params (string Name, string Identity)[] sources)
    {
        _library.AddCollection("SECOM", [.. sources.Select(s =>
            new SecomSource(Guid.NewGuid(), s.Name, new Uri($"https://{s.Name.Replace(' ', '-').ToLowerInvariant()}.example/api/secom/"), SecomFilter.All)
            {
                Identity = s.Identity,
            })]);
        await _library.WhenIdle();
    }

    /// <summary>Indexes SECOM sources: those named in <see cref="Waiting"/> need a certificate; the rest read.</summary>
    private sealed class WaitingIndexer : ICollectionSourceIndexer
    {
        public HashSet<string> Waiting { get; } = [];

        public bool CanIndex(CollectionSource source) => source is SecomSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            var waiting = Waiting.Contains(source.DisplayName ?? string.Empty);
            return ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [],
                waiting ? [new IndexDiagnostic(IndexDiagnosticSeverity.Error, "401") { Code = SecomDiagnosticCodes.NeedsCertificate }] : [])
            {
                Presentation = waiting
                    ? new SecomPresentation("none", null, null, true, [], false, SecomPresentationOutcome.NotPresented, DateTimeOffset.UtcNow) { Code = SecomDiagnosticCodes.NeedsCertificate }
                    : null,
            });
        }
    }
}
