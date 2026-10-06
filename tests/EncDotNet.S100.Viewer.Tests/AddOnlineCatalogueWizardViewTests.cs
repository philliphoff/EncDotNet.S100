using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Smoke test that the "Add online catalogue" wizard's AXAML (and its three
/// nested step views) loads and lays out against its view-models at every
/// step without error. Catches XAML/compiled-binding regressions that the
/// view-model tests cannot.
/// </summary>
public sealed class AddOnlineCatalogueWizardViewTests
{
    [AvaloniaFact]
    public void View_LoadsAndLaysOut_AtEveryStep()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();

        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
                Task.FromResult(new CatalogueProbe(null, null, null))),
            () => new AddToLibraryDialogViewModel(library, (_, _) => Task.FromResult(NoaaEncProductCatalogReader.Read(
                LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml")))));
        wizard.Start(null);

        var view = new AddOnlineCatalogueWizardView { DataContext = wizard };
        var window = new Window { Content = view, Width = 640, Height = 760 };
        window.Show();
        Layout(window);

        wizard.Directory.SearchText = "noaa";
        wizard.Directory.IsUrlPanelOpen = true;
        Layout(window);

        wizard.NextCommand.Execute(null);
        Assert.Equal(2, wizard.CurrentStep);
        wizard.Scope!.States[0].IsSelected = true;
        Layout(window);

        wizard.NextCommand.Execute(null);
        Assert.Equal(3, wizard.CurrentStep);
        Layout(window);

        Assert.Equal(640, view.Bounds.Width);
        window.Close();
    }

    [AvaloniaFact]
    public void View_LoadsAndLaysOut_with_the_S102_region_picker()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();

        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
                Task.FromResult(new CatalogueProbe(null, null, null))),
            () => new AddToLibraryDialogViewModel(library, null,
                loadS100Catalogue: (uri, _) =>
                {
                    using var stream = File.OpenRead(LibraryTestContext.RepoFile(
                        "tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-s102-catalog.xml"));
                    return Task.FromResult(RemoteS100CatalogueReader.Read(stream, uri));
                }));
        wizard.Start(null);

        var view = new AddOnlineCatalogueWizardView { DataContext = wizard };
        var window = new Window { Content = view, Width = 640, Height = 760 };
        window.Show();
        wizard.Directory.SelectedEntry = wizard.Directory.Entries.Single(e => e.Source.Id == "noaa-s102");
        Layout(window);

        wizard.NextCommand.Execute(null);
        Assert.Equal(2, wizard.CurrentStep);
        Assert.True(wizard.Scope!.IsRegionPicker);
        wizard.Scope.SelectedFacetGroup = wizard.Scope.FacetGroups[1];
        wizard.Scope.FacetGroups[1].Options[0].IsSelected = true;
        wizard.Scope.SelectedResolution = wizard.Scope.Resolutions[1];
        Layout(window);

        wizard.NextCommand.Execute(null);
        Assert.Equal(3, wizard.CurrentStep);
        Layout(window);

        Assert.Equal(640, view.Bounds.Width);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(480)]
    [InlineData(560)]
    [InlineData(1000)]
    public void Footer_stays_inside_the_window_and_does_not_move_between_steps(double windowHeight)
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();

        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
                Task.FromResult(new CatalogueProbe(null, null, null))),
            () => new AddToLibraryDialogViewModel(library, (_, _) => Task.FromResult(NoaaEncProductCatalogReader.Read(
                LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml")))));
        wizard.Start(null);

        var view = new AddOnlineCatalogueWizardView { DataContext = wizard };
        var window = new Window { Content = view, Width = 640, Height = windowHeight };
        window.Show();

        double? cancelBottom = null;
        for (var step = 1; step <= 3; step++)
        {
            window.Measure(new Size(640, windowHeight));
            window.Arrange(new Rect(0, 0, 640, windowHeight));

            var cancel = view.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "Wizard.Cancel");
            var bottom = cancel.TranslatePoint(new Point(0, cancel.Bounds.Height), window)!.Value.Y;
            Assert.InRange(bottom, 1, windowHeight);
            Assert.Equal(cancelBottom ?? bottom, bottom, 1);
            cancelBottom = bottom;

            if (step == 2)
                wizard.Scope!.States[0].IsSelected = true;
            wizard.NextCommand.Execute(null);
        }

        // With room to spare, the dialog keeps its designed height.
        if (windowHeight >= 600 + DialogWindowFit.WindowMargin)
            Assert.Equal(600, view.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Adding_a_catalogue_by_clicking_through_the_wizard_creates_the_collection()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        library.Initialize();
        var wizard = CreateNoaaEncWizard(library);
        bool? closed = null;
        wizard.Closed += (_, added) => closed = added;
        using var host = ShowWizard(wizard);

        // Step 1: find the catalogue and pick it.
        host.Click(host.Find<TextBox>("Catalogues.Search"));
        host.Type("NOAA ENC");
        host.Click(CatalogueRow(host, "noaa-enc"));
        host.Click(host.Find<Button>("Wizard.Next"));
        Assert.Equal(2, wizard.CurrentStep);

        // Step 2: only one state.
        var state = wizard.Scope!.States[0];
        host.Click(host.Find<RadioButton>("CatalogueScope.OnlySelected"));
        host.Click(host.FindAll<CheckBox>("CatalogueScope.Option").Single(c => ReferenceEquals(c.DataContext, state)));
        Assert.True(state.IsSelected);
        host.Click(host.Find<Button>("Wizard.Next"));
        Assert.Equal(3, wizard.CurrentStep);

        // Step 3: a new collection, named by the user.
        host.Click(host.Find<RadioButton>("CatalogueTarget.CreateNew"));
        host.Click(host.Find<TextBox>("CatalogueTarget.Name"));
        // Select-all is Ctrl+A on the headless platform (⌘A on a Mac).
        host.Press(PhysicalKey.A, RawInputModifiers.Control);
        host.Type("Gulf charts");
        host.Click(host.Find<Button>("Wizard.Add"));
        await library.WhenIdle();

        Assert.True(closed);
        var collection = Assert.Single(library.Collections);
        Assert.Equal("Gulf charts", collection.Definition.Name);
        Assert.Single(collection.Sources);
    }

    [AvaloniaFact]
    public void Back_returns_to_the_directory_with_the_catalogue_still_chosen()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        var wizard = CreateNoaaEncWizard(library);
        using var host = ShowWizard(wizard);

        host.Click(CatalogueRow(host, "noaa-enc"));
        host.Click(host.Find<Button>("Wizard.Next"));
        host.Click(host.Find<Button>("Wizard.Back"));

        Assert.Equal(1, wizard.CurrentStep);
        Assert.Equal("noaa-enc", wizard.Directory.SelectedEntry?.Source.Id);
        Assert.True(CatalogueRow(host, "noaa-enc").IsSelected);
    }

    [AvaloniaFact]
    public void Cancel_closes_without_adding_anything()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        library.Initialize();
        var wizard = CreateNoaaEncWizard(library);
        bool? closed = null;
        wizard.Closed += (_, added) => closed = added;
        using var host = ShowWizard(wizard);

        host.Click(CatalogueRow(host, "noaa-enc"));
        host.Click(host.Find<Button>("Wizard.Cancel"));

        Assert.False(closed);
        Assert.Empty(library.Collections);
    }

    [AvaloniaFact]
    public void Checking_a_malformed_url_shows_an_error()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        var wizard = CreateNoaaEncWizard(library);
        using var host = ShowWizard(wizard);

        host.Click(host.Find<Button>("Catalogues.AddByUrl"));
        host.Click(host.Find<TextBox>("Catalogues.Url"));
        host.Type("not a url");
        host.Click(host.Find<Button>("Catalogues.CheckAndAdd"));

        Assert.True(wizard.Directory.HasUrlError);
        Assert.Contains("error", host.Find<TextBox>("Catalogues.Url").Classes);
        Assert.Equal(1, wizard.CurrentStep);
    }

    private static AddOnlineCatalogueWizardViewModel CreateNoaaEncWizard(CollectionLibrary library)
    {
        var wizard = new AddOnlineCatalogueWizardViewModel(
            new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
                Task.FromResult(new CatalogueProbe(null, null, null))),
            () => new AddToLibraryDialogViewModel(library, (_, _) => Task.FromResult(NoaaEncProductCatalogReader.Read(
                LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml")))));
        wizard.Start(null);
        return wizard;
    }

    private static ViewHost ShowWizard(AddOnlineCatalogueWizardViewModel wizard)
        => ViewHost.Show(new AddOnlineCatalogueWizardView { DataContext = wizard }, width: 640, height: 760);

    private static ListBoxItem CatalogueRow(ViewHost host, string sourceId)
        => host.Find<ListBoxItem>(i => i.DataContext is CatalogueEntryViewModel entry && entry.Source.Id == sourceId);

    private static void Layout(Window window)
    {
        window.Measure(new Size(640, 760));
        window.Arrange(new Rect(0, 0, 640, 760));
    }
}
