using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Smoke test that the "Add collection manifest" dialog's AXAML (with the
/// nested scope and target step views) loads and lays out in each of its
/// states — groups, an unreadable manifest, and "Choose groups…". Catches
/// XAML problems (such as a static resource declared after its first use)
/// that the view-model tests cannot.
/// </summary>
public sealed class AddCollectionManifestDialogViewTests
{
    [AvaloniaFact]
    public async Task View_LoadsAndLaysOut_InEveryState()
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        Directory.CreateDirectory(Path.Combine(context.Root, "AU"));
        var good = Path.Combine(context.Root, "good.s100collection.json");
        File.WriteAllText(good, """
            { "format": "encdotnet-s100-collection", "version": 1, "title": "Test", "groups": [
              { "id": "AU", "name": "Australia", "paths": ["AU"] },
              { "id": "PE", "name": "Peru", "paths": ["PE", "PE2"] } ] }
            """);
        var bad = Path.Combine(context.Root, "bad.s100collection.json");
        File.WriteAllText(bad, """{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "id": "A" } ] }""");

        // Read the manifests off the UI thread; the views are built on it below.
        var adding = new AddToLibraryDialogViewModel(library, null);
        adding.Initialize(LibrarySourceKind.LocalManifest, good, targetCollectionId: null);
        await adding.LoadCatalogAsync();
        adding.Groups[1].IsSelected = true;

        var broken = new AddToLibraryDialogViewModel(library, null);
        broken.Initialize(LibrarySourceKind.LocalManifest, bad, targetCollectionId: null);
        await broken.LoadCatalogAsync();
        Assert.True(broken.HasManifestProblems);

        var source = new LocalManifestSource(Guid.NewGuid(), null, good, new LocalManifestFilter { Groups = ["AU"] });
        var collection = library.AddCollection("Test", [source]);
        var editing = new AddToLibraryDialogViewModel(library, null);
        editing.InitializeEdit(collection.Id, source);
        await editing.LoadCatalogAsync();

        foreach (var scope in new[] { adding, broken, editing })
        {
            var view = new AddCollectionManifestDialogView { DataContext = new AddCollectionManifestDialogViewModel(scope) };
            var window = new Window { Content = view, Width = 640, Height = 820 };
            window.Show();
            window.Measure(new Size(640, 820));
            window.Arrange(new Rect(0, 0, 640, 820));

            Assert.Equal(640, view.Bounds.Width);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(560)]
    [InlineData(700)]
    [InlineData(1000)]
    public async Task Footer_buttons_stay_inside_a_short_window(double windowHeight)
    {
        using var context = new LibraryTestContext();
        using var library = context.CreateService();
        var path = Path.Combine(context.Root, "many.s100collection.json");
        var groups = string.Join(",", Enumerable.Range(0, 20).Select(i => $$"""{ "id": "G{{i}}", "paths": ["G{{i}}"] }"""));
        File.WriteAllText(path, $$"""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ {{groups}} ] }""");

        var scope = new AddToLibraryDialogViewModel(library, null);
        scope.Initialize(LibrarySourceKind.LocalManifest, path, targetCollectionId: null);
        await scope.LoadCatalogAsync();

        var view = new AddCollectionManifestDialogView { DataContext = new AddCollectionManifestDialogViewModel(scope) };
        var window = new Window { Content = view, Width = 640, Height = windowHeight };
        window.Show();
        window.Measure(new Size(640, windowHeight));
        window.Arrange(new Rect(0, 0, 640, windowHeight));

        var buttons = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content is "Cancel" or "Add to Library")
            .ToArray();
        Assert.Equal(2, buttons.Length);
        foreach (var button in buttons)
        {
            var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y;
            Assert.InRange(bottom, 1, windowHeight);
        }

        window.Close();
    }
}
