using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The mariner's on chart / off chart / information classification of each
/// navigational warning (#996, S-98 Edition 2.0.0 §12.12.2): what the map
/// draws, the panel's rows and order, the Warning card, MCP, and keeping it
/// across restarts.
/// </summary>
public sealed class NavigationalWarningClassificationTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("navwarn-class-");

    public NavigationalWarningClassificationTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        _dir.Delete(recursive: true);
    }

    private static DateTimeOffset Oct(int day) => new(2026, 10, day, 0, 0, 0, TimeSpan.Zero);

    private static string PartId(int ordinal) => $"part.{ordinal}";

    private sealed class FakeProcessor(params NavigationalWarningInfo[] parts) : IDatasetProcessor
    {
        public string ProductSpec => "S-124";
        public SpecRef Spec { get; } = new("S-124", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];

        public IReadOnlyList<NavigationalWarningFeature> GetNavigationalWarnings() =>
            parts.Select((p, i) => new NavigationalWarningFeature(PartId(i), i, p)).ToList();

        public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal) => new()
        {
            Primitive = S100GeometryType.Point,
            Points = [new GeoPosition(-16.5 + ordinal * 0.1, 146.0)],
        };
    }

    private sealed class FixedViewTime(DateTimeOffset current) : IViewTime
    {
        public DateTimeOffset Current { get; } = current;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static NavigationalWarningInfo Part(int number) => new()
    {
        Series = "NAVAREA X",
        Number = $"{number}/26",
        Reference = new NavigationalWarningReference("NAVAREA X", number, 2026),
        WarningType = "Survey operations",
        PublicationTime = Oct(1),
        Validity = [new NavigationalWarningValidity(Oct(10), Oct(20))],
        HasLocation = true,
    };

    /// <summary>At 15 Oct every part is in force: 120/26 (parts 0 and 1), 121/26 (part 2), 122/26 (part 3).</summary>
    private sealed class Fixture
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public DatasetEntry Entry { get; } = new("/tmp/navarea.gml", "S-124");
        public FakeProcessor Processor { get; } = new(Part(120), Part(120), Part(121), Part(122));
        public NavigationalWarningsService Service { get; }
        public EcdisDisplayState Display { get; } = new();
        public NavigationalWarningDrawingService Drawing { get; }
        public NavigationalWarningsViewModel Panel { get; }

        public Fixture(NavigationalWarningClassificationStore? store = null)
        {
            Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [Entry] = Processor };
            Service = new NavigationalWarningsService(Loader, classifications: store);
            Drawing = new NavigationalWarningDrawingService(Service, new FixedViewTime(Oct(15)), new ViewerSettings { IsReadOnly = true }, Loader, display: Display);
            Panel = new NavigationalWarningsViewModel(Service, viewTime: new FixedViewTime(Oct(15)), clock: new FixedClock(Oct(15))) { Drawing = Drawing };
        }

        public NavigationalWarningEntry Find(int ordinal) => Service.Find(Entry.DisplayName, PartId(ordinal))!;

        public HashSet<string> Hidden => [.. Entry.HiddenFeatures ?? new HashSet<string>()];

        public NavigationalWarningRowItem Row(int ordinal) =>
            Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Entry.FeatureRef == PartId(ordinal));
    }

    private string StorePath => Path.Combine(_dir.FullName, "navigational-warning-classifications.json");

    [Fact]
    public void Every_warning_starts_on_chart_and_is_drawn()
    {
        var f = new Fixture();

        Assert.All(f.Service.Warnings, w => Assert.Equal(NavigationalWarningClassification.OnChart, f.Service.ClassificationOf(w)));
        Assert.Empty(f.Hidden);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Off_chart_and_information_warnings_are_not_drawn_but_every_part_goes(bool offChart)
    {
        var classification = offChart ? NavigationalWarningClassification.OffChart : NavigationalWarningClassification.Information;
        var f = new Fixture();

        Assert.True(f.Service.SetClassification(f.Find(0), classification));

        // Both parts of 120/26 share the classification; 121/26 and 122/26 stay drawn.
        Assert.Equal(classification, f.Service.ClassificationOf(f.Find(1)));
        Assert.Equal([PartId(0), PartId(1)], f.Hidden.Order());
        Assert.False(f.Drawing.IsDrawn(f.Find(1)));

        Assert.True(f.Service.SetClassification(f.Find(1), NavigationalWarningClassification.OnChart));
        Assert.Empty(f.Hidden);
    }

    [Fact]
    public void An_open_card_draws_an_off_chart_warning_until_it_closes()
    {
        var f = new Fixture();
        f.Service.SetClassification(f.Find(2), NavigationalWarningClassification.OffChart);

        f.Drawing.SetOpenWarning(f.Entry.DisplayName, PartId(2));

        Assert.DoesNotContain(PartId(2), f.Hidden);
        Assert.True(f.Drawing.IsDrawnForCard(f.Find(2)));

        f.Drawing.SetOpenWarning(null, null);
        Assert.Contains(PartId(2), f.Hidden);
    }

    [Fact]
    public void The_switch_off_still_overrides_an_open_card()
    {
        var f = new Fixture();
        f.Service.SetClassification(f.Find(2), NavigationalWarningClassification.Information);
        f.Drawing.SetOpenWarning(f.Entry.DisplayName, PartId(2));

        f.Display.SetShowNavigationalWarnings(false);

        Assert.Equal(4, f.Hidden.Count);
        Assert.False(f.Drawing.IsDrawnForCard(f.Find(2)));
    }

    [Fact]
    public void A_dataset_loading_hides_its_off_chart_parts_before_the_first_render()
    {
        var f = new Fixture();
        f.Service.SetClassification(f.Find(3), NavigationalWarningClassification.OffChart);

        var hidden = f.Loader.HiddenFeaturesOnLoad!(f.Entry, f.Processor)!;

        Assert.Equal([PartId(3)], hidden.Order());
    }

    [Fact]
    public void Classified_warnings_stay_listed_marked_and_can_be_changed_from_the_row()
    {
        var f = new Fixture();
        var rows = f.Panel.Items.OfType<NavigationalWarningRowItem>().Count();

        f.Row(2).ClassifyCommand!.Execute(NavigationalWarningClassification.OffChart);

        Assert.Equal(rows, f.Panel.Items.OfType<NavigationalWarningRowItem>().Count());
        var row = f.Row(2);
        Assert.True(row.IsClassifiedOff);
        Assert.True(row.IsOffChart);
        Assert.Equal(FluentIcons.Common.Icon.EyeOff, row.ClassificationGlyph);
        Assert.Equal("Classification: Off chart", row.ClassificationName);
        Assert.StartsWith(Strings.Tooltip_NavWarnings_ClassOffChart, row.ClassificationTip, StringComparison.Ordinal);
        Assert.Contains(", Off chart", row.AccessibleName, StringComparison.Ordinal);
        Assert.False(f.Row(3).IsClassifiedOff);
        Assert.Equal(FluentIcons.Common.Icon.Map, f.Row(3).ClassificationGlyph);
    }

    [Fact]
    public void The_classification_order_lists_on_chart_then_off_chart_then_information()
    {
        var f = new Fixture();
        f.Service.SetClassification(f.Find(3), NavigationalWarningClassification.Information);
        f.Service.SetClassification(f.Find(0), NavigationalWarningClassification.OffChart);

        f.Panel.SortIndex = 2;

        Assert.Equal(NavigationalWarningsSort.Classification, f.Panel.Sort);
        var order = f.Panel.Items.OfType<NavigationalWarningRowItem>().Select(r => r.Number).ToList();
        Assert.Equal(["121/26", "120/26", "120/26", "122/26"], order);
    }

    [Fact]
    public void The_warning_card_shows_and_sets_the_classification()
    {
        var f = new Fixture();
        var pick = new PickReportViewModel { WarningNavigator = f.Panel };
        var entry = f.Find(2);
        pick.SetPicks([new PickHit
        {
            FeatureType = "NavwarnPart",
            FeatureRef = entry.FeatureRef,
            DatasetFileName = f.Entry.DisplayName,
            ProductSpec = "S-124",
            NavigationalWarning = entry.Warning,
        }]);
        var card = pick.WarningCard!;
        Assert.True(card.CanClassify);
        Assert.True(card.IsOnChart);
        Assert.False(card.IsClassifiedOff);

        card.ClassifyCommand.Execute(NavigationalWarningClassification.Information);

        Assert.Equal(NavigationalWarningClassification.Information, f.Service.ClassificationOf(entry));
        Assert.True(card.IsInformation);
        Assert.True(card.IsClassifiedOff);
        Assert.True(f.Row(2).IsInformation);

        // Set from the list: the card follows.
        f.Service.SetClassification(entry, NavigationalWarningClassification.OffChart);
        Assert.True(card.IsOffChart);
    }

    [Fact]
    public void A_card_for_an_unlisted_part_offers_no_classification()
    {
        var pick = new PickReportViewModel { WarningNavigator = new Fixture().Panel };
        pick.SetPicks([new PickHit
        {
            FeatureType = "NavwarnPart",
            FeatureRef = "elsewhere",
            DatasetFileName = "other.gml",
            ProductSpec = "S-124",
            NavigationalWarning = Part(130),
        }]);

        Assert.False(pick.WarningCard!.CanClassify);
        Assert.False(pick.WarningCard.ClassifyCommand.CanExecute(NavigationalWarningClassification.OffChart));
    }

    [Fact]
    public async Task Mcp_lists_and_sets_the_classification()
    {
        var f = new Fixture();
        var tool = new SetNavigationalWarningClassificationTool(f.Panel, f.Service, new InlineInvoker());

        var set = await tool.InvokeAsync(f.Entry.DisplayName, PartId(0), "off_chart", TestContext.Current.CancellationToken);

        Assert.True(set.TryGetValue(out var dto));
        var byId = dto!.Warnings.ToDictionary(w => w.FeatureId);
        Assert.Equal("off_chart", byId[PartId(0)].Classification);
        Assert.Equal("off_chart", byId[PartId(1)].Classification);
        Assert.False(byId[PartId(0)].Drawn);
        Assert.Equal("on_chart", byId[PartId(2)].Classification);
        Assert.True(byId[PartId(2)].Drawn);

        var list = await new ListNavigationalWarningsTool(f.Panel, f.Service, new InlineInvoker()).InvokeAsync(TestContext.Current.CancellationToken);
        Assert.True(list.TryGetValue(out dto));
        Assert.Equal(4, dto!.Warnings.Count);
        Assert.Equal("off_chart", dto.Warnings.Single(w => w.FeatureId == PartId(0)).Classification);

        var bad = await tool.InvokeAsync(f.Entry.DisplayName, PartId(0), "sideways", TestContext.Current.CancellationToken);
        Assert.False(bad.TryGetValue(out _));
        var missing = await tool.InvokeAsync(f.Entry.DisplayName, "nope", "information", TestContext.Current.CancellationToken);
        Assert.False(missing.TryGetValue(out _));
    }

    [Fact]
    public void The_classification_is_kept_across_restarts_by_series_and_number()
    {
        var first = new Fixture(new NavigationalWarningClassificationStore(StorePath));
        first.Service.SetClassification(first.Find(2), NavigationalWarningClassification.Information);
        first.Service.SetClassification(first.Find(3), NavigationalWarningClassification.OffChart);
        first.Service.SetClassification(first.Find(3), NavigationalWarningClassification.OnChart);

        // A fresh viewer, which loads the warnings again (a refresh).
        var again = new Fixture(new NavigationalWarningClassificationStore(StorePath));

        Assert.Equal(NavigationalWarningClassification.Information, again.Service.ClassificationOf(again.Find(2)));
        Assert.Equal(NavigationalWarningClassification.OnChart, again.Service.ClassificationOf(again.Find(3)));
        Assert.Equal([PartId(2)], again.Hidden.Order());
    }

    [Fact]
    public void A_cancellation_or_replacement_has_its_own_number_so_it_starts_on_chart()
    {
        var store = new NavigationalWarningClassificationStore(StorePath);
        var warning = Part(121);
        store.Set(NavigationalWarningSeenStore.KeyOf(warning, "a.gml", "x"), NavigationalWarningClassification.OffChart, Oct(15));

        // The same warning in another dataset (a refresh) keeps it.
        Assert.Equal(NavigationalWarningClassification.OffChart, store.Get(NavigationalWarningSeenStore.KeyOf(warning, "b.gml", "y")));
        // 123/26, which cancels 121/26, and 121 of another year, are other warnings.
        Assert.Equal(NavigationalWarningClassification.OnChart, store.Get(NavigationalWarningSeenStore.KeyOf(Part(123), "b.gml", "y")));
        Assert.Equal(NavigationalWarningClassification.OnChart, store.Get(NavigationalWarningSeenStore.KeyOf(warning with { Number = "121/27" }, "b.gml", "y")));
    }

    [Fact]
    public void A_read_only_store_keeps_the_classification_in_memory_only()
    {
        var store = new NavigationalWarningClassificationStore(StorePath, readOnly: true);

        Assert.True(store.Set("NAVAREA X|121/26", NavigationalWarningClassification.OffChart, Oct(15)));

        Assert.Equal(NavigationalWarningClassification.OffChart, store.Get("NAVAREA X|121/26"));
        Assert.False(File.Exists(StorePath));
    }
}
