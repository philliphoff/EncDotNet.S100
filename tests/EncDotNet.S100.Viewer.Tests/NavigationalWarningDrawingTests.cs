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
/// Which S-124 warnings the map draws (#993): only those in force at the view
/// time, those not yet in force with the setting on, and the one on the open
/// Warning card; with the panel's wording and the MCP flags that follow.
/// </summary>
public sealed class NavigationalWarningDrawingTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public NavigationalWarningDrawingTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static DateTimeOffset Oct(int day) => new(2026, 10, day, 0, 0, 0, TimeSpan.Zero);

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

    private static string PartId(int ordinal) => $"part.{ordinal}";

    private sealed class MovableViewTime(DateTimeOffset current) : IViewTime
    {
        public DateTimeOffset Current { get; private set; } = current;
        public event EventHandler? Changed;

        public void MoveTo(DateTimeOffset time)
        {
            Current = time;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static NavigationalWarningInfo Part(int number, DateTimeOffset from, DateTimeOffset to, DateTimeOffset? published = null) => new()
    {
        Series = "NAVAREA X",
        Number = $"{number}/26",
        Reference = new NavigationalWarningReference("NAVAREA X", number, 2026),
        WarningType = "Survey operations",
        PublicationTime = published ?? Oct(1),
        Validity = [new NavigationalWarningValidity(from, to)],
        HasLocation = true,
    };

    /// <summary>At 15 Oct: 0 in force, 1 ended, 2 not yet in force (published 1 Oct), 3 cancelled by 4.</summary>
    private sealed class Fixture
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public DatasetEntry Entry { get; } = new("/tmp/navarea.gml", "S-124");
        public MovableViewTime ViewTime { get; } = new(Oct(15));
        public NavigationalWarningsService Service { get; }
        public NavigationalWarningDrawingService Drawing { get; }
        public NavigationalWarningsViewModel Panel { get; }
        public EcdisDisplayState Display { get; } = new();

        public Fixture(bool showNotYetInForce = false)
        {
            var processor = new FakeProcessor(
                Part(120, Oct(10), Oct(20)),
                Part(121, Oct(1), Oct(5)),
                Part(122, Oct(25), Oct(30)),
                Part(123, Oct(1), Oct(30)),
                Part(124, Oct(1), Oct(30), published: Oct(12)) with { Cancels = [new NavigationalWarningReference("NAVAREA X", 123, 2026)] });
            Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [Entry] = processor };
            Service = new NavigationalWarningsService(Loader);
            Drawing = new NavigationalWarningDrawingService(Service, ViewTime, new ViewerSettings { IsReadOnly = true, ShowWarningsNotYetInForce = showNotYetInForce }, display: Display);
            Panel = new NavigationalWarningsViewModel(Service, viewTime: ViewTime, clock: new FixedClock(Oct(15))) { Drawing = Drawing };
        }

        public NavigationalWarningEntry Find(int ordinal) => Service.Find(Entry.DisplayName, PartId(ordinal))!;

        public HashSet<string> Hidden => [.. Entry.HiddenFeatures ?? new HashSet<string>()];
    }

    [Fact]
    public void Only_warnings_in_force_are_drawn_by_default()
    {
        var f = new Fixture();

        Assert.Equal([PartId(1), PartId(2), PartId(3)], f.Hidden.Order());
        Assert.True(f.Drawing.IsDrawn(f.Find(0)));
        Assert.False(f.Drawing.IsDrawn(f.Find(2)));
    }

    [Fact]
    public void The_setting_draws_warnings_not_yet_in_force_but_never_ended_or_cancelled_ones()
    {
        var f = new Fixture();
        var raised = 0;
        f.Drawing.Changed += (_, _) => raised++;

        f.Drawing.ShowNotYetInForce = true;

        Assert.Equal([PartId(1), PartId(3)], f.Hidden.Order());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_setting_starts_from_the_saved_value()
    {
        var f = new Fixture(showNotYetInForce: true);

        Assert.True(f.Drawing.ShowNotYetInForce);
        Assert.DoesNotContain(PartId(2), f.Hidden);
    }

    [Fact]
    public void Moving_the_view_time_redraws_across_a_warnings_range()
    {
        var f = new Fixture();

        f.ViewTime.MoveTo(Oct(3));
        Assert.Contains(PartId(0), f.Hidden);
        Assert.DoesNotContain(PartId(1), f.Hidden);
        Assert.DoesNotContain(PartId(3), f.Hidden); // not cancelled until 124/26 is published on 12 Oct

        f.ViewTime.MoveTo(Oct(26));
        Assert.Equal([PartId(0), PartId(1), PartId(3)], f.Hidden.Order());
    }

    [Fact]
    public void An_open_card_keeps_its_warning_drawn_until_it_closes()
    {
        var f = new Fixture();
        var ended = f.Find(1);

        f.Drawing.SetOpenWarning(f.Entry.DisplayName, ended.FeatureRef);

        Assert.DoesNotContain(ended.FeatureRef, f.Hidden);
        Assert.True(f.Drawing.IsDrawn(ended));
        Assert.True(f.Drawing.IsDrawnForCard(ended));
        Assert.False(f.Drawing.IsDrawnForCard(f.Find(0)));

        // Still drawn when the view time moves while the card is open.
        f.ViewTime.MoveTo(Oct(28));
        Assert.DoesNotContain(ended.FeatureRef, f.Hidden);

        f.Drawing.SetOpenWarning(null, null);

        Assert.Contains(ended.FeatureRef, f.Hidden);
        Assert.False(f.Drawing.IsDrawnForCard(ended));
    }

    [Fact]
    public void The_open_card_follows_the_pick_report()
    {
        var f = new Fixture();
        var cancelled = f.Find(3);
        var pick = new PickReportViewModel();
        f.Drawing.Follow(pick);

        pick.SetPicks([new PickHit
        {
            FeatureType = "NavwarnPart",
            FeatureRef = cancelled.FeatureRef,
            DatasetFileName = f.Entry.DisplayName,
            ProductSpec = "S-124",
            NavigationalWarning = cancelled.Warning,
        }]);

        Assert.NotNull(pick.WarningCard);
        Assert.DoesNotContain(cancelled.FeatureRef, f.Hidden);

        pick.Clear();

        Assert.Contains(cancelled.FeatureRef, f.Hidden);
    }

    [Fact]
    public void The_not_in_force_group_says_what_is_drawn()
    {
        var f = new Fixture();
        var group = f.Panel.Items.OfType<NavigationalWarningGroupItem>().Single(g => g.IsNotInForce);
        Assert.Equal(Strings.NavWarnings_NotInForceNotDrawn, group.Source);

        f.Panel.ToggleNotInForceCommand.Execute(null);
        Assert.All(f.Panel.Items.OfType<NavigationalWarningRowItem>().Where(r => !r.IsInForce), r => Assert.False(r.IsDrawn));

        f.Drawing.ShowNotYetInForce = true;

        group = f.Panel.Items.OfType<NavigationalWarningGroupItem>().Single(g => g.IsNotInForce);
        Assert.Equal(Strings.NavWarnings_NotInForceEndedNotDrawn, group.Source);
        var later = f.Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Entry.FeatureRef == PartId(2));
        Assert.True(later.IsDrawn);
        Assert.EndsWith($" · {Strings.NavWarnings_Drawn}", later.SubText);
        var ended = f.Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Entry.FeatureRef == PartId(1));
        Assert.False(ended.IsDrawn);
        Assert.DoesNotContain(Strings.NavWarnings_Drawn, ended.SubText);
    }

    [Fact]
    public async Task Mcp_lists_drawn_flags_and_sets_the_setting()
    {
        var f = new Fixture();
        f.Drawing.SetOpenWarning(f.Entry.DisplayName, PartId(1));

        var list = await new ListNavigationalWarningsTool(f.Panel, f.Service, new InlineInvoker()).InvokeAsync(TestContext.Current.CancellationToken);
        Assert.True(list.TryGetValue(out var dto));
        Assert.False(dto!.ShowNotYetInForce);
        Assert.Equal(Oct(15), dto.DrawTime);
        var byId = dto.Warnings.ToDictionary(w => w.FeatureId);
        Assert.True(byId[PartId(0)].Drawn);
        Assert.True(byId[PartId(1)].Drawn);
        Assert.True(byId[PartId(1)].DrawnForCard);
        Assert.False(byId[PartId(2)].Drawn);
        Assert.False(byId[PartId(3)].Drawn);

        var set = await new SetNavigationalWarningDisplayTool(f.Panel, f.Service, new InlineInvoker()).InvokeAsync(true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(set.TryGetValue(out dto));
        Assert.True(dto!.ShowNotYetInForce);
        Assert.True(f.Drawing.ShowNotYetInForce);
        Assert.True(dto.Warnings.Single(w => w.FeatureId == PartId(2)).Drawn);
    }

    // #995: the Display controls' Navigational warnings switch.

    private static HashSet<string> AllParts => [.. Enumerable.Range(0, 5).Select(PartId)];

    [Fact]
    public void The_switch_off_draws_no_warning_not_even_the_open_cards()
    {
        var f = new Fixture(showNotYetInForce: true);
        f.Drawing.SetOpenWarning(f.Entry.DisplayName, PartId(1));
        var raised = 0;
        f.Drawing.Changed += (_, _) => raised++;

        f.Display.SetShowNavigationalWarnings(false);

        Assert.False(f.Drawing.ShowNavigationalWarnings);
        Assert.Equal(AllParts.Order(), f.Hidden.Order());
        Assert.False(f.Drawing.IsDrawn(f.Find(0)));
        Assert.False(f.Drawing.IsDrawnForCard(f.Find(1)));
        Assert.Equal(1, raised);

        // Moving the view time or the open card doesn't bring any back.
        f.ViewTime.MoveTo(Oct(26));
        f.Drawing.SetOpenWarning(f.Entry.DisplayName, PartId(0));
        Assert.Equal(AllParts.Order(), f.Hidden.Order());
    }

    [Fact]
    public void The_switch_on_again_draws_by_the_in_force_rules()
    {
        var f = new Fixture();
        f.Drawing.ShowNavigationalWarnings = false;

        f.Drawing.ShowNavigationalWarnings = true;

        Assert.True(f.Display.ShowNavigationalWarnings);
        Assert.Equal([PartId(1), PartId(2), PartId(3)], f.Hidden.Order());
    }

    [Fact]
    public void The_switch_leaves_the_datasets_own_visibility_alone()
    {
        var f = new Fixture();
        f.Entry.IsVisible = false;

        f.Drawing.ShowNavigationalWarnings = false;
        Assert.False(f.Entry.IsVisible);
        f.Drawing.ShowNavigationalWarnings = true;
        Assert.False(f.Entry.IsVisible);

        f.Entry.IsVisible = true;
        f.Drawing.ShowNavigationalWarnings = false;
        Assert.True(f.Entry.IsVisible);
        Assert.False(f.Drawing.ShowNavigationalWarnings);
    }

    [Fact]
    public void The_switch_off_hides_parts_before_the_first_render()
    {
        var loader = new FakeDatasetLoaderService();
        var entry = new DatasetEntry("/tmp/navarea.gml", "S-124");
        var processor = new FakeProcessor(Part(120, Oct(10), Oct(20)), Part(121, Oct(10), Oct(20)));
        var display = new EcdisDisplayState();
        display.SetShowNavigationalWarnings(false);

        using var drawing = new NavigationalWarningDrawingService(new NavigationalWarningsService(loader), new MovableViewTime(Oct(15)), loader: loader, display: display);

        var policy = loader.HiddenFeaturesOnLoad;
        Assert.NotNull(policy);
        Assert.Equal([PartId(0), PartId(1)], policy(entry, processor)!.Order());
        display.SetShowNavigationalWarnings(true);
        Assert.Empty(policy(entry, processor)!);
    }

    [Fact]
    public void The_switch_off_hides_a_warnings_pick_highlight_only()
    {
        var f = new Fixture();
        var warning = new PickHit { FeatureType = "NavwarnPart", FeatureRef = PartId(0), DatasetFileName = f.Entry.DisplayName, ProductSpec = "S-124", NavigationalWarning = f.Find(0).Warning };
        var area = new PickHit { FeatureType = "NavwarnAreaAffected", FeatureRef = "area.0", DatasetFileName = f.Entry.DisplayName, ProductSpec = "S-124" };
        var chart = new PickHit { FeatureType = "Wreck", FeatureRef = "W1", DatasetFileName = "101GB0001.000", ProductSpec = "S-101" };

        Assert.False(f.Drawing.IsHighlightHidden(warning));

        f.Drawing.ShowNavigationalWarnings = false;

        Assert.True(f.Drawing.IsHighlightHidden(warning));
        Assert.True(f.Drawing.IsHighlightHidden(area));
        Assert.False(f.Drawing.IsHighlightHidden(chart));
    }

    [Fact]
    public void The_panel_keeps_listing_and_says_the_switch_is_off()
    {
        var f = new Fixture();
        var rows = f.Panel.Items.OfType<NavigationalWarningRowItem>().Count();
        Assert.False(f.Panel.IsSwitchedOff);

        f.Drawing.ShowNavigationalWarnings = false;

        Assert.True(f.Panel.IsSwitchedOff);
        Assert.Equal(rows, f.Panel.Items.OfType<NavigationalWarningRowItem>().Count());
        // The banner says it; the Not in force group doesn't repeat it.
        Assert.Equal(string.Empty, f.Panel.Items.OfType<NavigationalWarningGroupItem>().Single(g => g.IsNotInForce).Source);

        f.Panel.TurnOnDrawingCommand.Execute(null);

        Assert.True(f.Drawing.ShowNavigationalWarnings);
        Assert.False(f.Panel.IsSwitchedOff);
    }

    [Fact]
    public async Task Mcp_reads_and_sets_the_switch()
    {
        var f = new Fixture();
        var tool = new SetNavigationalWarningDisplayTool(f.Panel, f.Service, new InlineInvoker());

        var set = await tool.InvokeAsync(null, showNavigationalWarnings: false, TestContext.Current.CancellationToken);

        Assert.True(set.TryGetValue(out var dto));
        Assert.False(dto!.ShowNavigationalWarnings);
        Assert.False(dto.ShowNotYetInForce); // left as it was
        Assert.All(dto.Warnings, w => Assert.False(w.Drawn));
        Assert.False(f.Display.ShowNavigationalWarnings);

        var list = await new ListNavigationalWarningsTool(f.Panel, f.Service, new InlineInvoker()).InvokeAsync(TestContext.Current.CancellationToken);
        Assert.True(list.TryGetValue(out dto));
        Assert.False(dto!.ShowNavigationalWarnings);
        Assert.Equal(5, dto.Warnings.Count);

        set = await tool.InvokeAsync(null, showNavigationalWarnings: true, TestContext.Current.CancellationToken);
        Assert.True(set.TryGetValue(out dto));
        Assert.True(dto!.ShowNavigationalWarnings);
        Assert.True(dto.Warnings.Single(w => w.FeatureId == PartId(0)).Drawn);
    }
}
