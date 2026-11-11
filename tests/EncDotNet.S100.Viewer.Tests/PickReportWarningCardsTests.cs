using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Several navigational warnings under one pick become Warning cards in
/// Object Information, one open at a time (#932, design 1c), with links and
/// Zoom to through the Navigational warnings panel.
/// </summary>
public sealed class PickReportWarningCardsTests
{
    private static readonly DatasetEntry Dataset = new("/tmp/amsa.gml", "S-124");

    internal sealed class FakeNavigator : INavigationalWarningNavigator
    {
        public List<NavigationalWarningEntry> Entries { get; } = [];
        public List<NavigationalWarningEntry> Selected { get; } = [];
        public List<NavigationalWarningEntry> Zoomed { get; } = [];

        public IReadOnlyList<NavigationalWarningEntry> Warnings => Entries;

        public event EventHandler? WarningsChanged;

        public NavigationalWarningEntry? Find(string datasetId, string featureRef) =>
            Entries.FirstOrDefault(e => e.DatasetId == datasetId && e.FeatureRef == featureRef);

        public bool Select(NavigationalWarningEntry entry)
        {
            Selected.Add(entry);
            return true;
        }

        public bool ZoomTo(NavigationalWarningEntry entry)
        {
            Zoomed.Add(entry);
            return entry.Extent is not null;
        }

        public void Raise() => WarningsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static NavigationalWarningInfo Info(string number, string text = "") => new()
    {
        Series = "NAVAREA X",
        Number = number,
        WarningType = "Survey operations",
        HasLocation = true,
        Texts = [new NavigationalWarningText("eng", text)],
    };

    private static PickHit Hit(string id, NavigationalWarningInfo? warning) => new()
    {
        FeatureType = warning is null ? "Buoy" : "NavwarnPart",
        FeatureRef = id,
        DatasetFileName = Dataset.DisplayName,
        ProductSpec = "S-124",
        NavigationalWarning = warning,
    };

    private static NavigationalWarningEntry Entry(PickHit hit, S100GeometryType geometry) => new(
        Dataset, hit.FeatureRef, 0, hit.NavigationalWarning!, geometry,
        geometry == S100GeometryType.None ? null : new MRect(0, 0, 100, 100));

    private static (PickReportViewModel Pick, FakeNavigator Navigator, PickHit Area, PickHit Point, PickHit Line, PickHit Buoy) Fixture()
    {
        var area = Hit("area", Info("098/26"));
        var point = Hit("point", Info("106/26", "SEE NAVAREA X 098/26."));
        var line = Hit("line", Info("101/26"));
        var buoy = Hit("buoy", null);
        var navigator = new FakeNavigator();
        navigator.Entries.AddRange([
            Entry(area, S100GeometryType.Surface),
            Entry(point, S100GeometryType.Point),
            Entry(line, S100GeometryType.Curve),
        ]);
        var pick = new PickReportViewModel { WarningNavigator = navigator };
        pick.SetPicks([buoy, area, point, line]);
        return (pick, navigator, area, point, line, buoy);
    }

    [Fact]
    public void Several_warnings_become_cards_points_before_lines_before_areas()
    {
        var (pick, _, area, point, line, buoy) = Fixture();

        Assert.True(pick.HasWarningCardList);
        Assert.False(pick.ShowHitList);
        Assert.Equal([point, line, area], pick.WarningCards.Select(c => c.Hit));
        Assert.Equal([buoy], pick.OtherHits);
        Assert.True(pick.HasOtherHits);
    }

    [Fact]
    public void Ordering_falls_back_to_number_within_a_geometry()
    {
        var hits = new[] { Hit("a", Info("106/26")), Hit("b", Info("098/26")), Hit("c", Info("002/27")), Hit("d", Info("099/25")) };

        var ordered = PickReportViewModel.OrderWarningHits(hits, _ => S100GeometryType.Point);

        Assert.Equal(["d", "b", "a", "c"], ordered.Select(h => h.FeatureRef));
    }

    [Fact]
    public void The_first_card_opens_and_the_others_collapse()
    {
        var (pick, _, _, point, _, _) = Fixture();

        Assert.Same(point, pick.SelectedHit);
        Assert.Equal([true, false, false], pick.WarningCards.Select(c => c.IsOpen));
        Assert.All(pick.WarningCards, c => Assert.True(c.IsCollapsible));
        Assert.Same(pick.WarningCards[0], pick.WarningCard);
        Assert.False(pick.HasSingleWarningCard);
    }

    [Fact]
    public void Opening_a_collapsed_card_closes_the_other_and_selects_its_hit()
    {
        var (pick, _, area, _, _, _) = Fixture();
        var areaCard = pick.WarningCards[2];

        areaCard.ToggleCommand.Execute(null);

        Assert.Same(area, pick.SelectedHit);
        Assert.Equal([false, false, true], pick.WarningCards.Select(c => c.IsOpen));
        Assert.Same(areaCard, pick.WarningCard);
    }

    [Fact]
    public void Selecting_another_feature_row_collapses_every_card()
    {
        var (pick, _, _, _, _, buoy) = Fixture();

        pick.SelectedOtherHit = buoy;

        Assert.Same(buoy, pick.SelectedHit);
        Assert.All(pick.WarningCards, c => Assert.False(c.IsOpen));
        Assert.Null(pick.WarningCard);
        Assert.True(pick.ShowInlineAttributes);
    }

    [Fact]
    public void Clearing_the_row_selection_keeps_the_open_card()
    {
        var (pick, _, _, point, _, _) = Fixture();

        pick.SelectedOtherHit = null;

        Assert.Same(point, pick.SelectedHit);
    }

    [Fact]
    public void Hovering_a_card_previews_its_hit()
    {
        var (pick, _, _, _, line, _) = Fixture();

        pick.SetWarningPreview(pick.WarningCards[1]);
        Assert.Same(line, pick.PreviewHit);

        pick.SetWarningPreview(null);
        Assert.Null(pick.PreviewHit);
    }

    [Fact]
    public void A_single_warning_keeps_the_lone_card()
    {
        var navigator = new FakeNavigator();
        var warning = Hit("w", Info("106/26"));
        navigator.Entries.Add(Entry(warning, S100GeometryType.Point));
        var pick = new PickReportViewModel { WarningNavigator = navigator };

        pick.SetPicks([warning, Hit("buoy", null)]);

        Assert.False(pick.HasWarningCardList);
        Assert.True(pick.ShowHitList);
        Assert.True(pick.HasSingleWarningCard);
        Assert.False(pick.WarningCard!.IsCollapsible);
        Assert.True(pick.WarningCard.IsOpen);
    }

    [Fact]
    public void Zoom_to_frames_the_part_through_the_panel()
    {
        var (pick, navigator, area, _, _, _) = Fixture();
        var areaCard = pick.WarningCards[2];

        Assert.True(areaCard.CanZoom);
        areaCard.ZoomToCommand.Execute(null);

        Assert.Equal("area", Assert.Single(navigator.Zoomed).FeatureRef);
        Assert.Same(area, pick.SelectedHit);
    }

    [Fact]
    public void A_part_with_no_location_offers_no_zoom()
    {
        var navigator = new FakeNavigator();
        var bulletin = Hit("bulletin", Info("110/26"));
        navigator.Entries.Add(Entry(bulletin, S100GeometryType.None));
        var pick = new PickReportViewModel { WarningNavigator = navigator };

        pick.SetPicks([bulletin]);

        Assert.False(pick.WarningCard!.CanZoom);
        Assert.False(pick.WarningCard.ZoomToCommand.CanExecute(null));
    }

    [Fact]
    public void Links_in_the_text_open_the_named_warning_through_the_panel()
    {
        var (pick, navigator, _, _, _, _) = Fixture();
        var card = pick.WarningCard!;

        var link = Assert.Single(card.Links);
        Assert.Equal("098/26", link.Text);
        card.OpenLinkCommand.Execute(link.Target);

        Assert.Equal("area", Assert.Single(navigator.Selected).FeatureRef);
    }

    [Fact]
    public void Links_follow_the_listed_warnings_as_they_change()
    {
        var navigator = new FakeNavigator();
        var bulletin = Hit("bulletin", Info("110/26", "IN FORCE: 106/26"));
        navigator.Entries.Add(Entry(bulletin, S100GeometryType.None));
        var pick = new PickReportViewModel { WarningNavigator = navigator };
        pick.SetPicks([bulletin]);
        Assert.Empty(pick.WarningCard!.Links);

        navigator.Entries.Add(Entry(Hit("w", Info("106/26")), S100GeometryType.Point));
        navigator.Raise();

        Assert.Equal("106/26", Assert.Single(pick.WarningCard.Links).Text);
    }
}
