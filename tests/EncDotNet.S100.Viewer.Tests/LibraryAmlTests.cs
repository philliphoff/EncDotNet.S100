using Avalonia.VisualTree;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// NATO Additional Military Layers cells in the Library (#1370): tagged and
/// marked rows, their own group below the charts, the AML filter chip and the
/// text filter, and their details. Items carry synthetic AML metadata.
/// </summary>
public sealed class LibraryAmlTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly RecordingLibraryImporter _importer = new();
    private readonly FakeLibraryLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();
    private CollectionLibrary? _library;

    public void Dispose()
    {
        _library?.Dispose();
        _context.Dispose();
    }

    private async Task<LibraryPanelViewModel> CreateAsync(params CollectionItem[] items)
    {
        _library = _context.CreateService(new CollectionIndexer([new FixedIndexer(items)]));
        _library.Initialize();
        _library.AddCollection("Mixed", [new LocalFolderSource(Guid.NewGuid(), null, _context.Root)]);
        await _library.WhenIdle();
        return TestLibrary.Panel(_library, _importer, _loader, _downloader, action => action(), action => action(),
            TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);
    }

    private static CollectionItem Enc(string name) => new()
    {
        Key = name,
        ProductSpec = "S-57",
        Name = name,
        Location = NoItemLocation.Instance,
    };

    private static CollectionItem Aml(
        string name, AmlProduct product, string? code, string? marking = "UNCLASSIFIED", string version = "1.0",
        AmlMarkingSource markingSource = AmlMarkingSource.Header, int? band = 6)
    {
        AmlCellName.TryParse(name, out var fromName);
        var info = new AmlDatasetInfo
        {
            DataSetName = name,
            Product = product,
            ProductSpecification = (int)product,
            ProductCode = code,
            ProductName = AmlProducts.Name(code) ?? AmlProducts.IntegratedName,
            Version = version,
            Title = "North Atlantic Ocean",
            ProtectiveMarking = marking,
            MarkingSource = markingSource,
            IdoStatus = "NATO: North Atlantic Treaty Organisation",
            OwnerAuthority = "NO",
            ScaleBand = band,
            FromName = fromName,
        };
        var properties = new Dictionary<string, string>();
        AmlLibraryProperties.Write(info, properties);
        return new CollectionItem
        {
            Key = name,
            ProductSpec = AmlDatasetInfo.SpecName,
            Name = name,
            Title = info.Title,
            Location = NoItemLocation.Instance,
            Properties = properties,
        };
    }

    private static readonly CollectionItem[] Mixed =
    [
        Aml("UKC6U114.000", AmlProduct.CLB, "CLB"),
        Enc("GB4X0000"),
        Aml("NOC6RA3B.000", AmlProduct.CLB, "CLB", marking: "RESTRICTED"),
        Aml("UKL0U100.000", AmlProduct.LBO, "LBO", band: 0),
        Aml("AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000", AmlProduct.Integrated, "CLB", marking: "NATO RESTRICTED", version: "3.0", band: 7),
        Aml("AMLDEU7U_NKKK0000_TrialData.000", AmlProduct.Integrated, null, version: "3.0", band: 7),
        Enc("GB5X0000"),
    ];

    [Fact]
    public async Task Rows_carry_the_AML_tag_and_a_marking_only_above_unclassified()
    {
        using var vm = await CreateAsync(Mixed);

        string Tag(string name) => Assert.Single(Row(vm, name).Tags, t => t.Kind == LibraryTagKind.Military).Text;
        Assert.Equal("AML · CLB", Tag("UKC6U114.000"));
        Assert.Equal("AML · LBO", Tag("UKL0U100.000"));
        Assert.Equal("AML 3.0 · CLB", Tag("AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000"));
        Assert.Equal("AML 3.0", Tag("AMLDEU7U_NKKK0000_TrialData.000"));
        Assert.StartsWith("Additional Military Layers · Contour Line Bathymetry", Assert.Single(Row(vm, "UKC6U114.000").Tags).ToolTip);

        Assert.False(Row(vm, "UKC6U114.000").HasAmlRowMarking);
        Assert.Equal("RESTRICTED", Row(vm, "NOC6RA3B.000").AmlRowMarking);
        Assert.Equal("NATO RESTRICTED", Row(vm, "AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000").AmlRowMarking);
        Assert.False(Row(vm, "GB4X0000").IsAml);
        Assert.False(Row(vm, "GB4X0000").HasAmlRowMarking);
    }

    [Fact]
    public async Task AML_cells_get_their_own_group_below_the_charts()
    {
        using var vm = await CreateAsync(Mixed);

        Assert.Equal(["GB4X0000", "GB5X0000"], vm.Items.Take(2).Select(i => i.Name));
        var header = vm.Items[2];
        Assert.True(header.IsAmlSection);
        Assert.True(header.IsGroupHeader);
        Assert.Equal("ADDITIONAL MILITARY LAYERS · 5", header.SectionHeader);
        Assert.Empty(header.Tags);
        Assert.False(header.CanLoad);
        Assert.All(vm.Items.Skip(3), i => Assert.True(i.IsAml));
        Assert.Equal(5, vm.Items.Count(i => i.IsAml));
        Assert.Equal("7", vm.ItemsSummary);  // datasets, never the header

        header.ToggleCommand!.Execute(null);
        Assert.Equal(3, vm.Items.Count);
        Assert.False(vm.Items[2].IsExpanded);
        Assert.Equal("7", vm.ItemsSummary);

        // A text match inside the collapsed group shows it.
        vm.FilterText = "UKL0";
        Assert.Equal(["ADDITIONAL MILITARY LAYERS · 1", null], vm.Items.Select(i => i.SectionHeader));

        vm.FilterText = string.Empty;
        vm.Items[2].ToggleCommand!.Execute(null);
        Assert.Equal(8, vm.Items.Count);
    }

    [Fact]
    public async Task A_library_without_AML_has_no_group_and_no_AML_chip()
    {
        using var vm = await CreateAsync(Enc("GB4X0000"), Enc("GB5X0000"));

        Assert.False(vm.HasAml);
        Assert.DoesNotContain(vm.Items, i => i.IsGroupHeader);
    }

    [Fact]
    public async Task The_AML_chip_filters_by_product()
    {
        using var vm = await CreateAsync(Mixed);
        Assert.True(vm.HasAml);
        Assert.False(vm.IsAmlFilterActive);
        Assert.Equal("AML", vm.AmlFilterText);

        vm.SetAmlFilterCommand.Execute(LibraryPanelViewModel.AmlFilterAllProducts);
        Assert.True(vm.IsAmlFilterActive);
        Assert.Equal("AML · All products", vm.AmlFilterText);
        Assert.Equal(5, Cells(vm).Length);
        Assert.Equal(5, vm.AllCount);
        Assert.StartsWith("5 of 7", vm.ItemsSummary);

        // A product takes its 3.0 cells too.
        vm.SetAmlFilterCommand.Execute("CLB");
        Assert.Equal("AML · CLB", vm.AmlFilterText);
        Assert.Equal(["UKC6U114.000", "NOC6RA3B.000", "AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000"], Cells(vm));

        vm.SetAmlFilterCommand.Execute("3.0");
        Assert.Equal(["AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000", "AMLDEU7U_NKKK0000_TrialData.000"], Cells(vm));

        vm.SetAmlFilterCommand.Execute("SBO");
        Assert.Empty(Cells(vm));

        vm.SetAmlFilterCommand.Execute(LibraryPanelViewModel.AmlFilterOff);
        Assert.False(vm.IsAmlFilterActive);
        Assert.Equal(7, Cells(vm).Length);
    }

    [Theory]
    [InlineData("AML", 5)]
    [InlineData("aml 3.0", 2)]
    [InlineData("LBO", 1)]
    [InlineData("clb", 3)]
    public async Task The_text_filter_matches_AML_and_product_codes(string filter, int count)
    {
        using var vm = await CreateAsync(Mixed);

        vm.FilterText = filter;

        Assert.Equal(count, Cells(vm).Length);
        Assert.All(Cells(vm), name => Assert.True(Row(vm, name).IsAml));
    }

    [Fact]
    public async Task Details_say_what_AML_is_and_decode_the_cell_name()
    {
        using var vm = await CreateAsync(Mixed);
        var row = Row(vm, "NOC6RA3B.000");

        Assert.True(row.HasAmlNote);
        Assert.Equal(
            S100Products.AmlPortrayal.Draws
                ? "Military overlay data, not a chart."
                : "Military overlay data, not a chart. SoundCharts shows where it is but doesn't draw it.",
            row.AmlNote);
        Assert.False(Row(vm, "GB4X0000").HasAmlNote);

        var aml = row.Details[0];
        Assert.Equal("Additional Military Layers", aml.Title);
        Assert.Equal(
            [
                ("Product", "CLB · Contour Line Bathymetry"),
                ("Version", "AML 1.0"),
                ("Protective marking", "RESTRICTED"),
                ("IDO status", "NATO: North Atlantic Treaty Organisation"),
                ("Owner authority", "NO"),
                ("Scale band", "Band 6"),
            ],
            aml.Fields.Select(f => (f.Label, f.Value)));

        var fromName = row.Details[1];
        Assert.Equal("From the cell name", fromName.Title);
        Assert.Equal(
            [
                ("Producer", "NO"),
                ("Product", "C · CLB · matches the header"),
                ("Scale band", "6 · matches the header"),
                ("Protective marking", "R · RESTRICTED · matches the header"),
            ],
            fromName.Fields.Select(f => (f.Label, f.Value)));

        // The AML facts aren't repeated as raw source properties.
        Assert.DoesNotContain(row.Details.SelectMany(g => g.Fields), f => f.Label.StartsWith("AML ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_AML_group_header_has_details_of_its_own()
    {
        using var vm = await CreateAsync(Mixed);

        var group = Assert.Single(vm.Items[2].Details);

        Assert.Equal("Additional Military Layers", group.Title);
        Assert.Contains(group.Fields, f => f.Label == "Cells" && f.Value == "5");
    }

    [AvaloniaFact]
    public async Task A_row_shows_its_marking_readably_before_its_AML_tag()
    {
        using var vm = await CreateAsync(Mixed);
        using var host = Headless.ViewHost.Show(new Views.LibraryPanelView { DataContext = vm }, width: 420, height: 900);
        vm.SelectedItem = Row(vm, "NOC6RA3B.000");
        host.Settle();

        var chips = host.All<Views.AmlMarkingChip>().Where(c => c.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(chips);
        foreach (var chip in chips)
        {
            var border = chip.GetVisualDescendants().OfType<Avalonia.Controls.Border>().Single(b => b.Name == "PART_Chip");
            var text = chip.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(t => t.Name == "PART_Text");
            Assert.Equal("RESTRICTED", text.Text);
            AmlChipsViewTests.AssertReadable(border, text);
        }
    }

    [AvaloniaFact]
    public async Task The_AML_chip_reads_as_solid_ink_while_it_filters()
    {
        using var vm = await CreateAsync(Mixed);
        using var host = Headless.ViewHost.Show(new Views.LibraryPanelView { DataContext = vm }, width: 420, height: 900);

        vm.SetAmlFilterCommand.Execute("LBO");
        host.Settle();

        var chip = host.Find<Avalonia.Controls.Button>("Library.AmlFilter");
        var text = host.Find<Avalonia.Controls.TextBlock>("Library.AmlFilter.Text");
        Assert.Equal("AML · LBO", text.Text);
        var border = chip.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
            .First(b => b.Background is Avalonia.Media.ISolidColorBrush { Color.A: 255 });
        AmlChipsViewTests.AssertReadable(border, text);
    }

    private static LibraryItemViewModel Row(LibraryPanelViewModel vm, string name) =>
        vm.Items.Single(i => !i.IsGroupHeader && i.Name == name);

    private static string[] Cells(LibraryPanelViewModel vm) =>
        [.. vm.Items.Where(i => !i.IsGroupHeader).Select(i => i.Name)];

    /// <summary>Indexes every source as the given items.</summary>
    private sealed class FixedIndexer(IReadOnlyList<CollectionItem> items) : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => true;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, items, []));
    }
}
