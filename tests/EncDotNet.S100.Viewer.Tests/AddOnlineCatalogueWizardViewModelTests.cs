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
            () => new AddToLibraryDialogViewModel(_library, new LibraryCatalogueReaders
            {
                NoaaEnc = (uri, _) =>
                {
                    _loads.Add(uri);
                    return Task.FromResult(_read(uri));
                },
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

        Assert.Equal(2, wizard.CurrentStep);  // a catalogue that isn't SECOM has no Connection step
        Assert.Single(_loads);
        Assert.Equal("B", wizard.Steps[0].Value);
        Assert.True(wizard.Steps[0].IsDone);

        wizard.BackCommand.Execute(null);

        Assert.Equal(1, wizard.CurrentStep);
        Assert.Equal("b", wizard.Directory.SelectedEntry!.Source.Id);
        Assert.False(wizard.BackCommand.CanExecute(null));

        await NextAsync(wizard);

        Assert.Equal(2, wizard.CurrentStep);  // a catalogue that isn't SECOM has no Connection step
        Assert.Single(_loads);
    }

    [Fact]
    public async Task Each_catalogue_keeps_its_own_ticks()
    {
        var wizard = CreateWizard(Source("a"), Source("b"));
        wizard.Start(null);
        await NextAsync(wizard);
        var first = wizard.Scope!;
        first.Choices("States").Single(s => s.Value == "AK").IsSelected = true;

        wizard.BackCommand.Execute(null);
        wizard.Directory.SelectedEntry = Entry(wizard, "b");
        await NextAsync(wizard);

        Assert.NotSame(first, wizard.Scope);
        Assert.True(wizard.Scope!.IncludeAll);
        Assert.Equal(string.Empty, wizard.Steps[1].Value);  // the current step says nothing yet

        await NextAsync(wizard);
        Assert.Equal("Everything", wizard.Steps[1].Value);

        wizard.ChangeCatalogueCommand.Execute(null);
        wizard.Directory.SelectedEntry = Entry(wizard, "a");
        await NextAsync(wizard);

        Assert.Same(first, wizard.Scope);
        Assert.True(first.Choices("States").Single(s => s.Value == "AK").IsSelected);
        await NextAsync(wizard);
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

        wizard.Scope.Choices("States")[0].IsSelected = true;

        Assert.True(wizard.NextCommand.CanExecute(null));
        Assert.Null(wizard.FooterHint);
    }

    [Fact]
    public async Task Only_completed_steps_can_be_clicked_and_say_what_was_chosen()
    {
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);
        Assert.Equal(["Source", "What to include", "Add to"], wizard.Steps.Select(s => s.Label));
        Assert.All(wizard.Steps, s => Assert.Equal(string.Empty, s.Value));  // no subtitles on current or upcoming steps
        Assert.All(wizard.Steps, s => Assert.False(s.GoToCommand.CanExecute(null)));

        await NextAsync(wizard);
        await NextAsync(wizard);

        Assert.Equal(3, wizard.CurrentStep);
        Assert.Equal(3, wizard.StepCount);
        Assert.True(wizard.IsLastStep);
        Assert.Equal(["A", "Everything", ""], wizard.Steps.Select(s => s.Value));
        Assert.True(wizard.Steps[0].GoToCommand.CanExecute(null));
        Assert.True(wizard.Steps[1].GoToCommand.CanExecute(null));
        Assert.False(wizard.Steps[2].GoToCommand.CanExecute(null));

        wizard.BackCommand.Execute(null);
        Assert.Equal(2, wizard.CurrentStep);
        Assert.Equal(["A", "", ""], wizard.Steps.Select(s => s.Value));
        wizard.BackCommand.Execute(null);
        Assert.Equal(1, wizard.CurrentStep);
        Assert.True(wizard.Steps[1].IsUpcoming);
        Assert.False(wizard.Steps[1].GoToCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_done_step_goes_back_by_its_position()
    {
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);
        await NextAsync(wizard);
        await NextAsync(wizard);

        wizard.Steps[1].GoToCommand.Execute(null);

        Assert.True(wizard.IsIncludeStep);
        Assert.Equal(2, wizard.CurrentStep);
        Assert.Equal("Step 2 of 3", wizard.StepOfText);

        wizard.Steps[0].GoToCommand.Execute(null);

        Assert.True(wizard.IsCatalogueStep);
    }

    [Fact]
    public void The_first_step_shows_the_selection_in_the_footer_and_has_no_back()
    {
        var wizard = CreateWizard(Source("a"), Source("b"));
        wizard.Start(null);
        wizard.Directory.SelectedEntry = Entry(wizard, "b");

        Assert.False(wizard.BackCommand.CanExecute(null));
        Assert.True(wizard.HasSelectionText);
        Assert.Equal($"B · {Entry(wizard, "b").FormatText}", wizard.SelectionText);
    }

    [Fact]
    public async Task The_footer_drops_the_selection_after_the_first_step()
    {
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);
        await NextAsync(wizard);

        Assert.Null(wizard.SelectionText);
        Assert.False(wizard.HasSelectionText);
        Assert.True(wizard.BackCommand.CanExecute(null));
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
        Assert.True(wizard.IsIncludeStep);
        Assert.Equal(3, wizard.StepCount);
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
    public async Task A_single_download_skips_what_to_include()
    {
        var read = _read;
        _read = uri => read(uri) is var full ? full with { Cells = [full.Cells[0]] } : null!;
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);

        await NextAsync(wizard);

        Assert.True(wizard.IsAddToStep);
        Assert.Equal(3, wizard.CurrentStep);
        Assert.True(wizard.Steps[1].IsDone);
        Assert.Equal(wizard.Scope!.ScopeDescription, wizard.Steps[1].Value);
        Assert.True(wizard.AddCommand.CanExecute(null));

        // What to include is still there to read, and Next comes back.
        wizard.BackCommand.Execute(null);
        Assert.True(wizard.IsIncludeStep);
        Assert.True(wizard.Scope.IsSingleEntry);
        await NextAsync(wizard);
        Assert.True(wizard.IsAddToStep);
        Assert.Single(_loads);
    }

    [Fact]
    public async Task Next_reads_the_catalogue_on_the_source_step_and_waits()
    {
        var gate = new TaskCompletionSource<NoaaEncProductCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel([Source("a")]),
            () => new AddToLibraryDialogViewModel(_library, new LibraryCatalogueReaders { NoaaEnc = (_, _) => gate.Task }));
        wizard.Start(null);

        var next = NextAsync(wizard);

        Assert.True(wizard.IsCatalogueStep);
        Assert.True(wizard.IsReadingCatalogue);
        Assert.False(wizard.NextCommand.CanExecute(null));
        Assert.Equal(Resources.Strings.Wizard_Loading, wizard.SelectionText);

        gate.SetResult(_read(new Uri("https://a.test/catalog.xml")));
        await next;

        Assert.False(wizard.IsReadingCatalogue);
        Assert.True(wizard.IsIncludeStep);
    }

    [Fact]
    public async Task A_failed_read_on_next_shows_the_error_on_what_to_include()
    {
        _read = _ => throw new HttpRequestException("offline");
        var wizard = CreateWizard(Source("a"));
        wizard.Start(null);

        await NextAsync(wizard);

        Assert.True(wizard.IsIncludeStep);
        Assert.True(wizard.Scope!.HasLoadError);
        Assert.False(wizard.IsReadingCatalogue);
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
