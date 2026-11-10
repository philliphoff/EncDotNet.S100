using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Aml;
using EncDotNet.S100.Viewer.Tests.DynamicSources;
using EncDotNet.S100.Viewer.Tests.Notifications;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// AML in the Datasets panel, on the map, in the status bar and in the
/// first-open notification (#1371), over synthetic AML cells.
/// </summary>
public sealed class AmlDatasetsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("aml-datasets-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private AmlCellState Cell(string fileName, int productSpecification = 54, string marking = "UNCLASSIFIED")
    {
        var path = SyntheticAmlCell.Write(_directory, fileName, new SyntheticAmlCell.Options
        {
            ProductSpecification = productSpecification,
            Comment = SyntheticAmlCell.UkhoComment(marking),
        });
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);
        return new AmlCellState(processor.Info, processor.Drawn, processor.Features.Count, processor.Coverage);
    }

    private static DatasetsViewModel NewDatasets() => new(new NoopLoader());

    // ── Rows ─────────────────────────────────────────────────────────

    [Fact]
    public void Row_NotDrawn_SaysWhatItIsAndThatItIsNotDrawn()
    {
        var datasets = NewDatasets();
        var entry = datasets.Add(Path.Combine(_directory, "UK6U0100.000"), "AML");
        Assert.True(entry.IsAml);
        Assert.False(entry.ShowsSpecTag);
        Assert.True(entry.ShowsAmlNotDrawnIcon);

        var cell = Cell("UK6U0100.000");
        entry.SetAml(cell);

        Assert.Equal($"{cell.Info.TagText}, {cell.Info.ProductName}", entry.AmlProductLine);
        Assert.StartsWith("AML · ", entry.AmlProductLine);
        Assert.Equal(Strings.Aml_State_RecognisedNotDrawn, entry.AmlStateText);
        Assert.True(entry.ShowsAmlNotDrawnIcon);
        Assert.False(entry.ShowsAmlDrawnIcon);
        Assert.False(entry.ShowsAmlMarking);
    }

    [Fact]
    public void Row_Drawn_FollowsThePortrayalNotAFixedNotDrawn()
    {
        var datasets = NewDatasets();
        var entry = datasets.Add(Path.Combine(_directory, "UK6U0100.000"), "AML");
        entry.SetAml(Cell("UK6U0100.000") with { Drawn = true, FeatureCount = 12 });

        Assert.True(entry.ShowsAmlDrawnIcon);
        Assert.False(entry.ShowsAmlNotDrawnIcon);
        Assert.Equal(string.Format(Strings.Aml_Row_DrawnFormat, 12), entry.AmlStateText);
        Assert.False(entry.ShowsAmlCoverageOutline);
    }

    [Fact]
    public void Row_AboveUnclassified_ShowsItsMarking()
    {
        var datasets = NewDatasets();
        var entry = datasets.Add(Path.Combine(_directory, "UK6R0100.000"), "AML");
        entry.SetAml(Cell("UK6R0100.000", marking: "RESTRICTED"));

        Assert.True(entry.ShowsAmlMarking);
        Assert.Equal("RESTRICTED", entry.AmlMarking);
    }

    [Fact]
    public void Row_ChartIsNotAml()
    {
        var entry = NewDatasets().Add("/x/US5NY1AM.000", "S-57");

        Assert.False(entry.IsAml);
        Assert.True(entry.ShowsSpecTag);
        Assert.Null(entry.AmlStateText);
        Assert.False(entry.ShowsAmlCoverageOutline);
    }

    [Fact]
    public void Outline_FollowsTheEyeAndTheDrawnState()
    {
        var entry = NewDatasets().Add(Path.Combine(_directory, "UK6U0100.000"), "AML");
        var cell = Cell("UK6U0100.000");
        Assert.False(entry.ShowsAmlCoverageOutline); // not loaded yet

        entry.SetAml(cell);
        Assert.True(entry.ShowsAmlCoverageOutline);

        entry.IsVisible = false;
        Assert.False(entry.ShowsAmlCoverageOutline);
        entry.IsVisible = true;

        // Drawn, with its layers showing: the drawing says where it is.
        entry.SetAml(cell with { Drawn = true });
        var layer = new DatasetSubLayer("aml.air", "Air");
        entry.SubLayers.Add(layer);
        Assert.False(entry.ShowsAmlCoverageOutline);

        // Drawn, but every layer hidden: outlined again.
        var raised = new List<string?>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        layer.IsVisible = false;
        Assert.True(entry.ShowsAmlCoverageOutline);
        Assert.Contains(nameof(DatasetEntry.ShowsAmlCoverageOutline), raised);
    }

    // ── Exchange-set header ──────────────────────────────────────────

    [Fact]
    public void Header_ShowsTheHighestMarkingOfTheSet()
    {
        var datasets = NewDatasets();
        var source = new StubAssetSource();
        var header = datasets.RegisterExchangeSetHeader(source, Path.Combine(_directory, "AML trial set"), null, null, 3, _ => { });
        var a = datasets.AddFromExchangeSet(source, "A/UK6U0100.000", "AML", "UK6U0100.000");
        var b = datasets.AddFromExchangeSet(source, "B/UK6R0200.000", "AML", "UK6R0200.000");
        var chart = datasets.AddFromExchangeSet(source, "C/GB500001.000", "S-57", "GB500001.000");

        Assert.True(header.IsAml);
        Assert.Equal(2, header.AmlCount);
        Assert.Null(header.AmlHighestMarking);

        a.SetAml(Cell("UK6U0100.000"));
        b.SetAml(Cell("UK6R0200.000", productSpecification: 51, marking: "RESTRICTED"));

        Assert.Equal("RESTRICTED", header.AmlHighestMarking);
        Assert.Equal(string.Format(Strings.Aml_Header_HighestOfFormat, 2), header.AmlHighestOfText);
        Assert.False(chart.IsAml);

        // Only the set's first AML row, in panel order, says the coverage is outlined.
        var first = header.Datasets.First(d => d.IsAml);
        var second = header.Datasets.Last(d => d.IsAml);
        Assert.True(first.IsFirstAmlInSet);
        Assert.False(second.IsFirstAmlInSet);
        Assert.Equal(
            string.Format(Strings.Aml_Row_NotDrawnOutlinedFormat, Strings.Aml_State_RecognisedNotDrawn),
            first.AmlStateText);
        Assert.Equal(Strings.Aml_State_RecognisedNotDrawn, second.AmlStateText);
    }

    [Fact]
    public void Header_ChartSetIsNotAml()
    {
        var datasets = NewDatasets();
        var source = new StubAssetSource();
        var header = datasets.RegisterExchangeSetHeader(source, "/charts", null, null, 1, _ => { });
        datasets.AddFromExchangeSet(source, "GB500001.000", "S-57");

        Assert.False(header.IsAml);
    }

    // ── Map outline ──────────────────────────────────────────────────

    [Fact]
    public void Outline_DrawsOneFrameWithItsLabelPerNotDrawnCell()
    {
        var host = new FakeMapHost();
        var datasets = NewDatasets();
        var entry = datasets.Add(Path.Combine(_directory, "UK6R0100.000"), "AML");
        datasets.Add("/x/GB500001.000", "S-57");

        using var controller = new AmlCoverageOutlineController(
            host, datasets, new StubMeasureOverlayAppearanceProvider(), a => a(), a => a());
        var layer = host.OverlayLayers.OfType<MemoryLayer>().Single(l => l.Name == AmlCoverageOutlineController.LayerName);
        Assert.Empty(layer.Features);

        var cell = Cell("UK6R0100.000", productSpecification: 51, marking: "RESTRICTED");
        entry.SetAml(cell);
        controller.Rebuild();

        var outline = Assert.Single(controller.Outlines);
        Assert.Equal($"{cell.Info.TagText}  UK6R0100.000", outline.Title);
        Assert.Equal($"{Strings.Aml_Outline_NotDrawn} · RESTRICTED", outline.Subtitle);
        var feature = Assert.Single(layer.Features);
        Assert.IsType<AmlCoverageOutlineStyle>(Assert.Single(feature.Styles));

        // The row's eye turns it off.
        entry.IsVisible = false;
        controller.Rebuild();
        Assert.Empty(controller.Outlines);
        Assert.Empty(layer.Features);
    }

    [Fact]
    public void Outline_LeavesADrawnCellAlone()
    {
        var host = new FakeMapHost();
        var datasets = NewDatasets();
        var entry = datasets.Add(Path.Combine(_directory, "UK6U0100.000"), "AML");
        entry.SetAml(Cell("UK6U0100.000") with { Drawn = true });

        using var controller = new AmlCoverageOutlineController(
            host, datasets, new StubMeasureOverlayAppearanceProvider(), a => a(), a => a());

        Assert.Empty(controller.Outlines);
    }

    [Fact]
    public void Outline_RemovesItsLayerOnDispose()
    {
        var host = new FakeMapHost();
        var controller = new AmlCoverageOutlineController(
            host, NewDatasets(), new StubMeasureOverlayAppearanceProvider(), a => a(), a => a());

        controller.Dispose();

        Assert.DoesNotContain(host.OverlayLayers, l => l.Name == AmlCoverageOutlineController.LayerName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ticks_PointIntoTheCoveredArea_EveryNinePixels(bool counterClockwise)
    {
        SKPoint[] square = [new(0, 0), new(90, 0), new(90, 90), new(0, 90)];
        if (counterClockwise)
            Array.Reverse(square);
        var clip = new SKRect(-100, -100, 200, 200);

        var ticks = AmlCoverageOutlineRenderer.Ticks(square, hole: false, clip);

        // 4 × 90 px of frame, one tick every 9 px.
        Assert.Equal(40, ticks.Count);
        foreach (var (from, to) in ticks)
        {
            Assert.True(to.X is > 0 and < 90 || to.Y is > 0 and < 90, $"tick {from}→{to} points out");
            Assert.InRange(to.X, 0, 90);
            Assert.InRange(to.Y, 0, 90);
        }

        // A hole's ticks point out of the hole, into the covered area around it.
        foreach (var (_, to) in AmlCoverageOutlineRenderer.Ticks(square, hole: true, clip))
            Assert.True(to.X < 0 || to.X > 90 || to.Y < 0 || to.Y > 90);
    }

    [Fact]
    public void Ticks_OffScreenAreLeftOut()
    {
        SKPoint[] huge = [new(-1e6f, -1e6f), new(1e6f, -1e6f), new(1e6f, 50), new(-1e6f, 50)];

        var ticks = AmlCoverageOutlineRenderer.Ticks(huge, hole: false, new SKRect(0, 0, 90, 100));

        Assert.InRange(ticks.Count, 10, 12); // only the bottom edge crosses the view
    }

    // ── MARKING pill ─────────────────────────────────────────────────

    [Fact]
    public void Pill_HiddenWithoutAml()
    {
        var pill = new AmlMarkingIndicatorViewModel();

        pill.Update([]);

        Assert.False(pill.IsVisible);
    }

    [Fact]
    public void Pill_OutlinedWhenEverythingIsUnclassified()
    {
        var pill = new AmlMarkingIndicatorViewModel();

        pill.Update([new AmlLoadedSet("UKHO", ["UNCLASSIFIED", "UNCLASSIFIED"])]);

        Assert.True(pill.IsVisible);
        Assert.False(pill.IsElevated);
        Assert.Equal("UNCLASSIFIED", pill.DisplayedText);
    }

    [Fact]
    public void Pill_ShowsTheHighestAndListsEachMarkingWithItsSets()
    {
        var pill = new AmlMarkingIndicatorViewModel();

        pill.Update(
        [
            new AmlLoadedSet("AML trial set", ["RESTRICTED", "UNCLASSIFIED", "UNCLASSIFIED"]),
            new AmlLoadedSet("Bundeswehr", ["UNCLASSIFIED"]),
        ]);

        Assert.True(pill.IsElevated);
        Assert.Equal("RESTRICTED", pill.DisplayedText);
        Assert.Collection(
            pill.Lines,
            line =>
            {
                Assert.Equal("RESTRICTED", line.Marking);
                Assert.True(line.IsElevated);
                Assert.Equal(["AML trial set · 1 cell"], line.Sets);
            },
            line =>
            {
                Assert.Equal("UNCLASSIFIED", line.Marking);
                Assert.Equal(["AML trial set · 2 cells", "Bundeswehr · 1 cell"], line.Sets);
            });
    }

    [Fact]
    public void Pill_TieAtTheTopShowsTheFirstPlusOne()
    {
        var pill = new AmlMarkingIndicatorViewModel();

        pill.Update([new AmlLoadedSet("A", ["NATO RESTRICTED", "UK EYES ONLY", "NATO SECRET"])]);

        // Any text beyond the known three ranks highest; two of them tie.
        Assert.EndsWith("+1", pill.DisplayedText, StringComparison.Ordinal);
        Assert.True(pill.IsElevated);
    }

    // ── First-open notification ──────────────────────────────────────

    [Fact]
    public void NoticeText_FollowsDecision12()
    {
        var cells = new[]
        {
            Cell("UK6R0100.000", productSpecification: 51, marking: "RESTRICTED"),
            Cell("UK6U0200.000", productSpecification: 54),
        };

        var (title, body) = AmlSessionService.NoticeText("AML trial set", cells);

        Assert.Equal("AML trial set is military data", title);
        Assert.Equal(
            $"2 Additional Military Layers cells ({cells[0].Info.ProductCode}, {cells[1].Info.ProductCode}). SoundCharts shows where they are but doesn't draw them. Marked RESTRICTED.",
            body);
    }

    [Fact]
    public void NoticeText_UnclassifiedSaysNoMarking()
    {
        var (_, body) = AmlSessionService.NoticeText("UKHO", [Cell("UK6U0100.000")]);

        Assert.DoesNotContain("Marked", body, StringComparison.Ordinal);
        Assert.StartsWith("1 Additional Military Layers cell (", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_FeedsThePill_AndAnnouncesEachSetOnce()
    {
        var datasets = NewDatasets();
        var pill = new AmlMarkingIndicatorViewModel();
        var notifications = TestNotifications.Create();
        var store = new AmlNoticeStore(Path.Combine(_directory, "aml-notices.txt"), readOnly: false, logger: null);
        var source = new StubAssetSource();
        datasets.RegisterExchangeSetHeader(source, Path.Combine(_directory, "AML trial set"), null, null, 2, _ => { });
        var a = datasets.AddFromExchangeSet(source, "UK6R0100.000", "AML");
        var b = datasets.AddFromExchangeSet(source, "UK6U0200.000", "AML");
        var details = new List<string>();

        using var session = new AmlSessionService(
            datasets, pill, notifications, store, a => a(), (_, _) => Task.CompletedTask, (cell, _) => details.Add(cell));

        a.IsLoading = true;
        b.IsLoading = true;
        a.SetAml(Cell("UK6R0100.000", productSpecification: 51, marking: "RESTRICTED"));
        a.IsLoading = false;
        session.Update();
        await Task.Yield();
        Assert.Empty(notifications.Active); // b is still loading

        b.SetAml(Cell("UK6U0200.000"));
        b.IsLoading = false;
        session.Update();
        await Task.Yield();

        Assert.True(pill.IsVisible);
        Assert.Equal("RESTRICTED", pill.DisplayedText);
        var notice = Assert.Single(notifications.Active);
        Assert.Equal("AML trial set is military data", notice.Title);
        Assert.Contains("Marked RESTRICTED.", notice.Message, StringComparison.Ordinal);
        var action = Assert.Single(notice.Actions);
        Assert.Equal(Strings.Aml_Notice_Details, action.Label);

        // Reopening the set, in this session or the next, says nothing more.
        session.Update();
        await Task.Yield();
        Assert.Single(notifications.Active);
        var reopened = new AmlNoticeStore(Path.Combine(_directory, "aml-notices.txt"), readOnly: false, logger: null);
        Assert.True(reopened.HasAnnounced(Path.Combine(_directory, "AML trial set")));

        // Closing the set hides the pill.
        datasets.Entries.Remove(a);
        datasets.Entries.Remove(b);
        session.Update();
        Assert.False(pill.IsVisible);
    }

    private sealed class StubAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public void Dispose()
        {
        }
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }
}
