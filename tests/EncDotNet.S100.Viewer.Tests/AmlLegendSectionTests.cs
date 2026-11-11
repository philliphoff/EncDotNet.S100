using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Datasets.Aml.Families;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S57.Aml;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Legend tab's AML section (#1409, legends design note D8, D18): the
/// families the loaded cells draw, one group each in draw order, from the
/// sub-layers' legends (<see cref="AmlStyleCatalog.BuildLegend(AmlFamily, PaletteType)"/>),
/// and the display section's <b>Legend</b> link that opens it.
/// </summary>
public sealed class AmlLegendSectionTests
{
    private static readonly AmlPortrayal Portrayal = new();

    // RAL (air and operational), CLB (bathymetry) and an AML 3.0 sediment cell
    // (environmental, with the ESB sediment legend), as the loader stacks them:
    // bottom first, by plane and priority.
    private static LegendServiceTests.Fixture ThreeCells(PaletteType palette = PaletteType.Day)
    {
        var f = new LegendServiceTests.Fixture();
        var cells = new (AmlDataSet Cell, string Name)[]
        {
            (AmlFixtures.Ral(), "UKR0TEST.000"),
            (AmlFixtures.Clb(), "UKC6TEST.000"),
            (AmlFixtures.Sed(), "AMLTEST6U_SED.000"),
        };
        var entries = new List<LayerStackEntry>();
        foreach (var (cell, name) in cells)
        {
            f.Datasets.Add("/data/" + name, AmlDatasetInfo.SpecName);
            var result = Portrayal.Draw(cell, name).ToVectorPortrayalResult(new SpecRef(AmlDatasetInfo.SpecName, default), name, new RenderContext { Palette = palette });
            entries.AddRange(result.SubLayers.Select(sub => new LayerStackEntry(
                new MemoryLayer(name),
                new SubLayerStackItem(new VectorStackPayload(result, sub), sub.Plane, sub.WithinPlanePriority, name, sub.SourceFeatureType))));
        }

        f.Loader.SetEntries([.. entries.OrderBy(e => e.Item.Plane).ThenBy(e => e.Item.WithinPlanePriority)]);
        return f;
    }

    private static IReadOnlyList<AmlFamily> DrawnFamilies() =>
        [.. new[] { AmlFixtures.Ral(), AmlFixtures.Clb(), AmlFixtures.Sed() }
            .SelectMany(c => Portrayal.Draw(c).Instructions.Keys)
            .Distinct()];

    [Fact]
    public void One_AML_section_has_a_group_per_drawn_family_in_draw_order()
    {
        var f = ThreeCells();
        using var service = f.Create();

        var section = Assert.Single(service.Sections);
        Assert.Equal(AmlDisplaySectionViewModel.LegendSectionId, section.Id);
        Assert.Equal(Strings.Legend_ProductAml, section.Title);
        Assert.Equal("3 datasets", section.Source);
        Assert.Equal(AmlStyleCatalog.LegendCaption, section.Caption);

        // The groups are the catalogue's legend (top of the plane first), less
        // the families no loaded cell draws, titled by the family's name.
        var drawn = DrawnFamilies();
        var expected = AmlStyleCatalog.Default.BuildLegend(PaletteType.Day).Where(g => drawn.Contains(g.Family)).ToList();
        Assert.True(expected.Count >= 3);
        Assert.Equal(expected.Select(g => g.Family.Name), section.Groups.Select(g => g.Title));
        Assert.Equal(expected.Select(g => g.Family.LayerKey), section.Groups.Select(g => g.Key));
        foreach (var (group, legend) in section.Groups.Zip(expected))
            Assert.Equal(legend.Entries.Select(e => e.Label), group.Rows.Select(r => r.Label));

        // The environmental family carries the ESB sediment legend.
        var environmental = Assert.Single(section.Groups, g => g.Key == AmlFamily.Environmental.LayerKey);
        var sediment = EsbSediment.BuildLegend(AmlStyleCatalog.Default, PaletteType.Day);
        Assert.NotEmpty(sediment);
        Assert.All(sediment, e => Assert.Contains(e.Label, environmental.Rows.Select(r => r.Label)));
    }

    [Fact]
    public void Each_family_is_listed_once_however_many_cells_draw_it()
    {
        var f = ThreeCells();
        var doubled = f.Loader.CurrentStackEntries.Concat(f.Loader.CurrentStackEntries).OrderBy(e => e.Item.Plane).ThenBy(e => e.Item.WithinPlanePriority).ToArray();
        f.Loader.SetEntries(doubled);
        using var service = f.Create();

        var section = Assert.Single(service.Sections);
        Assert.Equal(section.Groups.Select(g => g.Key).Distinct(), section.Groups.Select(g => g.Key));
        Assert.Equal(DrawnFamilies().Count, section.Groups.Count);
    }

    [Fact]
    public void The_rows_sit_on_the_palette_tile_and_follow_the_palette()
    {
        var f = ThreeCells(PaletteType.Night);
        using var service = f.Create(PaletteType.Night);
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))),
            new FixedTimeFormat(),
            find: null);

        var section = Assert.Single(tab.Sections);
        var (red, green, blue) = Tools.MeasureOverlayAppearance.ChartBackgroundFor(PaletteType.Night);
        Assert.All(section.Rows, r => Assert.Equal(
            Avalonia.Media.Color.FromRgb(red, green, blue),
            Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(r.Tile).Color));
        Assert.All(section.Groups, g => Assert.True(g.ShowTitle));
    }

    [Fact]
    public void The_display_sections_Legend_link_opens_the_tab_at_the_AML_section()
    {
        var f = ThreeCells();
        using var service = f.Create();
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))),
            new FixedTimeFormat(),
            find: null);
        var opened = 0;
        ((IActivityTabContentSignal)tab).ContentBecameAvailable += (_, _) => opened++;
        LegendRevealEventArgs? reveal = null;
        tab.RevealRequested += (_, e) => reveal = e;
        tab.CollapseAllCommand.Execute(null);
        tab.Filter = "no such row";

        using var aml = new AmlDisplayService(new EcdisDisplayState(), new LegendServiceTests.FakeNotifier(), action => action());
        using var display = new AmlDisplaySectionViewModel(aml, id => service.Show(id));
        display.ShowLegendCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.Equal(string.Empty, tab.Filter);
        var section = Assert.Single(tab.Sections);
        Assert.Equal(AmlDisplaySectionViewModel.LegendSectionId, section.Id);
        Assert.True(section.IsExpanded);
        Assert.Same(section, reveal?.Section);
    }

    private sealed class FixedTimeFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
