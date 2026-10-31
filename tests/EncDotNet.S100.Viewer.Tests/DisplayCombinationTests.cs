using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using DisplayPlane = EncDotNet.S100.Pipelines.Vector.DisplayPlane;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DisplayCombinations;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The built-in display combinations are well formed (#1192; design note
/// <c>predefined-display-combinations.md</c> §2, §7).
/// </summary>
public class DisplayCombinationTests
{
    [Fact]
    public async Task Built_in_viewing_groups_exist_in_the_bundled_S101_catalogue()
    {
        var catalogue = await OpenS101CatalogueAsync();
        var ids = catalogue.ViewingGroups.Select(g => int.Parse(g.Id, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();

        foreach (var combination in BuiltInDisplayCombinations.Create())
        {
            foreach (var group in combination.HiddenViewingGroups.Values.Concat(combination.ShownViewingGroups.Values).SelectMany(g => g))
                Assert.Contains(group, ids);
        }
    }

    [Fact]
    public async Task Charted_only_text_groups_are_the_catalogues_Important_and_Other_Text()
    {
        var catalogue = await OpenS101CatalogueAsync();

        Assert.Equal(TextGroupMapping.Resolve(TextGroup.Other, catalogue).Order(), BuiltInDisplayCombinations.S101OtherText.Order());
        Assert.Equal(TextGroupMapping.Resolve(TextGroup.Important, catalogue).Order(), BuiltInDisplayCombinations.S101ImportantText.Order());
    }

    [Fact]
    public void Built_in_sub_layer_keys_are_ones_a_processor_emits()
    {
        var known = new HashSet<string>
        {
            S104DatasetProcessor.IsolineLayerKey,
            S104DatasetProcessor.StationLayerKey,
            "s111.arrows",
            "s111.stations",
        };

        foreach (var combination in BuiltInDisplayCombinations.Create())
        {
            foreach (var key in combination.SubLayerVisibility.Keys)
                Assert.Contains(key, known);
        }
    }

    [Fact]
    public void Built_ins_have_unique_ids_and_a_three_step_ladder()
    {
        var builtIns = BuiltInDisplayCombinations.Create();

        Assert.Equal(builtIns.Count, builtIns.Select(c => c.Id).Distinct().Count());
        Assert.Equal(
            [BuiltInDisplayCombinations.ChartedId, BuiltInDisplayCombinations.ChartedBathymetryId, BuiltInDisplayCombinations.PilotageId],
            builtIns.Where(c => c.LadderStep is not null).OrderBy(c => c.LadderStep).Select(c => c.Id));
        Assert.All(builtIns, c => Assert.Equal(DisplayCombinationOrigin.BuiltIn, c.Origin));
    }

    [Fact]
    public void Passage_planning_takes_every_product_but_currents_and_weather()
    {
        var planning = BuiltInDisplayCombinations.Create().Single(c => c.Id == BuiltInDisplayCombinations.PassagePlanningId);

        Assert.False(planning.Includes("S-111"));
        Assert.False(planning.Includes(DisplayCombinationProducts.Weather));
        foreach (var product in new[] { "S-101", "S-102", "S-104", "S-122", "S-124", "S-127", "S-128" })
            Assert.True(planning.Includes(product), product);
        Assert.Equal(EcdisDisplayCategory.OtherInformation, planning.Category);
    }

    [Fact]
    public void Everything_loaded_includes_every_product_and_sets_nothing_else()
    {
        var all = BuiltInDisplayCombinations.Create().Single(c => c.Id == BuiltInDisplayCombinations.AllLoadedId);

        Assert.True(all.IncludesEveryProduct);
        Assert.Null(all.Category);
        Assert.Empty(all.HiddenViewingGroups);
        Assert.Empty(all.LayerVisibility);
        Assert.Empty(all.SubLayerVisibility);
    }

    [Theory]
    [InlineData("S-57", "S-101", "S-101")]
    [InlineData("S-57", "S-401", "S-101")]
    [InlineData("S-401", "S-401", "S-101")]
    [InlineData("S-101", "S-101", "S-101")]
    [InlineData("S-102", "S-102", "S-102")]
    [InlineData("GRIB2", "GRIB2", "Weather")]
    [InlineData("NetCDF", "NetCDF", "Weather")]
    public void Products_match_by_key(string productSpec, string portrayalSpec, string expected) =>
        Assert.Equal(expected, DisplayCombinationProducts.KeyOf(productSpec, portrayalSpec));

    [Theory]
    [InlineData("s111.arrows", "S-111")]
    [InlineData("s104.isolines", "S-104")]
    [InlineData("weather.wind.speed", "Weather")]
    public void Layer_keys_name_their_product(string key, string expected) =>
        Assert.Equal(expected, DisplayCombinationProducts.OfLayerKey(key));

    private static async Task<PortrayalCatalogue> OpenS101CatalogueAsync()
    {
        var provider = await PortrayalCatalogueProvider.OpenAsync(Specification.CreatePortrayalCatalogueSource("S-101"));
        return provider.Catalogue;
    }
}

/// <summary>
/// <see cref="DisplayCombinationService"/>: applying, the load hook, modified
/// detection and persistence (#1192; design note §7).
/// </summary>
public class DisplayCombinationServiceTests
{
    private sealed class Processor(string spec, string? portrayalSpec = null) : IDatasetProcessor
    {
        public string ProductSpec => spec;
        public SpecRef Spec { get; } = new(spec, default);
        public SpecRef PortrayalSpec => new(portrayalSpec ?? spec, default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];
    }

    private sealed class Harness : IDisposable
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public EcdisDisplayState State { get; } = new();
        public DatasetsViewModel Datasets { get; }
        public ViewerSettings Settings { get; }
        public DisplayCombinationService Service { get; }

        public Harness(ViewerSettings? settings = null)
        {
            Datasets = new DatasetsViewModel(Loader);
            Settings = settings ?? new ViewerSettings { IsReadOnly = true };
            Service = new DisplayCombinationService(State, Loader, Datasets, Settings);
        }

        public DatasetEntry Add(string path, string spec, params string[] subLayers)
        {
            var entry = new DatasetEntry(path, spec) { IsLoaded = true };
            foreach (var key in subLayers)
                entry.SubLayers.Add(new DatasetSubLayer(key, key));
            Datasets.Entries.Add(entry);
            return entry;
        }

        public bool Active(DatasetEntry entry) => Loader.GetActive(entry.Id.Value);

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public void A_fresh_install_starts_in_Everything_loaded()
    {
        using var h = new Harness();

        Assert.Equal(BuiltInDisplayCombinations.AllLoadedId, h.Service.Active?.Id);
        Assert.False(h.Service.IsModified);
    }

    [Fact]
    public void Applying_sets_the_category_viewing_groups_and_layer_switches()
    {
        using var h = new Harness();
        h.State.SetCategory(EcdisDisplayCategory.All);
        h.State.HideViewingGroup("S-101", 11);
        h.State.SetLayerVisible("weather.pressure.isobars", true);

        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);

        Assert.Equal(EcdisDisplayCategory.Standard, h.State.Category);
        var hidden = h.State.GetHidden("S-101");
        Assert.DoesNotContain(11, hidden);
        Assert.All(BuiltInDisplayCombinations.S101OtherText, g => Assert.Contains(g, hidden));

        h.Service.Apply(BuiltInDisplayCombinations.PilotageId);
        Assert.False(h.State.GetLayerVisible("weather.pressure.isobars"));
    }

    [Fact]
    public void Applying_keeps_viewing_groups_the_combination_doesnt_name()
    {
        using var h = new Harness();
        h.State.HideViewingGroup("S-101", 90000);

        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);

        Assert.Contains(90000, h.State.GetHidden("S-101"));
    }

    [Fact]
    public void Applying_never_touches_mariner_settings_the_warnings_switch_display_planes_or_the_palette()
    {
        var path = Path.Combine(Path.GetTempPath(), $"combo-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new ViewerSettings { SettingsFilePath = path, SafetyContour = 12, SafetyDepth = 9 };
            using var h = new Harness(settings);
            h.State.SetShowNavigationalWarnings(false);
            h.State.HideDisplayPlane(DisplayPlane.OverRadar);
            settings.Save();
            var before = Untouched(File.ReadAllText(path));

            foreach (var combination in h.Service.BuiltIns)
                h.Service.Apply(combination.Id);
            settings.Save();

            Assert.False(h.State.ShowNavigationalWarnings);
            Assert.Contains(DisplayPlane.OverRadar, h.State.GetHiddenDisplayPlanes());
            Assert.Equal(before, Untouched(File.ReadAllText(path)));
            Assert.Equal(12, settings.SafetyContour);
        }
        finally
        {
            File.Delete(path);
        }

        // Everything in settings.json but the combination's own keys.
        static string Untouched(string json)
        {
            var node = JsonNode.Parse(json)!.AsObject();
            node.Remove(nameof(ViewerSettings.ActiveDisplayCombinationId));
            node.Remove(nameof(ViewerSettings.DisplayCombinations));
            return node.ToJsonString();
        }
    }

    [Fact]
    public void Charted_only_makes_S102_inactive_not_hidden_so_R_101_102_B_stops()
    {
        using var h = new Harness();
        var s101 = h.Add("/data/US5WA31M.000", "S-57");
        var s102 = h.Add("/data/102US00_US4WA1PB.h5", "S-102");

        var result = h.Service.Apply(BuiltInDisplayCombinations.ChartedId)!;

        Assert.True(h.Active(s101));
        Assert.False(h.Active(s102));
        Assert.True(s102.IsVisible);
        var change = Assert.Single(result.Datasets);
        Assert.Equal(s102.Id.Value, change.Id);
        Assert.False(change.Active);

        // The S-98 rule that suppresses the ENC depth areas under S-102 keys on
        // Active, so with S-102 out of the picture it no longer fires.
        var context = new S98RuleContext(
        [
            new LoadedDatasetInfo(s101.Id.Value, "S-101", h.Active(s101)),
            new LoadedDatasetInfo(s102.Id.Value, "S-102", h.Active(s102)),
        ]);
        Assert.False(S98DefaultRules.R_101_102_B_SuppressDepthFeatures.Condition(context));

        h.Service.Apply(BuiltInDisplayCombinations.ChartedBathymetryId);
        Assert.True(h.Active(s102));
    }

    [Fact]
    public void Datasets_of_excluded_products_say_why_they_are_inactive()
    {
        using var h = new Harness();
        var s104 = h.Add("/data/104.h5", "S-104");

        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);

        Assert.Equal("Not in Charted only", s104.InactiveReason);
        Assert.Equal("Not in Charted only", h.Service.InactiveReason(s104));

        h.Loader.SetActive(s104.Id.Value, true);
        Assert.Null(s104.InactiveReason);
    }

    [Fact]
    public void Products_not_loaded_are_reported_and_never_loaded()
    {
        using var h = new Harness();
        h.Add("/data/US5WA31M.000", "S-101");

        var result = h.Service.Apply(BuiltInDisplayCombinations.PilotageId)!;

        Assert.Equal(["S-102", "S-104", "S-111", "S-124", "S-125", "S-201"], result.NotLoaded);
        Assert.Single(h.Datasets.Entries);
    }

    [Fact]
    public void Applying_sets_sub_layers_on_every_dataset_of_the_product()
    {
        using var h = new Harness();
        var a = h.Add("/data/a.h5", "S-104", S104DatasetProcessor.IsolineLayerKey, S104DatasetProcessor.StationLayerKey);
        var b = h.Add("/data/b.h5", "S-104", S104DatasetProcessor.IsolineLayerKey);

        h.Service.Apply(BuiltInDisplayCombinations.PilotageId);

        Assert.False(a.SubLayers.Single(s => s.Key == S104DatasetProcessor.IsolineLayerKey).IsVisible);
        Assert.True(a.SubLayers.Single(s => s.Key == S104DatasetProcessor.StationLayerKey).IsVisible);
        Assert.False(b.SubLayers.Single().IsVisible);
    }

    [Fact]
    public void Load_hook_loads_excluded_products_inactive()
    {
        using var h = new Harness();
        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);
        var hook = h.Loader.ActiveOnLoad!;

        Assert.False(hook(new DatasetEntry("/data/102.h5", "S-102"), new Processor("S-102")));
        Assert.True(hook(new DatasetEntry("/data/US5WA31M.000", "S-57"), new Processor("S-57", "S-101")));
        Assert.True(hook(new DatasetEntry("/data/U37IL005.000", "S-57"), new Processor("S-57", "S-401")));
    }

    [Fact]
    public void Load_hook_decides_sub_layers_the_combination_sets()
    {
        using var h = new Harness();
        h.Service.Apply(BuiltInDisplayCombinations.PilotageId);
        var hook = h.Loader.SubLayerVisibilityOnLoad!;
        var entry = new DatasetEntry("/data/104.h5", "S-104");

        Assert.False(hook(entry, S104DatasetProcessor.IsolineLayerKey));
        Assert.True(hook(entry, "s111.arrows"));
        Assert.Null(hook(entry, "s102.surface"));
    }

    [Fact]
    public void With_no_combination_the_load_hooks_are_cleared()
    {
        using var h = new Harness();
        var mine = h.Service.SaveCurrent("Mine");

        h.Service.Delete(mine.Id);

        Assert.Null(h.Service.Active);
        Assert.Null(h.Loader.ActiveOnLoad);
        Assert.Null(h.Loader.SubLayerVisibilityOnLoad);
    }

    [Fact]
    public void The_load_hook_holds_a_dataset_out_of_the_layer_stack()
    {
        // The production loader asks the hook before its first stack
        // evaluation (ApplyPostRegistrationPolicies), and the entry it sets
        // inactive is out of the S-98 rule set from then on.
        using var h = new Harness();
        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);
        var entry = new DatasetEntry("/data/102.h5", "S-102");

        if (h.Loader.ActiveOnLoad!(entry, new Processor("S-102")) is { } active)
            entry.IsActive = active;

        Assert.False(entry.IsActive);
    }

    [Fact]
    public void Each_covered_control_marks_the_combination_modified()
    {
        using var h = new Harness();
        var s101 = h.Add("/data/US5WA31M.000", "S-101");
        var s102 = h.Add("/data/102.h5", "S-102");
        var s104 = h.Add("/data/104.h5", "S-104", S104DatasetProcessor.IsolineLayerKey);

        h.Service.Apply(BuiltInDisplayCombinations.PilotageId);
        Assert.False(h.Service.IsModified);

        h.State.SetCategory(EcdisDisplayCategory.All);
        AssertModified(DisplayCombinationChangeKind.Category);
        Assert.Equal("Changed: Display category Standard → All.", DisplayCombinationsViewModel.DescribeChanges(h.Service.Changes));

        h.Loader.SetActive(s102.Id.Value, false);
        AssertModified(DisplayCombinationChangeKind.ProductMadeInactive);

        s104.SubLayers.Single().IsVisible = true;
        h.State.SetCategory(EcdisDisplayCategory.Standard); // recompute
        AssertModified(DisplayCombinationChangeKind.SubLayer);

        h.State.SetLayerVisible("weather.wind.speed", true);
        AssertModified(DisplayCombinationChangeKind.Layer);

        h.Service.Reapply();
        Assert.False(h.Service.IsModified);
        Assert.True(h.Active(s102));

        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);
        h.Loader.SetActive(s104.Id.Value, true);
        AssertModified(DisplayCombinationChangeKind.ProductMadeActive);

        h.Service.Reapply();
        h.State.ShowViewingGroup("S-101", 21);
        AssertModified(DisplayCombinationChangeKind.ViewingGroups);
        _ = s101;

        void AssertModified(DisplayCombinationChangeKind kind)
        {
            Assert.True(h.Service.IsModified);
            Assert.Contains(h.Service.Changes, c => c.Kind == kind);
        }
    }

    [Fact]
    public void Uncovered_controls_and_dataset_visibility_dont_mark_it_modified()
    {
        using var h = new Harness();
        var s102 = h.Add("/data/102.h5", "S-102");
        h.Service.Apply(BuiltInDisplayCombinations.ChartedBathymetryId);

        h.State.HideDisplayPlane(DisplayPlane.UnderRadar);
        h.State.SetShowNavigationalWarnings(false);
        h.State.HideViewingGroup("S-101", 90000);
        h.State.HideViewingGroup("S-124", 1);
        s102.IsVisible = false;
        h.State.SetCategory(EcdisDisplayCategory.Standard);

        Assert.False(h.Service.IsModified);
    }

    [Fact]
    public void User_combinations_and_the_active_id_round_trip_through_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"combo-{Guid.NewGuid():N}.json");
        try
        {
            string id;
            {
                using var h = new Harness(new ViewerSettings { SettingsFilePath = path });
                h.Add("/data/US5WA31M.000", "S-101");
                h.Add("/data/104.h5", "S-104", S104DatasetProcessor.IsolineLayerKey);
                h.Service.Apply(BuiltInDisplayCombinations.PilotageId);
                var saved = h.Service.SaveCurrent("Night approach, Elliott Bay");
                id = saved.Id;
                Assert.Equal(["S-101", "S-104"], saved.Products.Order());
            }

            var reloaded = ViewerSettings.Load(path);
            Assert.Equal(id, reloaded.ActiveDisplayCombinationId);
            using var again = new Harness(reloaded);
            var mine = Assert.Single(again.Service.UserCombinations);
            Assert.Equal("Night approach, Elliott Bay", mine.Label);
            Assert.Equal(EcdisDisplayCategory.Standard, mine.Category);
            Assert.False(mine.SubLayerVisibility[S104DatasetProcessor.IsolineLayerKey]);
            Assert.Equal(id, again.Service.Active?.Id);

            Assert.True(again.Service.Rename(id, "Elliott Bay"));
            Assert.Equal("Elliott Bay", ViewerSettings.Load(path).DisplayCombinations.Single().Label);
            Assert.False(again.Service.Rename(BuiltInDisplayCombinations.PilotageId, "Mine"));
            Assert.False(again.Service.Delete(BuiltInDisplayCombinations.PilotageId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_older_settings_file_without_combinations_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"combo-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "EcdisDisplayCategory": "All" }""");

            var settings = ViewerSettings.Load(path);

            Assert.Empty(settings.DisplayCombinations);
            Assert.Equal(BuiltInDisplayCombinations.AllLoadedId, settings.ActiveDisplayCombinationId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_active_combination_is_restored_but_not_reapplied()
    {
        var settings = new ViewerSettings { IsReadOnly = true, ActiveDisplayCombinationId = BuiltInDisplayCombinations.ChartedId };
        var state = new EcdisDisplayState();
        state.SetCategory(EcdisDisplayCategory.All);
        var loader = new FakeDatasetLoaderService();
        using var service = new DisplayCombinationService(state, loader, new DatasetsViewModel(loader), settings);

        Assert.Equal(BuiltInDisplayCombinations.ChartedId, service.Active?.Id);
        Assert.Equal(EcdisDisplayCategory.All, state.Category);
        Assert.True(service.IsModified);
        Assert.False(loader.ActiveOnLoad!(new DatasetEntry("/data/102.h5", "S-102"), new Processor("S-102")));
    }

    [Fact]
    public void Analysis_keeps_every_loaded_dataset()
    {
        // The route check and the depth probe read the dataset catalogue, which
        // follows loads and removals; a combination only changes Active, so
        // nothing is removed from what they work from (§1.3).
        using var h = new Harness();
        h.Add("/data/US5WA31M.000", "S-101");
        h.Add("/data/104.h5", "S-104");
        var removed = 0;
        h.Loader.DatasetRemoved += _ => removed++;

        h.Service.Apply(BuiltInDisplayCombinations.ChartedId);

        Assert.Equal(0, removed);
        Assert.Empty(h.Loader.RemovedEntries);
        Assert.Equal(2, h.Datasets.Entries.Count);
    }

    [Fact]
    public void View_model_labels_the_pill_and_groups_the_rows()
    {
        using var h = new Harness();
        h.Add("/data/US5WA31M.000", "S-101");
        using var vm = new DisplayCombinationsViewModel(h.Service);

        h.Service.Apply(BuiltInDisplayCombinations.PilotageId);
        Assert.Equal("View: Pilotage ▾", vm.PillLabel);
        Assert.Equal(3, vm.LadderRows.Count);
        Assert.Equal(2, vm.MoreRows.Count);
        Assert.False(vm.HasUserRows);
        var pilotage = vm.LadderRows[2];
        Assert.True(pilotage.IsActive);
        Assert.Equal("6 of 7 products not loaded", pilotage.NotLoadedSummary);

        h.State.SetCategory(EcdisDisplayCategory.All);
        Assert.Equal("View: Pilotage (modified) ▾", vm.PillLabel);
        Assert.True(vm.ReapplyCommand.CanExecute(null));

        vm.BeginSaveCommand.Execute(null);
        Assert.True(vm.IsSaving);
        Assert.Contains("Products in the picture: S-101", vm.SavedLines);
        vm.EditName = "Mine";
        vm.ConfirmEditCommand.Execute(null);
        Assert.False(vm.IsEditing);
        Assert.Equal("View: Mine ▾", vm.PillLabel);
        Assert.True(Assert.Single(vm.UserRows).IsUser);
    }
}

/// <summary>The <c>list_display_combinations</c> and <c>set_display_combination</c> MCP tools.</summary>
public class DisplayCombinationMcpToolsTests
{
    private static Task Inline(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task List_reports_the_combinations_and_the_active_one()
    {
        var loader = new FakeDatasetLoaderService();
        var datasets = new DatasetsViewModel(loader);
        datasets.Entries.Add(new DatasetEntry("/data/US5WA31M.000", "S-57") { IsLoaded = true });
        using var service = new DisplayCombinationService(new EcdisDisplayState(), loader, datasets, new ViewerSettings { IsReadOnly = true });

        var result = await new ListDisplayCombinationsTool(service, Inline).InvokeAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var list));
        Assert.Equal(BuiltInDisplayCombinations.AllLoadedId, list!.ActiveId);
        Assert.False(list.Modified);
        Assert.Equal(["S-101"], list.LoadedProducts);
        var pilotage = list.Combinations.Single(c => c.Id == "pilotage");
        Assert.Equal("built_in", pilotage.Origin);
        Assert.Equal(["S-101"], pilotage.LoadedProducts);
        Assert.Contains("S-111", pilotage.NotLoadedProducts);
        Assert.Equal(3, pilotage.LadderStep);
    }

    [Fact]
    public async Task Set_applies_a_combination_and_reports_what_changed()
    {
        var loader = new FakeDatasetLoaderService();
        var datasets = new DatasetsViewModel(loader);
        var s102 = new DatasetEntry("/data/102.h5", "S-102") { IsLoaded = true };
        datasets.Entries.Add(new DatasetEntry("/data/US5WA31M.000", "S-101") { IsLoaded = true });
        datasets.Entries.Add(s102);
        using var service = new DisplayCombinationService(new EcdisDisplayState(), loader, datasets, new ViewerSettings { IsReadOnly = true });
        var tool = new SetDisplayCombinationTool(service, Inline);

        var result = await tool.InvokeAsync(new SetDisplayCombinationRequest("charted", null), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var applied));
        Assert.Equal("all-loaded", applied!.PreviousId);
        Assert.Equal("charted", applied.ActiveId);
        var change = Assert.Single(applied.Datasets);
        Assert.Equal(s102.Id.Value, change.Id);
        Assert.False(change.Active);

        Assert.True((await tool.InvokeAsync(new SetDisplayCombinationRequest(null, true), TestContext.Current.CancellationToken)).TryGetValue(out _));
    }

    [Fact]
    public async Task Set_rejects_an_unknown_id_and_a_bad_request()
    {
        var loader = new FakeDatasetLoaderService();
        using var service = new DisplayCombinationService(new EcdisDisplayState(), loader, new DatasetsViewModel(loader), new ViewerSettings { IsReadOnly = true });
        var tool = new SetDisplayCombinationTool(service, Inline);

        Assert.True((await tool.InvokeAsync(new SetDisplayCombinationRequest("nope", null), TestContext.Current.CancellationToken)).TryGetError(out var notFound));
        Assert.IsType<DisplayCombinationNotFound>(notFound);
        Assert.True((await tool.InvokeAsync(new SetDisplayCombinationRequest("charted", true), TestContext.Current.CancellationToken)).TryGetError(out var both));
        Assert.Equal("invalid_argument", both!.Code);
        Assert.True((await tool.InvokeAsync(new SetDisplayCombinationRequest(null, null), TestContext.Current.CancellationToken)).TryGetError(out _));
    }

    [Fact]
    public async Task Reapply_with_no_active_combination_fails()
    {
        var loader = new FakeDatasetLoaderService();
        using var service = new DisplayCombinationService(
            new EcdisDisplayState(), loader, new DatasetsViewModel(loader), new ViewerSettings { IsReadOnly = true, ActiveDisplayCombinationId = null });

        var result = await new SetDisplayCombinationTool(service, Inline).InvokeAsync(new SetDisplayCombinationRequest(null, true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<NoActiveDisplayCombination>(error);
    }
}
