using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Services.Updates;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Catalogues entry points outside the activity (catalogue-panels D10,
/// §6.4–6.5, CR3): the Datasets version warning's <b>Catalogues…</b>, the
/// dataset inspector's Drawn with line, the missing-catalogue toast's
/// <b>Choose catalogue…</b> and Help › About's table, over a real
/// <see cref="CatalogueRegistry"/> and Catalogues activity.
/// </summary>
public sealed class CatalogueEntryPointTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;
    private readonly FeatureCatalogueManager _features = new();
    private readonly PortrayalCatalogueManager _portrayals = new();
    private readonly StubCatalogueDialogs _dialogs = new();
    private readonly NotificationService _notifications = Notifications.TestNotifications.Create();
    private readonly CatalogueRegistry _registry;

    public CatalogueEntryPointTests()
    {
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
        _registry = new CatalogueRegistry(_settings, _features, _portrayals);
        _registry.Initialize(null, null);
    }

    public void Dispose()
    {
        _features.Dispose();
        _portrayals.Dispose();
        _files.Dispose();
    }

    private static readonly string BuiltIn = CatalogueFixtures.BuiltInVersion;

    private CataloguesViewModel Catalogues() => TestCatalogues.ViewModel(_registry, _settings, _dialogs, _notifications);

    private DatasetsViewModel Datasets(CataloguesViewModel catalogues)
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.AttachCatalogues(_registry, spec => catalogues.ShowProduct(spec));
        return datasets;
    }

    private void UseCustomPortrayal(string version) =>
        _registry.Use(CatalogueSpecDetection.Detect(_files.PortrayalFolder(version)).Catalogue!);

    private static void WarnAboutVersion(DatasetEntry entry) =>
        entry.SetVersionAssessment(SpecVersionAssessment.TryCreate(
            new SpecRef(entry.ProductSpec, new SpecVersion(9, 0, 0)),
            [new SpecVersion(3, 0, 0)]));

    // ── Dataset inspector: Drawn with (D10, §6.4) ───────────────────────

    [Fact]
    public void Drawn_with_names_both_catalogues_and_follows_a_live_change()
    {
        var datasets = Datasets(Catalogues());
        var cell = datasets.Add("/data/102GB00TEST.h5", "S-102");
        datasets.SelectDataset(cell);

        Assert.True(datasets.HasSelectedDrawnWith);
        Assert.Equal($"Feature {BuiltIn} · Portrayal {BuiltIn}", datasets.SelectedDrawnWith);

        var raised = new List<string?>();
        datasets.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        UseCustomPortrayal("9.1.0");

        Assert.Contains(nameof(DatasetsViewModel.SelectedDrawnWith), raised);
        Assert.Equal($"Feature {BuiltIn} · Portrayal 9.1.0 (custom)", datasets.SelectedDrawnWith);
    }

    [Fact]
    public void Drawn_with_says_SoundCharts_draws_S104()
    {
        var datasets = Datasets(Catalogues());
        datasets.SelectDataset(datasets.Add("/data/104US00TEST.h5", "S-104"));

        Assert.StartsWith("Feature ", datasets.SelectedDrawnWith, StringComparison.Ordinal);
        Assert.EndsWith(" · " + Strings.Catalogues_Row_DrawnBySoundCharts, datasets.SelectedDrawnWith, StringComparison.Ordinal);
    }

    [Fact]
    public void An_S57_cell_is_drawn_with_and_links_to_the_S101_portrayal()
    {
        string? shown = null;
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.AttachCatalogues(_registry, spec => shown = spec);
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");
        datasets.SelectDataset(cell);

        Assert.Contains("S-101 Portrayal ", datasets.SelectedDrawnWith, StringComparison.Ordinal);
        datasets.ShowCataloguesCommand.Execute(cell);
        Assert.Equal("S-101", shown);
    }

    [Fact]
    public void Drawn_with_names_a_failed_custom_catalogue_and_what_draws_instead()
    {
        var state = new CatalogueState(
            CatalogueKind.Portrayal,
            CatalogueStatus.Error,
            new CatalogueSourceInfo(CatalogueOrigin.Custom, "/x", null),
            new CatalogueSourceInfo(CatalogueOrigin.BuiltIn, null, new CatalogueMetadata("S-102", "2.0.0", null)),
            null,
            "broken",
            true);

        Assert.Equal("Portrayal 2.0.0 (custom didn't load)", CatalogueEntryPointText.Describe(state));
        Assert.Equal("? · custom, didn't load", CatalogueEntryPointText.AboutCell(state));
    }

    [Fact]
    public void Catalogues_link_opens_the_activity_with_the_product_selected()
    {
        var catalogues = Catalogues();
        var opened = 0;
        catalogues.ContentBecameAvailable += (_, _) => opened++;
        catalogues.ShowProduct("S-104");
        var datasets = Datasets(catalogues);
        var cell = datasets.Add("/data/102GB00TEST.h5", "S-102");

        Assert.True(datasets.ShowCataloguesCommand.CanExecute(cell));
        datasets.ShowCataloguesCommand.Execute(cell);

        Assert.Equal("S-102", catalogues.SelectedRow?.Spec);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void Catalogues_link_is_disabled_until_attached()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var cell = datasets.Add("/data/102GB00TEST.h5", "S-102");
        datasets.SelectDataset(cell);

        Assert.False(datasets.ShowCataloguesCommand.CanExecute(cell));
        Assert.Null(datasets.SelectedDrawnWith);
        Assert.False(datasets.HasSelectedDrawnWith);
    }

    // ── Datasets panel, headless ─────────────────────────────────────────

    private static ViewHost Show(DatasetsViewModel datasets)
        => ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);

    private static ListBoxItem Row(ViewHost host, DatasetEntry entry)
        => host.Find<ListBoxItem>(item => ReferenceEquals(item.DataContext, entry));

    [AvaloniaFact]
    public void The_version_warning_shows_why_and_its_Catalogues_link_opens_the_product()
    {
        var catalogues = Catalogues();
        var datasets = Datasets(catalogues);
        var other = datasets.Add("/data/104US00TEST.h5", "S-104");
        var cell = datasets.Add("/data/102GB00TEST.h5", "S-102");
        WarnAboutVersion(cell);

        using var host = Show(datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        Assert.True(host.Find<TextBlock>("Datasets.Row.VersionWarning", Row(host, cell)).IsEffectivelyVisible);
        Assert.Equal(cell.VersionWarningTooltip, host.Find<TextBlock>("Datasets.Row.VersionWarning", Row(host, cell)).Text);
        Assert.DoesNotContain(
            host.FindAll<Button>("Datasets.Row.VersionWarning.Catalogues"),
            b => b.GetVisualAncestors().Contains(Row(host, other)));

        host.Click(host.Find<Button>("Datasets.Row.VersionWarning.Catalogues", Row(host, cell)));

        Assert.Equal("S-102", catalogues.SelectedRow?.Spec);
    }

    [AvaloniaFact]
    public void A_rows_menu_opens_Catalogues_at_its_product()
    {
        var catalogues = Catalogues();
        var datasets = Datasets(catalogues);
        var cell = datasets.Add("/data/102GB00TEST.h5", "S-102");

        using var host = Show(datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        var menu = host.RightClick(Row(host, cell).GetVisualDescendants().OfType<Border>().First(b => b.ContextMenu is not null));
        var item = menu.Items.OfType<MenuItem>().Single(m => AutomationProperties.GetAutomationId(m) == "Datasets.RowMenu.Catalogues");
        Assert.Equal(Strings.Catalogues_Link, item.Header);

        item.Command!.Execute(item.CommandParameter);

        Assert.Equal("S-102", catalogues.SelectedRow?.Spec);
    }

    [AvaloniaFact]
    public void The_inspector_shows_Drawn_with_and_links_to_Catalogues()
    {
        var catalogues = Catalogues();
        var datasets = Datasets(catalogues);
        var cell = datasets.Add("/data/102GB00TEST.h5", "S-102");
        datasets.SelectDataset(cell);
        UseCustomPortrayal("9.1.0");

        using var host = Show(datasets);
        Assert.Equal(
            $"Feature {BuiltIn} · Portrayal 9.1.0 (custom)",
            host.Find<TextBlock>("Datasets.Inspector.DrawnWith").Text);

        host.Click(host.Find<Button>("Datasets.Inspector.DrawnWith.Catalogues"));

        Assert.Equal("S-102", catalogues.SelectedRow?.Spec);
    }

    // ── Missing-catalogue toast (D10) ────────────────────────────────────

    [Fact]
    public void The_missing_catalogue_toast_chooses_a_catalogue_for_the_product()
    {
        var catalogues = Catalogues();
        _dialogs.Picks.Enqueue(_files.PortrayalFolder("9.1.0"));

        DatasetLoaderService.ShowMissingCatalogueToast(
            _notifications, "S-102", catalogues.ChooseCatalogue, NullLogger.Instance);

        var toast = _notifications.Active.Single();
        Assert.Equal(string.Format(Strings.Status_SelectPortrayalCatalogue, "S-102"), toast.Message);
        var choose = toast.Actions.Single();
        Assert.Equal(Strings.Catalogues_ChooseCatalogue, choose.Label);
        Assert.True(choose.IsPrimary);

        choose.Command.Execute(null);

        Assert.Equal("S-102", catalogues.SelectedRow?.Spec);
        Assert.IsType<AddCatalogueSheetViewModel>(Assert.Single(_dialogs.Shown));
    }

    [Fact]
    public void Without_the_activity_the_toast_has_no_action()
    {
        DatasetLoaderService.ShowMissingCatalogueToast(_notifications, "S-101", null, NullLogger.Instance);

        Assert.Empty(_notifications.Active.Single().Actions);
    }

    // ── Help › About (§6.5) ──────────────────────────────────────────────

    private sealed class StubUpdateService : IUpdateService
    {
        public bool UpdateChecksEnabled => false;
        public Task<UpdateStatus> CheckForUpdatesAsync(bool force, CancellationToken cancellationToken = default)
            => Task.FromResult(new UpdateStatus { Availability = UpdateAvailability.Disabled });
        public void SkipVersion(string version) { }
        public void SetUpdateChecksEnabled(bool enabled) { }
    }

    private sealed class StubVersionProvider : IAppVersionProvider
    {
        public AppVersionInfo Current { get; } = new("2.4.1", "2.4.1", null, null);
    }

    private sealed class StubUrlOpener : IUrlOpener
    {
        public void Open(string url) { }
    }

    private AboutDialogViewModel About(Action<string>? showCatalogues) => new(
        new ShadUI.DialogManager(),
        new StubUpdateService(),
        new StubVersionProvider(),
        new StubUrlOpener(),
        TimeProvider.System,
        _registry,
        showCatalogues);

    [Fact]
    public void About_lists_every_products_catalogues_and_where_they_come_from()
    {
        UseCustomPortrayal("9.1.0");
        _registry.Use(CatalogueSpecDetection.Detect(_files.FeatureFile("0.1.0", product: "S-199")).Catalogue!);

        var about = About(null);

        Assert.True(about.HasCatalogues);
        Assert.False(about.CanShowCatalogues);
        Assert.Equal(_registry.Products.Select(p => p.Spec), about.Catalogues.Select(r => r.Spec));
        var s102 = about.Catalogues.Single(r => r.Spec == "S-102");
        Assert.Equal($"{BuiltIn} · built-in", s102.Feature);
        Assert.Equal("9.1.0 · custom", s102.Portrayal);
        Assert.Equal($"S-102: feature {BuiltIn} · built-in, portrayal 9.1.0 · custom", s102.AccessibleName);
        Assert.Equal(Strings.About_Catalogues_NotRequired, about.Catalogues.Single(r => r.Spec == "S-104").Portrayal);
        var draft = about.Catalogues.Single(r => r.Spec == "S-199");
        Assert.Equal("0.1.0 · custom", draft.Feature);
        Assert.Equal(Strings.About_Catalogues_Missing, draft.Portrayal);
        Assert.True(draft.HasProblem);
    }

    [Fact]
    public void Without_a_registry_About_has_no_catalogue_table()
    {
        var about = new AboutDialogViewModel(
            new ShadUI.DialogManager(), new StubUpdateService(), new StubVersionProvider(), new StubUrlOpener(), TimeProvider.System);

        Assert.False(about.HasCatalogues);
        Assert.False(about.ShowCataloguesCommand.CanExecute("S-102"));
    }

    [AvaloniaFact]
    public void About_shows_the_table_and_a_product_opens_Catalogues()
    {
        var catalogues = Catalogues();
        var about = About(spec => catalogues.ShowProduct(spec));
        using var host = ViewHost.Show(new AboutDialogView { DataContext = about }, width: 480, height: 1000);

        Assert.True(host.Find<StackPanel>("About.Catalogues").IsEffectivelyVisible);
        var rows = host.FindAll<Grid>("About.Catalogues.Row");
        Assert.Equal(_registry.Products.Count, rows.Count);
        var s102 = rows.Single(r => AutomationProperties.GetName(r)!.StartsWith("S-102:", StringComparison.Ordinal));
        Assert.Equal(
            $"{BuiltIn} · built-in",
            host.Find<SelectableTextBlock>("About.Catalogues.Row.Feature", s102).Text);

        host.Click(host.Find<Button>("About.Catalogues.Row.Product", s102));

        Assert.Equal("S-102", catalogues.SelectedRow?.Spec);
    }

    [Fact]
    public void The_feedback_report_carries_the_same_catalogue_table()
    {
        UseCustomPortrayal("9.1.0");

        var s102 = FeedbackService.DescribeCatalogues(_registry).Single(c => c.Spec == "S-102");

        Assert.Equal(BuiltIn, s102.FeatureVersion);
        Assert.Equal("BuiltIn", s102.FeatureSource);
        Assert.Equal("9.1.0", s102.PortrayalVersion);
        Assert.Equal("Custom", s102.PortrayalSource);
        Assert.Equal("InUse", s102.PortrayalStatus);
    }
}
