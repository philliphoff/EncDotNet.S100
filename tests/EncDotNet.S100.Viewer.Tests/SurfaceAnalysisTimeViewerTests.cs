using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1301 in the viewer (V28, V30, §6.5, §7): a surface analysis's layer
/// time (stale, out of range, taken over by a newer one), its Timeline lane
/// group, its Library row's stale warning and the pick report's stale line.
/// </summary>
public sealed class SurfaceAnalysisTimeViewerTests
{
    private static readonly DateTime Analysis = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static MapsuiMapTimedDataset Bulletin(string id, DateTime valid, TimeStepKind kind = TimeStepKind.Analysis) =>
        new(id, valid, valid)
        {
            DatasetId = id,
            ProductSpec = "CODSUS",
            Samples = [valid],
            Selection = MapsuiTimeSelectionKind.AtOrBefore,
            Tolerance = SurfaceAnalysisDatasetProcessor.DrawWindow,
            StepKind = kind,
            StaleAfter = SurfaceAnalysisDatasetProcessor.StaleAfter,
            Supersession = SurfaceAnalysisDatasetProcessor.SupersessionKey,
            Coverage = [new MapsuiMapTimeSegment(valid, valid + SurfaceAnalysisDatasetProcessor.DrawWindow)],
        };

    private static LayerTime Describe(MapsuiMapTimedDataset dataset, DateTime at, params MapsuiMapTimedDataset[] all) =>
        LayerTimes.Describe(dataset, at, drawing: false, TimeFormat.Utc, TimeZoneInfo.Utc, all: all.Length == 0 ? null : all);

    [Fact]
    public void A_current_analysis_shows_its_time_and_a_stale_one_its_Stale_tag()
    {
        var wpc = Bulletin("wpc", Analysis);

        var current = Describe(wpc, Analysis.AddHours(3));
        var stale = Describe(wpc, Analysis.AddHours(9).AddMinutes(20));

        Assert.Equal("12:00Z (−3 h)", current.Text);
        Assert.False(current.IsStale);
        Assert.False(current.IsHidden);
        Assert.Equal("12:00Z (−9 h)", stale.Text);
        Assert.True(stale.IsStale);
        Assert.Equal(TimeSpan.FromHours(9), stale.StaleAge);
        Assert.False(stale.IsHidden);
    }

    [Fact]
    public void Out_of_range_says_there_is_no_analysis_at_the_view_time_and_jumps_to_the_nearest()
    {
        var wpc = Bulletin("wpc", Analysis);

        var before = Describe(wpc, Analysis.AddHours(-2));
        var after = Describe(wpc, Analysis.AddHours(13));

        Assert.Equal("No analysis at view time", before.Text);
        Assert.True(before.IsHidden);
        Assert.Equal(Analysis, before.Nearest);
        Assert.Equal("No analysis at view time", after.Text);
        Assert.Equal(Analysis, after.Nearest);
    }

    [Fact]
    public void A_newer_analysis_or_a_forecast_valid_nearer_the_view_time_takes_over()
    {
        var older = Bulletin("12z", Analysis);
        var newer = Bulletin("15z", Analysis.AddHours(3));
        var forecast = Bulletin("srp", Analysis.AddHours(6), TimeStepKind.Forecast);
        MapsuiMapTimedDataset[] all = [older, newer, forecast];

        Assert.Equal("12:00Z (−2 h)", Describe(older, Analysis.AddHours(2), all).Text);
        Assert.Equal(LayerTimeState.Superseded, Describe(older, Analysis.AddHours(4), all).State);
        Assert.True(Describe(older, Analysis.AddHours(4), all).IsHidden);
        Assert.Equal("newer analysis drawn", Describe(newer, Analysis.AddHours(7), all).Text);
        Assert.Equal("18:00Z (−1 h)", Describe(forecast, Analysis.AddHours(7), all).Text);
        Assert.Equal(forecast.Samples[0], forecast.DrawnSampleAt(Analysis.AddHours(7), all));
    }

    [Fact]
    public void The_Timeline_groups_analyses_under_Surface_analysis_with_a_WPC_lane()
    {
        var service = new GlobalTimeService();
        var timeline = new TimelineViewModel(service, new UtcFormat(), new FakeTimeProvider(new DateTimeOffset(Analysis.AddHours(3))), action => action(), refresher: null, progress: null, scope: null, library: null);
        var wpc = Bulletin("wpc", Analysis);
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = Analysis,
            Maximum = Analysis,
            Current = Analysis.AddHours(3),
            Samples = [Analysis],
            CoverageSegments = wpc.Coverage,
            Datasets = [wpc],
        });

        var group = Assert.Single(timeline.LaneGroups);
        Assert.Equal("Surface analysis", group.Title);
        var lane = Assert.Single(group.Lanes);
        Assert.Equal("WPC", lane.Code);
        Assert.Equal("analysis · 3 h", lane.Sub);
        Assert.Equal("Surface analysis", TimelineViewModel.GroupTitle("CODSUS"));
    }

    [Fact]
    public void A_WPC_analysis_over_6_hours_old_is_stale_in_the_Library_and_offers_the_latest()
    {
        var known = KnownCatalogueSources.Find("noaa-wpc-surface")!;
        var source = new LibrarySource(
            new WeatherFeedSource(Guid.NewGuid(), null, known.CatalogUri, known.Weather!, new WeatherSelection()), null, LibrarySourceState.Ready);
        var run = new DateTimeOffset(Analysis);
        var item = new CollectionItem
        {
            Key = "wpc-analysis",
            ProductSpec = "CODSUS",
            Name = "wpc-analysis",
            Title = "Surface analysis · WPC",
            Location = new RemoteItemLocation(new Uri("https://tgftp.nws.noaa.gov/data/raw/as/asus02.kwbc.cod.sus.txt"), LastModified: run),
            Properties = new Dictionary<string, string>
            {
                [S100ForecastFeedIndexer.RunProperty] = "2026-11-04T12:00:00Z",
                [WeatherRuns.StaleAfterProperty] = "6",
                [WeatherFeedIndexer.BulletinKindProperty] = "analysis",
                [WeatherRuns.CoverageProperty] = "North America and adjacent waters",
            },
        };

        var fresh = new LibraryItemViewModel(item, source, time: new FakeTimeProvider(run.AddHours(5)), loadState: null, downloader: null, collectionName: null, download: null, timeFormat: null);
        var stale = new LibraryItemViewModel(item, source, time: new FakeTimeProvider(run.AddHours(7)), loadState: null, downloader: null, collectionName: null, download: null, timeFormat: null);

        Assert.False(fresh.IsStaleRun);
        Assert.True(stale.IsStaleRun);
        Assert.True(stale.CanLoadLatestRun);
        Assert.Matches("^Analysis .+ is over 6 hours old$", stale.StaleRunText);
        Assert.Contains(stale.Tags, t => t.Text == "Stale");
        Assert.EndsWith(" · North America and adjacent waters", fresh.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pick_report_shows_a_stale_analysis_as_a_line_under_the_notice()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(
        [
            new PickHit
            {
                FeatureType = SurfaceAnalysisDatasetProcessor.PressureCentreFeatureType,
                FeatureTypeName = "Low 996 hPa",
                FeatureRef = "LOW 1",
                DatasetFileName = "wpc-analysis.txt",
                ProductSpec = "CODSUS",
                Attributes =
                [
                    PickAttribute.NavigationNotice(SurfaceAnalysisDatasetProcessor.NotForNavigationNotice),
                    PickAttribute.StaleNotice("This analysis is 9 h old. A newer one is likely."),
                    new PickAttribute { Code = "pressure", Name = "Pressure", RawValue = "996 hPa" },
                ],
            },
        ]);

        Assert.Equal("This analysis is 9 h old. A newer one is likely.", vm.StaleNotice);
        Assert.True(vm.HasStaleNotice);
        Assert.DoesNotContain(vm.Attributes, a => a.IsStaleNotice);
        Assert.Contains(vm.Attributes, a => a.Code == "pressure");
    }

    private sealed class UtcFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
