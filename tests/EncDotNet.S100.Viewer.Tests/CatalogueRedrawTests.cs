using System.Text.Json;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A catalogue change redraws the loaded datasets of that product and no
/// others (design note <c>catalogue-panels.md</c> D6, A5), and MCP
/// <c>list_catalogues</c> reports the registry's state.
/// </summary>
public sealed class CatalogueRedrawTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();
    private readonly CatalogueRegistry _registry;
    private readonly FakeDatasetLoaderService _loader = new();
    private readonly List<DatasetEntry> _entries = [];
    private readonly CatalogueRedrawCoordinator _redraw;

    public CatalogueRedrawTests()
    {
        var settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
        _registry = new CatalogueRegistry(settings, _features, _portrayals);
        _registry.Initialize(null, null);
        _redraw = new CatalogueRedrawCoordinator(_registry, () => _entries, _loader);
    }

    public void Dispose()
    {
        _redraw.Dispose();
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    private DatasetEntry Entry(string spec, bool loaded = true, string? portrayalSpec = null)
    {
        var entry = new DatasetEntry($"/tmp/{Guid.NewGuid():N}.dat", spec) { IsLoaded = loaded };
        if (portrayalSpec is not null)
            entry.SetPortrayalSpec(portrayalSpec);
        _entries.Add(entry);
        return entry;
    }

    private static DetectedCatalogue Detect(string path) => CatalogueSpecDetection.Detect(path).Catalogue!;

    [Fact]
    public async Task A_change_redraws_only_the_loaded_datasets_of_that_product()
    {
        var a = Entry("S-102");
        var b = Entry("S-102");
        Entry("S-102", loaded: false);
        Entry("S-111");

        _registry.Use(Detect(_files.PortrayalFolder("9.0.0")));
        await _redraw.LastRedraw;

        var reloaded = Assert.Single(_loader.Reloaded);
        Assert.Equal([a, b], reloaded);
    }

    [Fact]
    public async Task An_S57_cell_is_redrawn_when_an_S101_catalogue_changes()
    {
        var cell = Entry("S-57", portrayalSpec: "S-101");
        Entry("S-102");

        Assert.Equal([cell], _redraw.AffectedBy("S-101"));
        Assert.Equal(1, await _redraw.RedrawAsync("S-101"));
        Assert.Equal([cell], Assert.Single(_loader.Reloaded));
    }

    [Fact]
    public async Task A_change_with_nothing_loaded_reloads_nothing()
    {
        Entry("S-111");

        _registry.Use(Detect(_files.FeatureFile("9.0.0")));
        await _redraw.LastRedraw;

        Assert.Empty(_loader.Reloaded);
    }

    [Fact]
    public async Task List_catalogues_reports_each_product_with_its_loaded_datasets()
    {
        Entry("S-102");
        Entry("S-57", portrayalSpec: "S-101");
        _registry.Use(Detect(_files.PortrayalFolder("9.0.0")));
        var tool = new ListCataloguesTool(_registry, spec => _redraw.AffectedBy(spec).Count, new InlineInvoker());

        Assert.True((await tool.InvokeAsync(ct: TestContext.Current.CancellationToken)).TryGetValue(out var all));
        Assert.True((await tool.InvokeAsync("s102", TestContext.Current.CancellationToken)).TryGetValue(out var one));
        Assert.True((await tool.InvokeAsync("S-999", TestContext.Current.CancellationToken)).TryGetError(out var error));

        Assert.Contains(all!.Products, p => p.Spec == "S-101" && p.LoadedDatasets == 1 && !p.Changed);
        var s102 = Assert.Single(one!.Products);
        Assert.Equal("S-102", s102.Spec);
        Assert.Equal(1, s102.LoadedDatasets);
        Assert.True(s102.Changed);
        Assert.Equal("in_use", s102.Portrayal.Status);
        Assert.Equal("custom", s102.Portrayal.Source!.Origin);
        Assert.Equal("9.0.0", s102.Portrayal.Source.Version);
        Assert.Equal("built_in", s102.Portrayal.Overrides!.Origin);
        Assert.Equal("built_in", s102.Feature.Source!.Origin);
        Assert.Equal("catalogue_product_not_found", error!.Code);

        var json = JsonSerializer.Serialize(s102, McpAdapterShared.Options);
        Assert.Contains("\"loadedDatasets\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"keptAfterRestart\":true", json, StringComparison.Ordinal);
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
