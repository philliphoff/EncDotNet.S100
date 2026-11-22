using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="CatalogueRegistry"/> (design note <c>catalogue-panels.md</c>
/// D4–D7, D11, D12): precedence, live replace and revert through the two
/// managers, error fallback to the built-in catalogue, session-only and
/// custom products, and the stopgap panels reading it.
/// </summary>
public sealed class CatalogueRegistryTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();

    public CatalogueRegistryTests()
    {
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
    }

    public void Dispose()
    {
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    private CatalogueRegistry Registry() => new(_settings, _features, _portrayals);

    private CatalogueRegistry Initialized(string[]? fc = null, string[]? pc = null)
    {
        var registry = Registry();
        registry.Initialize(fc, pc);
        return registry;
    }

    private static DetectedCatalogue Detect(string path) =>
        CatalogueSpecDetection.Detect(path).Catalogue ?? throw new InvalidOperationException(CatalogueSpecDetection.Detect(path).Reason);

    private string PortrayalVersionInUse(string spec) => _portrayals.GetProvider(spec).Catalogue.Version;

    [Fact]
    public void Bundled_feature_catalogues_are_there_before_initialize()
    {
        _ = Registry();

        Assert.Equal(CatalogueFixtures.BuiltInVersion, _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.False(_portrayals.HasCatalogue("S-102"));
    }

    [Fact]
    public void Every_bundled_product_is_built_in_and_in_use_after_initialize()
    {
        var registry = Initialized();

        Assert.Equal(Specifications.Specification.AvailableSpecs.Order().ToArray(), registry.Products.Select(p => p.Spec).Order().ToArray());
        var s102 = registry.Find("s102")!;
        Assert.True(s102.IsBuiltInProduct);
        Assert.False(s102.IsChanged);
        Assert.False(s102.HasProblem);
        Assert.Equal(CatalogueStatus.InUse, s102.Feature.Status);
        Assert.Equal(CatalogueOrigin.BuiltIn, s102.Feature.Origin);
        Assert.Equal(CatalogueFixtures.BuiltInVersion, s102.Feature.Source!.Metadata!.Version);
        Assert.Equal(CatalogueOrigin.BuiltIn, s102.Portrayal.Origin);
        Assert.Null(s102.Portrayal.Overrides);
        Assert.Equal(CatalogueFixtures.BuiltInVersion, PortrayalVersionInUse("S-102"));
    }

    [Fact]
    public void Command_line_wins_over_custom_which_wins_over_built_in()
    {
        _settings.PortrayalCataloguePaths["S-102"] = _files.PortrayalFolder("9.0.0");
        _settings.FeatureCataloguePaths["S-102"] = _files.FeatureFile("9.0.0");
        var cliPc = _files.PortrayalFolder("8.0.0");

        var registry = Initialized(pc: [cliPc]);

        var s102 = registry.Find("S-102")!;
        Assert.Equal(CatalogueOrigin.CommandLine, s102.Portrayal.Origin);
        Assert.Equal(Path.GetFullPath(cliPc), s102.Portrayal.Source!.Path);
        Assert.Equal(CatalogueOrigin.Custom, s102.Portrayal.Overrides!.Origin);
        Assert.Equal("9.0.0", s102.Portrayal.Overrides.Metadata!.Version);
        Assert.Equal("8.0.0", PortrayalVersionInUse("S-102"));

        Assert.Equal(CatalogueOrigin.Custom, s102.Feature.Origin);
        Assert.Equal(CatalogueOrigin.BuiltIn, s102.Feature.Overrides!.Origin);
        Assert.Equal(CatalogueFixtures.BuiltInVersion, s102.Feature.Overrides.Metadata!.Version);
        Assert.Equal("9.0.0", _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.True(s102.IsChanged);
    }

    [Fact]
    public void A_command_line_catalogue_cant_be_reverted()
    {
        var registry = Initialized(fc: [_files.FeatureFile("8.0.0")]);

        Assert.False(registry.Revert("S-102", CatalogueKind.Feature));
        Assert.Equal("8.0.0", _features.GetCatalogue("S-102")?.VersionNumber);
    }

    [Fact]
    public void Using_a_feature_catalogue_replaces_the_parsed_one_live_and_keeps_it()
    {
        var registry = Initialized();
        Assert.Equal(CatalogueFixtures.BuiltInVersion, _features.GetCatalogue("S-102")?.VersionNumber);
        var changes = new List<string>();
        registry.CataloguesChanged += (_, e) => changes.Add(e.Spec.Name);
        var path = _files.FeatureFile("9.1.0");

        var state = registry.Use(Detect(path));

        Assert.Equal("9.1.0", _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.Equal(["S-102"], changes);
        Assert.Equal(CatalogueOrigin.Custom, state.Feature.Origin);
        Assert.True(state.Feature.KeptAfterRestart);
        Assert.Equal(Path.GetFullPath(path), _settings.FeatureCataloguePaths["S-102"]);
        Assert.True(File.Exists(_settings.SettingsFilePath));
    }

    [Fact]
    public void Using_a_portrayal_package_replaces_the_provider_live()
    {
        var registry = Initialized();
        var before = _portrayals.GetProvider("S-102");

        registry.Use(Detect(_files.PortrayalPackage("3.5.0")));

        Assert.NotSame(before, _portrayals.GetProvider("S-102"));
        Assert.Equal("3.5.0", PortrayalVersionInUse("S-102"));
    }

    [Fact]
    public void Reverting_a_portrayal_catalogue_puts_the_built_in_back()
    {
        var registry = Initialized();
        registry.Use(Detect(_files.PortrayalFolder("9.2.0")));
        Assert.Equal("9.2.0", PortrayalVersionInUse("S-102"));

        Assert.True(registry.Revert("S-102", CatalogueKind.Portrayal));

        Assert.Equal(CatalogueFixtures.BuiltInVersion, PortrayalVersionInUse("S-102"));
        Assert.False(_settings.PortrayalCataloguePaths.ContainsKey("S-102"));
        Assert.Equal(CatalogueOrigin.BuiltIn, registry.Find("S-102")!.Portrayal.Origin);
        Assert.False(registry.Revert("S-102", CatalogueKind.Portrayal));
    }

    [Fact]
    public void A_catalogue_for_this_session_only_isnt_saved()
    {
        var registry = Initialized();

        var state = registry.Use(Detect(_files.FeatureFile("9.3.0")), keepAfterRestart: false);

        Assert.Equal("9.3.0", _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.False(state.Feature.KeptAfterRestart);
        Assert.Empty(_settings.FeatureCataloguePaths);
        Assert.True(registry.Revert("S-102", CatalogueKind.Feature));
        Assert.Equal(CatalogueFixtures.BuiltInVersion, _features.GetCatalogue("S-102")?.VersionNumber);
    }

    [Fact]
    public void A_broken_custom_catalogue_is_an_error_and_the_built_in_draws()
    {
        var broken = Path.Combine(_files.Root, "broken-pc");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "portrayal_catalogue.xml"), "<pc:portrayalCatalog");
        _settings.PortrayalCataloguePaths["S-102"] = broken;
        _settings.FeatureCataloguePaths["S-102"] = Path.Combine(_files.Root, "gone.xml");

        var registry = Initialized();

        var s102 = registry.Find("S-102")!;
        Assert.True(s102.HasProblem);
        Assert.Equal(CatalogueStatus.Error, s102.Portrayal.Status);
        Assert.Equal(CatalogueOrigin.Custom, s102.Portrayal.Origin);
        Assert.Equal(CatalogueOrigin.BuiltIn, s102.Portrayal.Drawing!.Origin);
        Assert.False(string.IsNullOrWhiteSpace(s102.Portrayal.Error));
        Assert.Equal(CatalogueFixtures.BuiltInVersion, PortrayalVersionInUse("S-102"));
        Assert.Equal(CatalogueStatus.Error, s102.Feature.Status);
        Assert.Equal(CatalogueFixtures.BuiltInVersion, _features.GetCatalogue("S-102")?.VersionNumber);

        // The stopgap row says what failed and what draws instead (A8).
        var vm = new PortrayalCataloguesViewModel(registry);
        var row = vm.Entries.Single(e => e.ProductSpec == "S-102");
        Assert.True(row.HasError);
        Assert.True(row.CanRemove);
        Assert.Contains(CatalogueFixtures.BuiltInVersion, row.StateText, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_failures_are_logged_under_the_catalogues_area()
    {
        Assert.Equal("Catalogues", LogAreas.Resolve(typeof(CatalogueRegistry).FullName!));
        Assert.Equal("Catalogues", LogAreas.Resolve(typeof(CatalogueRedrawCoordinator).FullName!));
        Assert.Contains("Catalogues", LogAreas.KnownAreas);
    }

    [Fact]
    public void A_catalogue_for_a_product_that_isnt_bundled_adds_the_product_and_revert_removes_it()
    {
        var registry = Initialized();

        var state = registry.Use(Detect(_files.FeatureFile("0.1.0", product: "S-199")));

        Assert.Equal("S-199", state.Spec);
        Assert.False(state.IsBuiltInProduct);
        Assert.True(state.IsChanged);
        Assert.Equal(CatalogueStatus.InUse, state.Feature.Status);
        Assert.Null(state.Feature.Overrides);
        Assert.Equal(CatalogueStatus.Missing, state.Portrayal.Status);
        Assert.True(state.HasProblem);
        Assert.Equal("0.1.0", _features.GetCatalogue("S-199")?.VersionNumber);

        Assert.True(registry.Revert("S-199", CatalogueKind.Feature));

        Assert.Null(registry.Find("S-199"));
        Assert.Null(_features.GetCatalogue("S-199"));
    }

    [Fact]
    public void The_feature_panel_shows_one_row_per_product_and_removes_through_the_registry()
    {
        var registry = Initialized(fc: [_files.FeatureFile("8.0.0")]);
        var vm = new FeatureCataloguesViewModel(registry, TestPickReport.Examiner());

        Assert.Single(vm.Entries, e => e.ProductSpec == "S-102");
        var cli = vm.Entries.Single(e => e.ProductSpec == "S-102");
        Assert.False(cli.CanRemove);
        Assert.Equal("v8.0.0 · 2024-12-05", cli.Subtitle);
        Assert.Contains(CatalogueFixtures.BuiltInVersion, cli.StateText, StringComparison.Ordinal);

        Assert.Null(vm.Add(_files.FeatureFile("9.0.0", product: "S-104")));
        vm.Reload();
        var custom = vm.Entries.Single(e => e.ProductSpec == "S-104");
        Assert.True(custom.CanRemove);

        vm.RemoveCommand.Execute(custom);
        vm.Reload();
        Assert.True(vm.Entries.Single(e => e.ProductSpec == "S-104").IsBuiltIn);
        Assert.NotNull(vm.Add(Path.Combine(_files.Root, "missing.xml")));
    }

    [Fact]
    public void Initialize_ignores_a_command_line_catalogue_it_cant_read()
    {
        var registry = Initialized(fc: [Path.Combine(_files.Root, "missing.xml")]);

        Assert.All(registry.Products, p => Assert.NotEqual(CatalogueOrigin.CommandLine, p.Feature.Origin));
    }
}
