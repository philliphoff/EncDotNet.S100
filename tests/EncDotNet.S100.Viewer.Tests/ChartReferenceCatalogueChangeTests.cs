using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A feature catalogue replaced in the Catalogues UI reaches the Chart
/// reference (#1652): <see cref="CatalogueRegistry.Use"/> and
/// <see cref="CatalogueRegistry.Revert"/> make
/// <see cref="FeatureCatalogueManager"/> raise <c>CataloguesChanged</c> with
/// a new hash, <see cref="ChartReferenceService"/> builds a new index from
/// the new catalogue, and the activity reloads.
/// </summary>
public sealed class ChartReferenceCatalogueChangeTests : IDisposable
{
    private static readonly SpecRef S101 = new("S-101", default);

    private const string BuiltInName = "Lateral Beacon";
    private const string CustomName = "Lateral Beacon (custom)";
    private const string CustomDefinition = "A lateral beacon as the custom catalogue defines it.";

    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();
    private readonly CatalogueRegistry _registry;
    private readonly ChartReferenceService _service;

    public ChartReferenceCatalogueChangeTests()
    {
        // Wired as App.axaml.cs wires them: the registry owns a resolver-less
        // feature catalogue manager, and the service reads both managers.
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
        _registry = new CatalogueRegistry(_settings, _features, _portrayals);
        _registry.Initialize(null, null);
        _service = new ChartReferenceService(_features, _portrayals, () => [], bundledSpecs: ["S-101"]);
    }

    public void Dispose()
    {
        _service.Dispose();
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    [Fact]
    public async Task Using_and_reverting_a_custom_feature_catalogue_rebuilds_the_index_and_reloads_the_activity()
    {
        var ct = TestContext.Current.CancellationToken;
        using var vm = new ChartReferenceViewModel(_service, new ChartReferenceGlyphCache(PaletteType.Day));
        vm.Activate();
        await vm.Loading;
        Assert.Equal("S-101", vm.SelectedProduct?.Spec.Name);
        var builtInIndex = await _service.GetIndexAsync(S101, ct);
        var builtInHash = await _features.GetCatalogueHashAsync(S101, ct);
        Assert.Equal(BuiltInName, LateralBeacon(builtInIndex!).Name);
        Assert.Equal(BuiltInName, Row(vm).Name);

        var managerChanges = new List<SpecRef>();
        var serviceChanges = new List<SpecRef>();
        _features.CataloguesChanged += (_, e) => managerChanges.Add(e.Spec);
        _service.Changed += (_, e) => serviceChanges.Add(e.Spec);

        // Use: the path the Catalogues UI takes for "Use this catalogue".
        var detected = CatalogueSpecDetection.Detect(CustomS101FeatureCatalogue());
        _registry.Use(detected.Catalogue ?? throw new InvalidOperationException(detected.Reason));
        var customLoading = vm.Loading;
        await customLoading;

        Assert.Equal([S101], managerChanges);
        Assert.Contains(S101, serviceChanges);
        var customHash = await _features.GetCatalogueHashAsync(S101, ct);
        Assert.NotEqual(builtInHash, customHash);
        var customIndex = await _service.GetIndexAsync(S101, ct);
        Assert.NotSame(builtInIndex, customIndex);
        var beacon = LateralBeacon(customIndex!);
        Assert.Equal(CustomName, beacon.Name);
        Assert.Equal(CustomDefinition, beacon.Definition);
        Assert.Same(customIndex, vm.Index);
        Assert.Equal(CustomName, Row(vm).Name);

        // Revert: the custom catalogue goes and the built-in one is back.
        managerChanges.Clear();
        serviceChanges.Clear();
        Assert.True(_registry.Revert("S-101", CatalogueKind.Feature));
        Assert.NotSame(customLoading, vm.Loading);
        await vm.Loading;

        Assert.Equal([S101], managerChanges);
        Assert.Contains(S101, serviceChanges);
        Assert.Equal(builtInHash, await _features.GetCatalogueHashAsync(S101, ct));
        var revertedIndex = await _service.GetIndexAsync(S101, ct);
        Assert.NotSame(customIndex, revertedIndex);
        Assert.Equal(BuiltInName, LateralBeacon(revertedIndex!).Name);
        Assert.Same(revertedIndex, vm.Index);
        Assert.Equal(BuiltInName, Row(vm).Name);
    }

    // A copy of the bundled S-101 feature catalogue with Lateral Beacon's
    // name and definition changed.
    private string CustomS101FeatureCatalogue()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("The bundled S-101 feature catalogue is missing.");
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();
        const string original = "<S100FC:name>Lateral Beacon</S100FC:name>\n\t\t\t<S100FC:definition>A lateral beacon is used to indicate the port or starboard hand side of the route to be followed.";
        var normalized = xml.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = normalized.IndexOf(original, StringComparison.Ordinal);
        Assert.True(start >= 0, "The bundled S-101 feature catalogue no longer defines Lateral Beacon as expected.");
        var end = normalized.IndexOf("</S100FC:definition>", start, StringComparison.Ordinal);
        var custom = string.Concat(
            normalized.AsSpan(0, start),
            $"<S100FC:name>{CustomName}</S100FC:name>\n\t\t\t<S100FC:definition>{CustomDefinition}",
            normalized.AsSpan(end));
        return _files.File_("custom-s101-fc.xml", custom);
    }

    private static FeatureTypeEntry LateralBeacon(ChartReferenceIndex index) =>
        index.FeatureTypes.Single(t => t.Code == "LateralBeacon");

    private static ChartReferenceRowViewModel Row(ChartReferenceViewModel vm) =>
        vm.Rows.Single(r => r.Key.Code == "LateralBeacon");
}
