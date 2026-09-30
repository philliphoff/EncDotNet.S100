using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.Library;
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
    [Fact]
    public void View_LoadsAndLaysOut_AtEveryStep()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();

        HeadlessTest.Run(() =>
        {
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
        });
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(640, 760));
        window.Arrange(new Rect(0, 0, 640, 760));
    }
}
