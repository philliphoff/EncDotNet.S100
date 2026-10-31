using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;
using Synth = EncDotNet.S100.Mcp.Tools.Tests.Fakes.S101AreaSynth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The depth basis service (#1191) recomputes on each of its inputs, coalesces
/// a burst into one recompute, and reads each chart cell's datum from its
/// <c>SoundingDatum</c> areas.
/// </summary>
public sealed class DepthBasisServiceTests : IDisposable
{
    private const int Mllw = 12;

    private readonly string _directory = Directory.CreateTempSubdirectory("depth-basis").FullName;
    private readonly FakeDatasetLoaderService _loader = new();
    private readonly ManualViewTime _viewTime = new();
    private readonly Queue<Action> _posted = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private DepthBasisService Service(IWaterLevelAdjustmentState? adjustment = null) => new(
        _loader,
        viewport: null,
        _viewTime,
        adjustment ?? NoWaterLevelAdjustment.Instance,
        scope: null,
        post: _posted.Enqueue,
        readDatum: read => Task.FromResult(read()));

    private int RunPosted()
    {
        var count = 0;
        while (_posted.TryDequeue(out var action))
        {
            action();
            count++;
        }

        return count;
    }

    private (DatasetEntry Entry, S101DatasetProcessor Processor) Cell(string name, int? datum)
    {
        var box = Synth.Box(0, 0, 0.1, 0.1);
        var areas = new List<Synth.AreaFeature> { Synth.DataCoverage(22_000, box), Synth.DepthArea(5, box) };
        if (datum is { } code)
            areas.Add(Synth.SoundingDatum(code, box));
        var path = Path.Combine(_directory, name + ".000");
        S101DocumentWriter.WriteToFile(path, Synth.Cell(areas).Document);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return (new DatasetEntry(path, "S-101"), new S101DatasetProcessor(path, catalogues, new MoonSharpLuaEngine(), new FeatureCatalogueManager()));
    }

    private void Load(params (DatasetEntry Entry, S101DatasetProcessor Processor)[] cells)
    {
        _loader.Processors = cells.ToDictionary(c => c.Entry, c => (IDatasetProcessor)c.Processor);
        foreach (var (entry, _) in cells)
            _loader.RaiseLoaded(entry);
    }

    [Fact]
    public void Starts_hidden_and_stays_hidden_with_nothing_loaded()
    {
        using var service = Service();
        Assert.Same(DepthBasis.Hidden, service.Current);

        service.Refresh();

        Assert.Equal(DepthBasisKind.None, service.Current.Kind);
        Assert.False(service.WaterLevelLoaded);
    }

    [Fact]
    public void A_loaded_cell_shows_charted_with_its_sounding_datum()
    {
        using var service = Service();
        var cell = Cell("US5TEST1", Mllw);

        Load(cell);
        RunPosted();

        Assert.Equal(DepthBasisKind.Charted, service.Current.Kind);
        var source = Assert.Single(service.Current.DepthSources);
        Assert.Equal(cell.Entry.DisplayName, source.DatasetId);
        Assert.Equal("S-101", source.Spec);
        Assert.Equal(Mllw, source.VerticalDatumCode);
    }

    [Fact]
    public void A_cell_without_a_sounding_datum_declares_none()
    {
        using var service = Service();
        Load(Cell("US5TEST2", datum: null));
        RunPosted();

        Assert.Null(Assert.Single(service.Current.DepthSources).VerticalDatumCode);
    }

    [Fact]
    public void A_burst_of_changes_is_one_recompute_and_one_changed()
    {
        using var service = Service();
        var changed = 0;
        service.Changed += (_, _) => changed++;
        var cell = Cell("US5TEST3", Mllw);
        _loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [cell.Entry] = cell.Processor };

        _loader.RaiseLoaded(cell.Entry);
        _viewTime.Move();
        service.Invalidate();
        _loader.SetActive(cell.Entry.Id.Value, false);

        Assert.Equal(1, RunPosted());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Each_input_recomputes()
    {
        var adjustment = new DepthBasisEvaluatorTests.FakeAdjustment(null);
        using var service = Service(adjustment);
        var changed = 0;
        service.Changed += (_, _) => changed++;
        var cell = Cell("US5TEST4", Mllw);
        _loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor> { [cell.Entry] = cell.Processor };

        _loader.RaiseLoaded(cell.Entry);
        RunPosted();
        Assert.Equal(DepthBasisKind.Charted, service.Current.Kind);

        _loader.SetActive(cell.Entry.Id.Value, false);
        RunPosted();
        Assert.Equal(DepthBasisKind.None, service.Current.Kind);

        _loader.SetActive(cell.Entry.Id.Value, true);
        RunPosted();
        Assert.Equal(DepthBasisKind.Charted, service.Current.Kind);

        _viewTime.Move();
        RunPosted();
        Assert.Equal(_viewTime.Current, service.Current.ViewTime);

        adjustment.Raise();
        RunPosted();

        _loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor>();
        _loader.RaiseRemoved(cell.Entry);
        RunPosted();
        Assert.Equal(DepthBasisKind.None, service.Current.Kind);

        Assert.Equal(6, changed);
    }

    [Fact]
    public void Dispose_stops_listening()
    {
        var service = Service();
        service.Dispose();

        _viewTime.Move();
        service.Invalidate();

        Assert.Equal(0, RunPosted());
    }

    [Fact]
    public void The_cell_reports_its_datum_at_a_point_and_overall()
    {
        var (_, processor) = Cell("US5TEST5", Mllw);
        IChartDepthCell cell = processor;

        Assert.Equal(Mllw, cell.SoundingDatum);
        Assert.Equal(Mllw, cell.SoundingDatumAt(0.05, 0.05));
        Assert.Null(cell.SoundingDatumAt(1, 1));
    }

    [Fact]
    public void The_pick_reports_an_s101_depth_on_its_sounding_datum()
    {
        var (entry, processor) = Cell("US5TEST6", Mllw);
        var hit = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureRef = "1",
            DatasetFileName = entry.DisplayName,
            OwningProcessor = processor,
            Attributes = [new PickAttribute { Code = "depthRangeMinimumValue", RawValue = "5", DepthMetresValue = 5 }],
        };

        var probe = new LocationDepthProbe().Evaluate([new(entry, processor)], [hit], 0.05, 0.05);

        Assert.NotNull(probe.Depth);
        Assert.Equal(BaseDepthSource.DepthArea, probe.Depth!.Base.Source);
        Assert.Equal(Mllw, probe.Depth.Base.VerticalDatumCode);
    }

    private sealed class ManualViewTime : IViewTime
    {
        public DateTimeOffset Current { get; private set; } = new(2026, 10, 31, 14, 20, 0, TimeSpan.Zero);

        public event EventHandler? Changed;

        public void Move()
        {
            Current = Current.AddMinutes(10);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
