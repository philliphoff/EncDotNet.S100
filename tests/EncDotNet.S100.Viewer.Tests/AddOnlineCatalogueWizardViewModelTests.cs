using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class AddOnlineCatalogueWizardViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly List<Uri> _loads = [];
    private Func<Uri, NoaaEncProductCatalog> _read;

    public AddOnlineCatalogueWizardViewModelTests()
    {
        // Not initialized: no background indexing (and no network) runs.
        _library = _context.CreateService();
        _read = _ => NoaaEncProductCatalogReader.Read(LibraryTestContext.RepoFile(
            "tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml"));
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private static KnownCatalogueSource Source(string id) =>
        new(id, id.ToUpperInvariant(), "Provider", ["North America", "United States"], KnownCatalogueFormat.NoaaEnc,
            new Uri($"https://{id}.test/catalog.xml"), null, KnownCatalogueCoverage.Polygons, Editions: true, Sizes: true);

    private AddOnlineCatalogueWizardViewModel CreateWizard(params KnownCatalogueSource[] sources) =>
        new(
            new CatalogueDirectoryDialogViewModel(sources),
            () => new AddToLibraryDialogViewModel(_library, (uri, _) =>
            {
                _loads.Add(uri);
                return Task.FromResult(_read(uri));
            }));

    private static Task NextAsync(AddOnlineCatalogueWizardViewModel wizard) =>
        ((IAsyncRelayCommand)wizard.NextCommand).ExecuteAsync(null);

    private static CatalogueEntryViewModel Entry(AddOnlineCatalogueWizardViewModel wizard, string id) =>
        wizard.Directory.Entries.Single(e => e.Source.Id == id);

    [Fact]
    public async Task Back_keeps_the_catalogue_and_returning_does_not_reload()
    {
        var wizard = CreateWizard(Source("a"), Source("b"));
        wizard.Start(null);
        wizard.Directory.SelectedEntry = Entry(wizard, "b");

        await NextAsync(wizard);

        Assert.Equal(2, wizard.CurrentStep);
        Assert.Single(_loads);
        Assert.Equal("B", wizard.Steps[0].Value);
        Assert.True(wizard.Steps[0].IsDone);

        wizard.BackCommand.Execute(null);

        Assert.Equal(1, wizard.CurrentStep);
        Assert.Equal("b", wizard.Directory.SelectedEntry!.Source.Id);
        Assert.False(wizard.BackCommand.CanExecute(null));

        await NextAsync(wizard);

        Assert.Equal(2, wizard.CurrentStep);
        Assert.Single(_loads);
    }

    [Fact]
    public async Task Each_catalogue_keeps_its_own_ticks()
    {
        var wizard = CreateWizard(Source("a"), Source("b"));
        wizard.Start(null);
        await NextAsync(wizard);
        var first = wizard.Scope!;
        first.States.Single(s => s.Value == "AK").IsSelected = true;

        wizard.BackCommand.Execute(null);
        wizard.Directory.SelectedEntry = Entry(wizard, "b");
        await NextAsync(wizard);

        Assert.NotSame(first, wizard.Scope);
        Assert.True(wizard.Scope!.IncludeAll);
        Assert.Equal("Everything", wizard.Steps[1].Value);

        wizard.ChangeCatalogueCommand.Execute(null);
        wizard.Directory.SelectedEntry = Entry(wizard, "a");
        await NextAsync(wizard);

        Assert.Same(first, wizard.Scope);
        Assert.True(first.States.Single(s => s.Value == "AK").IsSelected);
        Assert.Equal("Alaska", wizard.Steps[1].Value);
        Assert.Equal(2, _loads.Count);
    }

    [Fact]
    public async Task Next_is_disabled_on_the_scope_step_until_something_is_included()
    {
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);
        await NextAsync(wizard);
        Assert.True(wizard.NextCommand.CanExecute(null));

        wizard.Scope!.OnlySelected = true;

        Assert.False(wizard.NextCommand.CanExecute(null));
        Assert.Equal("Select at least one", wizard.FooterHint);

        wizard.Scope.States[0].IsSelected = true;

        Assert.True(wizard.NextCommand.CanExecute(null));
        Assert.Null(wizard.FooterHint);
    }

    [Fact]
    public async Task Only_completed_steps_can_be_clicked()
    {
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);
        Assert.Equal("Choose a catalogue first", wizard.Steps[1].Value);
        Assert.Equal("Collection", wizard.Steps[2].Value);
        Assert.All(wizard.Steps, s => Assert.False(s.GoToCommand.CanExecute(null)));

        await NextAsync(wizard);
        await NextAsync(wizard);

        Assert.Equal(3, wizard.CurrentStep);
        Assert.True(wizard.IsLastStep);
        Assert.Equal("New collection", wizard.Steps[2].Value);
        Assert.True(wizard.Steps[0].GoToCommand.CanExecute(null));
        Assert.True(wizard.Steps[1].GoToCommand.CanExecute(null));
        Assert.False(wizard.Steps[2].GoToCommand.CanExecute(null));

        wizard.Steps[0].GoToCommand.Execute(null);

        Assert.Equal(1, wizard.CurrentStep);
        Assert.True(wizard.Steps[1].IsUpcoming);
        Assert.False(wizard.Steps[1].GoToCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_load_failure_keeps_back_and_try_again_reloads()
    {
        var fail = true;
        var read = _read;
        _read = uri => fail ? throw new HttpRequestException("offline") : read(uri);
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);

        await NextAsync(wizard);

        Assert.True(wizard.Scope!.HasLoadError);
        Assert.False(wizard.NextCommand.CanExecute(null));
        Assert.True(wizard.BackCommand.CanExecute(null));
        Assert.True(wizard.TryAgainCommand.CanExecute(null));

        fail = false;
        await ((IAsyncRelayCommand)wizard.TryAgainCommand).ExecuteAsync(null);

        Assert.False(wizard.Scope.HasLoadError);
        Assert.True(wizard.NextCommand.CanExecute(null));
        Assert.Equal(2, _loads.Count);
    }

    [Fact]
    public async Task The_shared_feed_entry_starts_at_the_scope_step()
    {
        var wizard = CreateWizard(Source("a"));
        var feed = KnownCatalogueSources.FromUrl(new Uri("https://machine.test/catalog.xml"), KnownCatalogueFormat.NoaaEnc, "Shared");

        await wizard.StartAtIncludeAsync(feed, null);

        Assert.Equal(2, wizard.CurrentStep);
        Assert.True(wizard.Steps[0].IsDone);
        Assert.Equal("Shared", wizard.Steps[0].Value);
        Assert.Equal(feed.CatalogUri, Assert.Single(_loads));

        wizard.BackCommand.Execute(null);

        Assert.Equal(feed.Id, wizard.Directory.SelectedEntry!.Source.Id);
    }

    [Fact]
    public async Task Add_adds_the_source_and_closes()
    {
        var wizard = CreateWizard(Source("a"));
        bool? closed = null;
        wizard.Closed += (_, added) => closed = added;
        wizard.Start(null);
        await NextAsync(wizard);
        await NextAsync(wizard);

        Assert.True(wizard.AddCommand.CanExecute(null));
        wizard.AddCommand.Execute(null);

        Assert.True(closed);
        var collection = Assert.Single(_library.Collections);
        Assert.Equal("A", collection.Definition.Name);
        Assert.True(Assert.IsType<NoaaEncFeedSource>(Assert.Single(collection.Sources).Definition).Filter.IsUnscoped);
    }

    [Fact]
    public void Cancel_closes_without_adding()
    {
        var wizard = CreateWizard(Source("a"));
        bool? closed = null;
        wizard.Closed += (_, added) => closed = added;
        wizard.Start(null);

        wizard.CancelCommand.Execute(null);

        Assert.False(closed);
        Assert.Empty(_library.Collections);
    }
}
