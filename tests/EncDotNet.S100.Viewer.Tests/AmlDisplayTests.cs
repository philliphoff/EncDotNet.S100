using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using EncDotNet.S57;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The display panel's Additional Military Layers section (#1374, AML handoff
/// decisions 23, 24 and 26): product switches, scale bands, legend, CLB chip,
/// persistence and the MCP tools. The cells are synthetic.
/// </summary>
public sealed class AmlDisplayTests : IDisposable
{
    private const int Clb = 54;
    private const int Ral = 52;

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("aml-display-");
    private readonly FakeDatasetLoaderService _loader = new();
    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = [];
    private readonly FakeNotifier _viewport = new();
    private readonly EcdisDisplayState _state = new();
    private readonly DatasetsViewModel _datasets;

    public AmlDisplayTests()
    {
        _loader.Processors = _processors;
        _datasets = new DatasetsViewModel(_loader);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }

        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        // A viewport over the synthetic cells at 1:scale.
        public void ZoomTo(double scale)
        {
            const double latitude = 50.05;
            Current = new MapViewportSnapshot
            {
                MinLatitude = latitude - 0.01,
                MinLongitude = -1.06,
                MaxLatitude = latitude + 0.01,
                MaxLongitude = -1.04,
                MercatorResolution = MapScaleFormatter.ScaleDenominatorToResolution(scale, latitude),
            };
            ViewportChanged?.Invoke(this, Current);
        }
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private AmlDisplayService CreateService() => new(_datasets, _loader, _state, _viewport, action => action());

    // Loads a synthetic AML cell of product PRSP productSpecification in scale band band.
    private DatasetEntry Load(string name, int productSpecification, int band)
    {
        var path = SyntheticAmlCell.Write(_dir.FullName, name, new SyntheticAmlCell.Options
        {
            ProductSpecification = productSpecification,
            IntendedUsage = 100 + band,
            Comment = SyntheticAmlCell.UkhoComment(),
        });
        return Load(path);
    }

    private DatasetEntry Load(string path)
    {
        var processor = new AmlDatasetProcessor(path, AmlOverlayPortrayal.Default);
        var entry = _datasets.Add(path, AmlDatasetInfo.SpecName);
        entry.SetLoadedState(processor.Metadata);
        _processors[entry] = processor;
        _loader.RaiseLoaded(entry);
        return entry;
    }

    // A CLB cell with drawn (RAL-styled) features, so the CLB chip has something to stand for.
    private DatasetEntry LoadDrawnClb()
    {
        var document = AmlFixtures.RalBuilder().BuildDocument();
        var dsid = document.DataSetIdentification!;
        var clb = new S57Document
        {
            DataSetIdentification = new S57DataSetIdentification
            {
                RecordName = dsid.RecordName,
                ExchangePurpose = dsid.ExchangePurpose,
                IntendedUsage = 106,
                DataSetName = "UKC6DRAWN.000",
                EditionNumber = dsid.EditionNumber,
                UpdateNumber = dsid.UpdateNumber,
                UpdateApplicationDate = dsid.UpdateApplicationDate,
                IssueDate = dsid.IssueDate,
                S57EditionNumber = dsid.S57EditionNumber,
                ProductSpecification = Clb,
                ProductSpecificationEdition = dsid.ProductSpecificationEdition,
                ApplicationProfile = 4,
                ProducingAgency = dsid.ProducingAgency,
                DataStructure = dsid.DataStructure,
                AttfLexicalLevel = dsid.AttfLexicalLevel,
                NatfLexicalLevel = dsid.NatfLexicalLevel,
                Comment = dsid.Comment,
            },
            DataSetParameters = document.DataSetParameters,
            VectorRecords = document.VectorRecords,
            FeatureRecords = document.FeatureRecords,
        };
        var path = Path.Combine(_dir.FullName, "UKC6DRAWN.000");
        S57DocumentWriter.WriteToFile(path, clb);
        return Load(path);
    }

    [Fact]
    public void Turning_a_product_off_hides_only_that_products_cells()
    {
        var clb = Load("UKC6U001.000", Clb, 6);
        var ral = Load("UKR6U001.000", Ral, 6);
        using var service = CreateService();
        _viewport.ZoomTo(50_000);

        service.SetProductVisible("clb", false);

        Assert.True(clb.IsHiddenByDisplay);
        Assert.False(clb.MapDataset!.IsVisible);
        // The Datasets row's own eye keeps its state.
        Assert.True(clb.IsVisible);
        Assert.False(ral.IsHiddenByDisplay);
        Assert.True(ral.MapDataset!.IsVisible);
        Assert.Contains("CLB", _state.HiddenAmlProducts);

        service.SetProductVisible("CLB", true);

        Assert.False(clb.IsHiddenByDisplay);
        Assert.True(clb.MapDataset!.IsVisible);
    }

    [Fact]
    public void Zooming_across_band_boundaries_shows_the_expected_bands()
    {
        // The two cells share their coverage, so band 6 covers band 5.
        var band5 = Load("UKC5U001.000", Clb, 5);
        var band6 = Load("UKC6U001.000", Clb, 6);
        using var service = CreateService();

        // Past band 5's coarsest scale (1:625 000): neither.
        _viewport.ZoomTo(1_000_000);
        Assert.True(band5.IsHiddenByDisplay);
        Assert.True(band6.IsHiddenByDisplay);

        // Band 5's range only.
        _viewport.ZoomTo(300_000);
        Assert.False(band5.IsHiddenByDisplay);
        Assert.True(band6.IsHiddenByDisplay);

        // The overlap (1:100 000–1:125 000): the finer band wins.
        _viewport.ZoomTo(110_000);
        Assert.True(band5.IsHiddenByDisplay);
        Assert.False(band6.IsHiddenByDisplay);

        // Zoomed in past band 6, it still shows.
        _viewport.ZoomTo(5_000);
        Assert.False(band6.IsHiddenByDisplay);

        // Off: everything at every scale.
        service.SetFollowScaleBands(false);
        _viewport.ZoomTo(1_000_000);
        Assert.False(band5.IsHiddenByDisplay);
        Assert.False(band6.IsHiddenByDisplay);
    }

    [Fact]
    public void A_cell_hidden_by_its_row_doesnt_hide_coarser_cells()
    {
        var band5 = Load("UKC5U001.000", Clb, 5);
        var band6 = Load("UKC6U001.000", Clb, 6);
        using var service = CreateService();
        _viewport.ZoomTo(110_000);
        Assert.True(band5.IsHiddenByDisplay);

        band6.IsVisible = false;

        Assert.False(band5.IsHiddenByDisplay);
    }

    [Fact]
    public void Products_list_loaded_ones_first_with_their_cell_counts()
    {
        Load("UKR6U001.000", Ral, 6);
        Load("UKC6U001.000", Clb, 6);
        Load("UKC6U002.000", Clb, 6);
        using var service = CreateService();
        using var section = new AmlDisplaySectionViewModel(service, AmlStyleCatalog.Default);

        Assert.True(section.IsShown);
        Assert.Equal(["CLB", "RAL", "ESB", "LBO", "SBO", "MFF", "3.0"], section.Products.Select(p => p.Code));
        Assert.Equal("2 cells", section.Products[0].CountText);
        Assert.Equal("1 cell", section.Products[1].CountText);
        Assert.Equal(Strings.AmlDisplay_NotLoaded, section.Products[2].CountText);
        Assert.False(section.Products[2].IsLoaded);
        Assert.Equal("Contour Line Bathymetry", section.Products[0].Name);
        Assert.Equal(AmlStyleCatalog.NoChecksNote, section.NoChecksNote);
    }

    [Fact]
    public void The_section_hides_without_aml_and_the_switch_row_drives_the_service()
    {
        using var service = CreateService();
        using var section = new AmlDisplaySectionViewModel(service, AmlStyleCatalog.Default);
        Assert.False(section.IsShown);

        var clb = Load("UKC6U001.000", Clb, 6);
        Assert.True(section.IsShown);

        section.Products.Single(p => p.Code == "CLB").IsVisible = false;
        Assert.True(clb.IsHiddenByDisplay);
        Assert.False(section.Products.Single(p => p.Code == "CLB").IsVisible);

        _datasets.Entries.Remove(clb);
        Assert.False(section.IsShown);
        Assert.False(clb.IsHiddenByDisplay);
    }

    [Fact]
    public void The_legend_lists_every_class_the_styles_draw()
    {
        using var service = CreateService();
        using var section = new AmlDisplaySectionViewModel(service, AmlStyleCatalog.Default);

        var labels = section.Legend.SelectMany(g => g.Entries).Select(e => e.Label).ToList();

        Assert.Equal(AmlStyleCatalog.Default.Classes.Select(c => c.LegendLabel), labels);
        Assert.Equal(
            AmlStyleCatalog.Default.BuildLegend(EncDotNet.S100.Pipelines.PaletteType.Day).Select(g => g.Family.Name),
            section.Legend.Select(g => g.Name));
    }

    [Fact]
    public void The_clb_chip_shows_while_a_drawn_clb_cell_is_shown()
    {
        using var service = CreateService();
        using var section = new AmlDisplaySectionViewModel(service, AmlStyleCatalog.Default);
        _viewport.ZoomTo(50_000);
        Assert.False(section.IsClbChipShown);

        LoadDrawnClb();
        Assert.True(section.IsClbChipShown);

        service.SetProductVisible("CLB", false);
        Assert.False(section.IsClbChipShown);
    }

    [Fact]
    public void The_switches_persist()
    {
        var path = Path.Combine(_dir.FullName, "settings.json");
        var state = EcdisDisplayStatePersistence.Create(ViewerSettings.Load(path));
        Assert.True(state.FollowAmlScaleBands);
        Assert.Empty(state.HiddenAmlProducts);

        state.SetAmlProductVisible("CLB", false);
        state.SetAmlProductVisible("3.0", false);
        state.SetFollowAmlScaleBands(false);

        var reloaded = ViewerSettings.Load(path);
        Assert.Equal("3.0,CLB", reloaded.EcdisHiddenAmlProducts);
        Assert.False(reloaded.EcdisFollowAmlScaleBands);

        var restored = EcdisDisplayStatePersistence.Create(reloaded);
        Assert.Equal(["3.0", "CLB"], restored.HiddenAmlProducts.Order(StringComparer.Ordinal));
        Assert.False(restored.FollowAmlScaleBands);
    }

    [Fact]
    public async Task Mcp_sets_product_visibility_and_the_scale_band_option()
    {
        var clb = Load("UKC6U001.000", Clb, 6);
        using var service = CreateService();
        _viewport.ZoomTo(50_000);
        var tools = new AmlDisplayTools(service, new InlineInvoker());

        var set = await tools.SetAsync(new Dictionary<string, bool> { ["clb"] = false }, followScaleBands: false, TestContext.Current.CancellationToken);

        var dto = Assert.IsType<ToolResult<AmlDisplayDto>.OkResult>(set).Value;
        Assert.True(dto.SectionShown);
        Assert.False(dto.FollowScaleBands);
        Assert.Equal(50_000, dto.ScaleDenominator!.Value, 0);
        var row = dto.Products.Single(p => p.Code == "CLB");
        Assert.False(row.Visible);
        Assert.Equal(1, row.CellCount);
        Assert.Equal(0, row.ShownCount);
        Assert.True(clb.IsHiddenByDisplay);

        var get = await tools.GetAsync(TestContext.Current.CancellationToken);
        Assert.False(Assert.IsType<ToolResult<AmlDisplayDto>.OkResult>(get).Value.Products.Single(p => p.Code == "CLB").Visible);
    }

    [Fact]
    public async Task Mcp_rejects_an_unknown_product_and_changes_nothing()
    {
        using var service = CreateService();
        var tools = new AmlDisplayTools(service, new InlineInvoker());

        var result = await tools.SetAsync(new Dictionary<string, bool> { ["CLB"] = false, ["XYZ"] = false }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<ToolResult<AmlDisplayDto>.ErrResult>(result);
        Assert.Empty(_state.HiddenAmlProducts);
    }

    [AvaloniaFact]
    public void The_section_shows_below_display_planes_only_with_aml()
    {
        using var service = CreateService();
        using var section = new AmlDisplaySectionViewModel(service, AmlStyleCatalog.Default);
        using var panel = new EcdisDisplayPanelViewModel(_state, new PortrayalCatalogueManager(), _datasets, labelOverrides: new EcdisLabelOverrideProvider(), displayModeToolbar: null, displayCombinations: null, amlDisplay: section);
        Assert.True(panel.IsEmpty);

        Load("UKC6U001.000", Clb, 6);
        Assert.False(panel.IsEmpty);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 900);

        var sectionPanel = host.Find<StackPanel>("EcdisDisplayPanel.Aml");
        Assert.True(sectionPanel.IsEffectivelyVisible);
        var follow = host.Find<CheckBox>("EcdisDisplayPanel.Aml.FollowScaleBands");
        Assert.True(follow.IsChecked);
        Assert.Equal(Strings.Tooltip_AmlDisplay_FollowScaleBands, AutomationProperties.GetHelpText(follow));
        var legend = host.Find<Button>("EcdisDisplayPanel.Aml.Legend");
        Assert.Equal(Strings.AmlDisplay_LegendTitle, AutomationProperties.GetName(legend));
    }
}
