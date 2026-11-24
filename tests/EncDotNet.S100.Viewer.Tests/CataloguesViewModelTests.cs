using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Catalogues activity (design note <c>catalogue-panels.md</c> D2–D6,
/// D9, D11–D13) over a real <see cref="CatalogueRegistry"/>: ordering, pill
/// counts and filtering, selection, the badge, Add catalogue… with its sheet,
/// Revert, Remove product and Undo.
/// </summary>
public sealed class CataloguesViewModelTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();
    private readonly StubCatalogueDialogs _dialogs = new();
    private readonly NotificationService _notifications = Notifications.TestNotifications.Create();

    public CataloguesViewModelTests()
    {
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
    }

    public void Dispose()
    {
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    private CatalogueRegistry Registry(string[]? fc = null)
    {
        var registry = new CatalogueRegistry(_settings, _features, _portrayals);
        registry.Initialize(fc, null);
        return registry;
    }

    private CataloguesViewModel ViewModel(
        CatalogueRegistry registry,
        IReadOnlyDictionary<string, int>? loaded = null,
        ExaminerLinks? examiner = null,
        Action? showLogs = null) =>
        TestCatalogues.ViewModel(registry, _settings, _dialogs, _notifications, examiner, loaded, showLogs: showLogs);

    private static DetectedCatalogue Detect(string path) => CatalogueSpecDetection.Detect(path).Catalogue!;

    private CatalogueProductRowViewModel Row(CataloguesViewModel vm, string spec) => vm.AllRows.Single(r => r.Spec == spec);

    [Fact]
    public void Lists_every_product_loaded_first_then_by_spec()
    {
        var registry = Registry();
        var vm = ViewModel(registry, loaded: new Dictionary<string, int> { ["S-104"] = 2, ["S-111"] = 1 });

        Assert.Equal(registry.Products.Count, vm.AllCount);
        Assert.Equal(["S-104", "S-111"], vm.AllRows.Take(2).Select(r => r.Spec));
        Assert.Equal(
            registry.Products.Select(p => p.Spec).Where(s => s is not ("S-104" or "S-111")),
            vm.AllRows.Skip(2).Select(r => r.Spec));
        Assert.Equal("2 loaded", Row(vm, "S-104").LoadedText);
        Assert.Equal(string.Empty, Row(vm, "S-102").LoadedText);
        Assert.Same(vm.Rows[0], vm.SelectedRow);
    }

    [Fact]
    public void A_row_reads_both_catalogues_and_tags_only_what_isnt_the_default()
    {
        var registry = Registry();
        var vm = ViewModel(registry);

        var s102 = Row(vm, "S-102");
        Assert.Equal($"Feature {CatalogueFixtures.BuiltInVersion} · Portrayal {CatalogueFixtures.BuiltInVersion}", s102.CataloguesText);
        Assert.Empty(s102.Tags);
        Assert.StartsWith("S-102 ", s102.Title, StringComparison.Ordinal);

        // S-104 is drawn by the viewer, not a portrayal catalogue (D2).
        Assert.EndsWith(Strings.Catalogues_Row_DrawnBySoundCharts, Row(vm, "S-104").CataloguesText, StringComparison.Ordinal);

        registry.Use(Detect(_files.PortrayalFolder("9.1.0")));
        vm.Refresh();

        Assert.Equal($"Feature {CatalogueFixtures.BuiltInVersion} · Portrayal 9.1.0", s102.CataloguesText);
        Assert.Equal([Strings.Catalogues_Tag_Custom], s102.Tags.Select(t => t.Text));
        Assert.Equal("9.1.0", s102.Portrayal.VersionText);
        Assert.Equal(Strings.Catalogues_Origin_Custom, s102.Portrayal.SourceText);
        Assert.True(s102.Portrayal.HasPath);
        Assert.Equal($"Built-in {CatalogueFixtures.BuiltInVersion}", s102.Portrayal.OverridesText);
        Assert.True(s102.CanReload);
        Assert.True(s102.CanRevert);
        Assert.True(s102.CanReveal);
        Assert.False(s102.CanRemoveProduct);
    }

    [Fact]
    public void Pills_count_and_filter_and_the_filter_box_narrows_further()
    {
        var registry = Registry(fc: [_files.FeatureFile("8.0.0")]);
        var vm = ViewModel(registry, loaded: new Dictionary<string, int> { ["S-101"] = 3 });

        Assert.Equal(1, vm.LoadedCount);
        Assert.Equal(1, vm.ChangedCount);
        Assert.Equal(0, vm.ProblemsCount);
        Assert.False(vm.HasProblems);
        Assert.Equal([Strings.Catalogues_Tag_CommandLine], Row(vm, "S-102").Tags.Select(t => t.Text));

        vm.IsLoadedPill = true;
        Assert.Equal(["S-101"], vm.Rows.Select(r => r.Spec));
        vm.IsChangedPill = true;
        Assert.Equal(["S-102"], vm.Rows.Select(r => r.Spec));
        Assert.Equal($"1 of {vm.AllCount}", vm.CountText);

        vm.IsAllPill = true;
        vm.FilterText = "bathymetr";
        Assert.Equal(["S-102"], vm.Rows.Select(r => r.Spec));
        vm.FilterText = "s104";
        Assert.Equal(["S-104"], vm.Rows.Select(r => r.Spec));
        vm.FilterText = "nothing like this";
        Assert.True(vm.HasNoMatches);
        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void The_selected_pill_is_remembered()
    {
        var registry = Registry();
        var vm = ViewModel(registry);

        vm.Pill = CataloguesPill.Changed;

        Assert.Equal("Changed", _settings.CataloguesPill);
        Assert.Equal(CataloguesPill.Changed, ViewModel(registry).Pill);
    }

    [Fact]
    public void Problems_show_on_a_pill_and_the_badge_and_hide_at_zero()
    {
        var broken = Path.Combine(_files.Root, "broken-pc");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "portrayal_catalogue.xml"), "<pc:portrayalCatalog");
        _settings.PortrayalCataloguePaths["S-102"] = broken;
        var registry = Registry();
        var vm = ViewModel(registry);
        var badges = new List<string?>();
        vm.PropertyChanged += (_, e) => badges.Add(e.PropertyName);

        Assert.Equal(1, vm.ProblemsCount);
        Assert.True(vm.HasProblems);
        Assert.Equal(1, vm.BadgeCount);
        Assert.Equal(Strings.Catalogues_BadgeOne, vm.BadgeName);
        var row = Row(vm, "S-102");
        Assert.True(row.IsError);
        Assert.Contains(row.Tags, t => t.IsError && t.Text == Strings.Catalogues_Tag_ProblemOne);
        Assert.Contains($"built-in {CatalogueFixtures.BuiltInVersion}", row.Portrayal.ErrorText, StringComparison.Ordinal);

        vm.IsProblemsPill = true;
        Assert.Equal(["S-102"], vm.Rows.Select(r => r.Spec));

        // Revert clears the problem: the pill hides and the list goes back to All.
        vm.RevertCommand.Execute(row);

        Assert.Equal(0, vm.BadgeCount);
        Assert.False(vm.HasProblems);
        Assert.Equal(CataloguesPill.All, vm.Pill);
        Assert.Contains(nameof(vm.BadgeCount), badges);
    }

    [Fact]
    public void Show_product_selects_its_row_clearing_a_filter_that_hides_it_and_opens_the_activity()
    {
        var vm = ViewModel(Registry());
        var opened = 0;
        vm.ContentBecameAvailable += (_, _) => opened++;
        vm.FilterText = "S-104";

        Assert.True(vm.ShowProduct("s102"));

        Assert.Equal("S-102", vm.SelectedRow?.Spec);
        Assert.Equal(string.Empty, vm.FilterText);
        Assert.Equal(1, opened);
        Assert.False(vm.ShowProduct("S-999"));
        Assert.Equal(2, opened);
    }

    [Fact]
    public void A_refresh_keeps_the_selected_row()
    {
        var registry = Registry();
        var vm = ViewModel(registry);
        vm.ShowProduct("S-111");
        var selected = vm.SelectedRow;

        registry.Use(Detect(_files.FeatureFile("9.0.0")));
        vm.Refresh();

        Assert.Same(selected, vm.SelectedRow);
    }

    [Fact]
    public async Task Add_catalogue_confirms_in_a_sheet_applies_live_and_undo_puts_the_built_in_back()
    {
        var registry = Registry();
        var vm = ViewModel(registry, loaded: new Dictionary<string, int> { ["S-102"] = 3 });
        _dialogs.Picks.Enqueue(_files.PortrayalFolder("9.1.0"));

        await vm.AddCommand.ExecuteAsync(null);

        var sheet = _dialogs.LastSheet;
        Assert.True(sheet.IsFound);
        Assert.Equal(Strings.Catalogues_Sheet_Title, sheet.Title);
        Assert.Equal("S-102", sheet.SpecText);
        Assert.Equal("Portrayal catalogue 9.1.0", sheet.KindText);
        Assert.Equal($"Portrayal catalogue {CatalogueFixtures.BuiltInVersion} (built-in)", sheet.ReplacesText);
        Assert.Equal("3 loaded S-102 datasets", sheet.RedrawsText);
        Assert.Equal(Strings.Catalogues_Sheet_Use, sheet.ConfirmLabel);
        Assert.True(sheet.KeepAfterRestart);

        sheet.KeepAfterRestart = false;
        sheet.ConfirmCommand.Execute(null);

        Assert.Empty(_dialogs.Open);
        Assert.Equal("9.1.0", _portrayals.GetProvider("S-102").Catalogue.Version);
        Assert.Empty(_settings.PortrayalCataloguePaths);
        Assert.Equal(Strings.Catalogues_Origin_CustomSession, Row(vm, "S-102").Portrayal.SourceText);
        Assert.Equal("S-102", vm.SelectedRow?.Spec);

        var toast = Assert.Single(_notifications.Active);
        Assert.Equal("S-102 is now drawn with portrayal catalogue 9.1.0 (custom). 3 datasets redrawn.", toast.Message);
        var undo = toast.Actions.Single(a => a.Label == Strings.Catalogues_Undo);
        undo.Command.Execute(null);

        Assert.Equal(CatalogueFixtures.BuiltInVersion, _portrayals.GetProvider("S-102").Catalogue.Version);
        Assert.Equal(CatalogueOrigin.BuiltIn, registry.Find("S-102")!.Portrayal.Origin);
    }

    [Fact]
    public void Undo_after_revert_puts_the_custom_catalogue_back_as_it_was_kept()
    {
        var registry = Registry();
        var custom = _files.FeatureFile("9.0.0");
        registry.Use(Detect(custom), keepAfterRestart: true);
        var vm = ViewModel(registry);

        vm.RevertCommand.Execute(Row(vm, "S-102"));

        Assert.Equal(CatalogueFixtures.BuiltInVersion, _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.Empty(_settings.FeatureCataloguePaths);

        _notifications.Active.Single().Actions.Single(a => a.Label == Strings.Catalogues_Undo).Command.Execute(null);

        Assert.Equal("9.0.0", _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.Equal(custom, _settings.FeatureCataloguePaths["S-102"]);
    }

    [Fact]
    public async Task A_new_product_is_added_missing_its_portrayal_and_remove_product_undoes()
    {
        var registry = Registry();
        var vm = ViewModel(registry);
        _dialogs.Picks.Enqueue(_files.FeatureFile("0.1.0", product: "S-199"));

        await vm.AddCommand.ExecuteAsync(null);

        var sheet = _dialogs.LastSheet;
        Assert.True(sheet.IsNewProduct);
        Assert.Equal(Strings.Catalogues_Sheet_NewProduct, sheet.ReplacesText);
        Assert.Equal(Strings.Catalogues_Sheet_AddProduct, sheet.ConfirmLabel);
        sheet.ConfirmCommand.Execute(null);

        var row = Row(vm, "S-199");
        Assert.Contains(row.Tags, t => t.Text == Strings.Catalogues_Tag_NotBuiltIn);
        Assert.Contains(row.Tags, t => t.IsWarning && t.Text == Strings.Catalogues_Tag_Missing);
        Assert.True(row.IsPortrayalMissing);
        Assert.True(row.CanRemoveProduct);
        Assert.False(row.CanRevert);
        Assert.Equal(1, vm.ProblemsCount);

        vm.RemoveProductCommand.Execute(row);

        Assert.DoesNotContain(vm.AllRows, r => r.Spec == "S-199");
        Assert.Null(_features.GetCatalogue("S-199"));

        _notifications.Active.Last().Actions.Single(a => a.Label == Strings.Catalogues_Undo).Command.Execute(null);

        Assert.Contains(vm.AllRows, r => r.Spec == "S-199");
        Assert.Equal("0.1.0", _features.GetCatalogue("S-199")?.VersionNumber);
    }

    [Fact]
    public async Task A_path_that_cant_be_used_is_refused_and_choose_another_reads_the_next()
    {
        var vm = ViewModel(Registry());
        _dialogs.Picks.Enqueue(Path.Combine(_files.Root, "missing.xml"));
        _dialogs.Picks.Enqueue(_files.FeatureFile("9.0.0"));

        await vm.AddCommand.ExecuteAsync(null);

        var sheet = _dialogs.LastSheet;
        Assert.True(sheet.IsRefused);
        Assert.False(sheet.IsFound);
        Assert.Equal(Strings.Catalogues_Refused_NotFound, sheet.Reason);
        Assert.False(sheet.ConfirmCommand.CanExecute(null));

        await sheet.ChooseAnotherCommand.ExecuteAsync(null);

        Assert.True(sheet.IsFound);
        Assert.Equal("Feature catalogue 9.0.0", sheet.KindText);
        Assert.True(sheet.ConfirmCommand.CanExecute(null));

        sheet.CancelCommand.Execute(null);
        Assert.Empty(_dialogs.Open);
        Assert.Equal(CatalogueFixtures.BuiltInVersion, _features.GetCatalogue("S-102")?.VersionNumber);
    }

    [Fact]
    public async Task Replace_fixes_the_product_and_kind()
    {
        var vm = ViewModel(Registry());
        _dialogs.Picks.Enqueue(_files.PortrayalFolder("9.1.0"));

        await vm.ReplaceFeatureCommand.ExecuteAsync(Row(vm, "S-102"));

        var sheet = _dialogs.LastSheet;
        Assert.Equal(("S-102", CatalogueKind.Feature), sheet.Target);
        Assert.Equal(string.Format(Strings.Catalogues_Sheet_ReplaceFeatureTitleFormat, "S-102"), sheet.Title);
        Assert.True(sheet.IsRefused);
        Assert.Contains("portrayal catalogue for S-102", sheet.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancelled_pick_opens_no_sheet()
    {
        var vm = ViewModel(Registry());

        vm.AddCommand.Execute(null);

        Assert.Empty(_dialogs.Shown);
    }

    [Fact]
    public void A_catalogue_that_fails_to_load_warns_with_show_in_logs()
    {
        var registry = Registry();
        var logs = 0;
        var vm = ViewModel(registry, showLogs: () => logs++);
        var folder = _files.PortrayalFolder("9.1.0");
        var catalogue = Detect(folder);
        File.WriteAllText(Path.Combine(folder, "portrayal_catalogue.xml"), "<pc:portrayalCatalog");

        vm.Use(catalogue, keepAfterRestart: false);

        var toast = Assert.Single(_notifications.Active);
        Assert.Equal(NotificationSeverity.Warning, toast.Severity);
        toast.Actions.Single(a => a.Label == Strings.Catalogues_ShowInLogs).Command.Execute(null);
        Assert.Equal(1, logs);
        Assert.True(Row(vm, "S-102").HasError);
    }

    [Fact]
    public void Reveal_shows_the_custom_path()
    {
        var registry = Registry();
        var folder = _files.PortrayalFolder("9.1.0");
        registry.Use(Detect(folder));
        var vm = ViewModel(registry);

        vm.RevealCommand.Execute(Row(vm, "S-102"));

        Assert.Equal([folder], _dialogs.Revealed);
    }

    [Fact]
    public void The_examiner_item_follows_the_settings_and_names_the_published_edition_for_a_custom_catalogue()
    {
        _settings.S100ExaminerLinksEnabled = true;
        var opener = new RecordingUrlOpener();
        var examiner = TestPickReport.Examiner(new S100ExaminerLinkBuilder(_settings), opener);
        var registry = Registry();
        var vm = ViewModel(registry, examiner: examiner);

        var row = Row(vm, "S-101");
        Assert.True(row.CanOpenInExaminer);
        Assert.Equal(Strings.Catalogues_Menu_OpenInExaminer, row.ExaminerLabel);
        vm.OpenInExaminerCommand.Execute(row);
        Assert.Equal("https://s100examiner.com/?catalog=S-101", opener.LastUrl);

        _settings.S100ExaminerLinksEnabled = false;
        vm.RefreshExaminerAvailability();
        Assert.False(row.CanOpenInExaminer);

        _settings.S100ExaminerLinksEnabled = true;
        registry.Use(Detect(_files.FeatureFile("9.0.0")));
        vm.Refresh();
        var s102 = Row(vm, "S-102");
        Assert.Equal(Strings.Catalogues_Menu_OpenPublishedInExaminer, s102.ExaminerLabel);
    }

    [Fact]
    public void Reload_reads_a_custom_catalogue_again()
    {
        var registry = Registry();
        var file = _files.FeatureFile("9.0.0");
        registry.Use(Detect(file));
        var vm = ViewModel(registry);
        File.WriteAllText(file, File.ReadAllText(file).Replace("9.0.0", "9.0.1", StringComparison.Ordinal));

        vm.ReloadCommand.Execute(Row(vm, "S-102"));

        Assert.Equal("9.0.1", _features.GetCatalogue("S-102")?.VersionNumber);
        Assert.Equal("9.0.1", Row(vm, "S-102").Feature.VersionText.Split(' ')[0]);
    }

    private sealed class RecordingUrlOpener : IUrlOpener
    {
        public string? LastUrl { get; private set; }

        public void Open(string url) => LastUrl = url;
    }
}
