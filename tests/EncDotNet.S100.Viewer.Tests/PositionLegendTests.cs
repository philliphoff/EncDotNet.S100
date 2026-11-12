using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using Synth = EncDotNet.S100.Mcp.Tools.Tests.Fakes.S101AreaSynth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The S-98 position legend (#1407, legends design note D17): units, datum and
/// safety contour, then the finest cell at the view centre with its edition and
/// compilation scale against the display scale; it follows pan end, says "No
/// chart at the view centre" over no chart, and is in <c>get_legend</c>.
/// </summary>
public sealed class PositionLegendTests : IDisposable
{
    private const int Mllw = 12;

    private readonly string _directory = Directory.CreateTempSubdirectory("position-legend").FullName;
    private readonly LegendServiceTests.Fixture _f = new();
    private readonly Mariner _mariner = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class Mariner : IMarinerSettingsProvider
    {
        public MarinerSettings Current { get; private set; } = MarinerSettings.Default;

        public event Action<MarinerSettings>? Changed;

        public void Set(MarinerSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
        }
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    /// <summary>The scale's group separator is a narrow no-break space: compare with plain spaces.</summary>
    private static string N(string text) => text.Replace('\u202F', ' ').Replace('\u00A0', ' ');

    private PositionLegendService Position(LegendService legend) =>
        new(_f.Loader, _f.Datasets, _f.Viewport, _mariner, depthBasis: null, legend, action => action(), read => Task.FromResult(read()));

    /// <summary>
    /// A loaded S-101 cell over <paramref name="box"/>, drawn for
    /// <paramref name="minimumDisplayScale"/> to <paramref name="maximumDisplayScale"/>.
    /// </summary>
    private DatasetEntry Cell(
        string name,
        (double South, double West, double North, double East) box,
        int minimumDisplayScale,
        int? maximumDisplayScale,
        int? datum = Mllw,
        string edition = "4")
    {
        var ring = Synth.Box(box.South, box.West, box.North, box.East);
        var coverage = new Dictionary<string, string> { ["minimumDisplayScale"] = minimumDisplayScale.ToString(CultureInfo.InvariantCulture) };
        if (maximumDisplayScale is int maximum)
            coverage["maximumDisplayScale"] = maximum.ToString(CultureInfo.InvariantCulture);
        var areas = new List<Synth.AreaFeature> { new("DataCoverage", ring, coverage), Synth.DepthArea(5, ring) };
        if (datum is int code)
            areas.Add(Synth.SoundingDatum(code, ring));

        var path = Path.Combine(_directory, name + ".000");
        var identification = new S101DatasetIdentification { DatasetName = name + ".000", DatasetEdition = edition, DatasetReferenceDate = "20260915" };
        S101DocumentWriter.WriteToFile(path, Synth.Cell(areas, identification: identification).Document);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S101DatasetProcessor(path, catalogues, TestLuaEngine.Create(), new FeatureCatalogueManager());

        var entry = _f.Datasets.Add(path, "S-101");
        ((Dictionary<DatasetEntry, IDatasetProcessor>)_f.Loader.Processors).Add(entry, processor);
        return entry;
    }

    [Fact]
    public void The_s101_cell_reports_its_edition_and_compilation_scale()
    {
        var entry = Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, 12_000);
        IChartDepthCell cell = (IChartDepthCell)_f.Loader.Processors[entry];

        Assert.Equal(new ChartCellEdition(4, 0, new DateOnly(2026, 9, 15)), cell.Edition);
        Assert.Equal(12_000, cell.CompilationScale);
    }

    [Fact]
    public void It_shows_the_settings_then_the_cell_at_the_view_centre()
    {
        var entry = Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, 12_000);
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        using var legend = _f.Create();
        using var position = Position(legend);

        var current = position.Current;
        Assert.Equal(0.05, current.Latitude!.Value, 6);
        Assert.Equal(0.05, current.Longitude!.Value, 6);
        var cell = Assert.IsType<PositionLegendCell>(current.Cell);
        Assert.Equal(entry.Id.Value, cell.DatasetId);
        Assert.Equal(4, cell.Edition!.Number);
        Assert.Equal(12_000, cell.CompilationScale);
        Assert.Equal(Mllw, current.DatumCode);

        // The fixture's view is 10 m a pixel: 1:35 714 at the 0.28 mm pixel.
        Assert.Equal("Depths in metres · Mean Lower Low Water · Safety contour 30 m", N(PositionLegendText.SettingsLine(current)));
        Assert.Equal(entry.DisplayName + " ed. 4 · compiled 1:12 000 · shown 1:35 700", N(PositionLegendText.CellLine(current)));
        Assert.Null(current.OverscaleFactor);
    }

    [Fact]
    public void The_finest_cell_covering_the_centre_wins_and_overscale_is_shown()
    {
        Cell("101US00COAST", (0, 0, 1, 1), 350_000, 90_000, edition: "2");
        var harbour = Cell("101US00HARB", (0, 0, 0.1, 0.1), 45_000, 22_000);
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        using var legend = _f.Create();
        using var position = Position(legend);

        Assert.Equal(harbour.Id.Value, position.Current.Cell!.DatasetId);
        // Shown at 1:35 714 against a cell compiled for 1:22 000: not overscaled.
        Assert.Null(position.Current.OverscaleFactor);

        // Zoom in to 1:8 929 (2.5 m a pixel): 2.5× overscale.
        _f.Viewport.Publish(0.045, 0.045, 0.055, 0.055, mercatorResolution: 2.5);
        _f.Clock.Advance(LegendService.PanSettleDelay);

        Assert.Equal(22_000 / (2.5 / 0.00028 * Math.Cos(0.05 * Math.PI / 180)), position.Current.OverscaleFactor!.Value, 3);
        Assert.Matches(@"· shown 1:8 930 · 2[.,]5× overscale$", N(PositionLegendText.CellLine(position.Current)));
    }

    [Fact]
    public void It_updates_on_pan_end_and_says_no_chart_over_no_chart()
    {
        Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, 12_000);
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        using var legend = _f.Create();
        using var position = Position(legend);
        var changes = 0;
        position.Changed += (_, _) => changes++;
        Assert.NotNull(position.Current.Cell);

        // Panning away: nothing changes until the view has been still for the settle delay.
        _f.Viewport.Publish(10.04, 10.04, 10.06, 10.06);
        _f.Clock.Advance(LegendService.PanSettleDelay / 2);
        Assert.NotNull(position.Current.Cell);
        Assert.Equal(0, changes);

        _f.Clock.Advance(LegendService.PanSettleDelay);

        Assert.Equal(1, changes);
        Assert.Null(position.Current.Cell);
        Assert.False(position.Current.HasDatum);
        Assert.Equal("Depths in metres · Safety contour 30 m", N(PositionLegendText.SettingsLine(position.Current)));
        Assert.Equal("No chart at the view centre", PositionLegendText.CellLine(position.Current));
    }

    [Fact]
    public void A_hidden_cell_is_not_the_chart_at_the_centre()
    {
        var entry = Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, 12_000);
        entry.IsVisible = false;
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        using var legend = _f.Create();
        using var position = Position(legend);

        Assert.Null(position.Current.Cell);
    }

    [Fact]
    public void A_cell_without_a_sounding_datum_says_so()
    {
        Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, maximumDisplayScale: null, datum: null, edition: "");
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        using var legend = _f.Create();
        using var position = Position(legend);

        Assert.Null(position.Current.Cell!.Edition);
        Assert.Null(position.Current.Cell.CompilationScale);
        Assert.Equal("Depths in metres · Datum not declared · Safety contour 30 m", N(PositionLegendText.SettingsLine(position.Current)));
        Assert.Equal("101US00TEST.000 shown 1:35 700", N(PositionLegendText.CellLine(position.Current)));
    }

    [Fact]
    public void It_follows_the_mariner_settings()
    {
        using var legend = _f.Create();
        using var position = Position(legend);

        _mariner.Set(MarinerSettings.Default with { DepthUnit = DepthUnit.Feet, SafetyContour = Depth.FromFeet(30) });

        Assert.Equal("Depths in feet · Safety contour 30 ft", N(PositionLegendText.SettingsLine(position.Current)));
        Assert.Equal(DepthUnit.Feet, position.Current.DepthUnit);
    }

    [Fact]
    public void The_row_names_itself_and_splits_the_cell_line()
    {
        var entry = Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, 12_000);
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        using var legend = _f.Create();
        using var position = Position(legend);
        using var row = new PositionLegendRowViewModel(position);

        Assert.True(row.HasCell);
        Assert.Equal(entry.DisplayName, row.CellName);
        Assert.Equal(" ed. 4 · compiled 1:12 000 · shown 1:35 700", N(row.CellDetail));
        Assert.StartsWith("Position legend at the view centre: Depths in metres", row.AccessibleName, StringComparison.Ordinal);

        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _f.Viewport.Publish(10.04, 10.04, 10.06, 10.06);
        _f.Clock.Advance(LegendService.PanSettleDelay);

        Assert.NotEmpty(raised);
        Assert.False(row.HasCell);
        Assert.Equal(string.Empty, row.CellName);
        Assert.Equal("No chart at the view centre", row.CellDetail);
    }

    [Fact]
    public async Task Get_legend_returns_the_position_row()
    {
        var entry = Cell("101US00TEST", (0, 0, 0.1, 0.1), 22_000, 12_000);
        using var legend = _f.Create();
        using var position = Position(legend);
        var tool = new GetLegendTool(legend, position, new InlineInvoker());

        // get_legend takes the current view, without waiting for pan end.
        _f.Viewport.Publish(0.04, 0.04, 0.06, 0.06);
        Assert.True((await tool.InvokeAsync(ct: TestContext.Current.CancellationToken)).TryGetValue(out var dto));

        var row = dto!.Position;
        Assert.Equal(35_714, row.DisplayScale);
        Assert.Equal("Metres", row.DepthUnit);
        Assert.Equal(30, row.SafetyContourMetres);
        Assert.Equal(Mllw, row.DatumCode);
        Assert.Equal("Mean Lower Low Water", row.Datum);
        Assert.Null(row.OverscaleFactor);
        var cell = Assert.IsType<PositionLegendCellDto>(row.Cell);
        Assert.Equal(entry.Id.Value, cell.DatasetId);
        Assert.Equal("S-101", cell.Spec);
        Assert.Equal(4, cell.Edition);
        Assert.Equal(0, cell.Update);
        Assert.Equal("2026-09-15", cell.IssueDate);
        Assert.Equal(12_000, cell.CompilationScale);
        Assert.Equal(
            ["Depths in metres · Mean Lower Low Water · Safety contour 30 m", entry.DisplayName + " ed. 4 · compiled 1:12 000 · shown 1:35 700"],
            row.Lines.Select(N));

        // Over no chart, the cell is null and the row says so.
        _f.Viewport.Publish(10.04, 10.04, 10.06, 10.06);
        Assert.True((await tool.InvokeAsync(ct: TestContext.Current.CancellationToken)).TryGetValue(out dto));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, McpAdapterShared.Options));
        var position2 = json.RootElement.GetProperty("position");
        Assert.False(position2.TryGetProperty("cell", out _)); // null, so not written
        Assert.Equal("Metres", position2.GetProperty("depthUnit").GetString());
        Assert.Equal("No chart at the view centre", position2.GetProperty("lines")[1].GetString());
    }
}
