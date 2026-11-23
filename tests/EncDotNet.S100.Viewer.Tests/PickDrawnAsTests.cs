using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Object information's Drawn as and header glyph (symbol dictionary R10,
/// #1679): the picked feature's drawn parts, read from the stack's display
/// list, on the S-57 cell US5MA1BO drawn with the S-101 catalogue.
/// </summary>
public sealed class PickDrawnAsTests
{
    private static readonly Lazy<Task<Cell>> Portrayed = new(PortrayAsync);

    /// <summary>
    /// US5MA1BO portrayed with the S-101 catalogue, once for the class. The
    /// cell has no beacon, so its wreck is made a red stake lateral beacon
    /// (<see cref="SyntheticS57Cell.WriteWithLateralBeacon"/>).
    /// </summary>
    private sealed record Cell(VectorPortrayalResult Result, IReadOnlyList<FeatureSummary> Features);

    private static async Task<Cell> PortrayAsync()
    {
        var directory = Directory.CreateTempSubdirectory("pick-drawn-as-").FullName;
        var path = SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000");
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            path,
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.All } });
        return new Cell(result, [.. processor.EnumerateFeatures()]);
    }

    private static string Fixture(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, "tests", "datasets", .. parts]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    /// <summary>The cell on the stack, a legend over it, and Object information with Drawn as.</summary>
    private sealed class Scene : IDisposable
    {
        private Scene(Cell cell, PaletteType palette)
        {
            Cell = cell;
            Chart = Legend.Add("US5MA1BO.000", "S-57", 42, -71, 43, -70);
            Legend.Loader.SetEntries([.. Entries(cell.Result, Chart.Id.Value)]);
            Service = Legend.Create(palette);
            Glyphs = new ChartReferenceGlyphCache(palette);
            Terms = new ReferenceTerms(Glyphs, _ => { }, spec => Catalogues.Service.GetIndexAsync(spec), Catalogues.Service.GetArtwork);
            DrawnAs = new PickDrawnAsViewModel(Service.DrawnParts, Terms, Catalogues.Service.GetArtwork, action => action());
            Report = TestPickReport.Create();
            Report.AttachLegend(Service);
            Report.AttachDrawnAs(DrawnAs);
        }

        public static async Task<Scene> CreateAsync(PaletteType palette = PaletteType.Day) =>
            new(await Portrayed.Value, palette);

        public Cell Cell { get; }

        public LegendServiceTests.Fixture Legend { get; } = new();

        public ChartReferenceServiceTests.Fixture Catalogues { get; } = new("S-101");

        public DatasetEntry Chart { get; }

        public LegendService Service { get; }

        public ChartReferenceGlyphCache Glyphs { get; }

        public ReferenceTerms Terms { get; }

        public PickDrawnAsViewModel DrawnAs { get; }

        public PickReportViewModel Report { get; }

        /// <summary>A pick of the first feature of one of <paramref name="types"/> that draws something.</summary>
        public PickHit Hit(params string[] types)
        {
            foreach (var feature in Cell.Features.Where(f => types.Contains(f.FeatureType, StringComparer.OrdinalIgnoreCase)))
            {
                var hit = new PickHit
                {
                    FeatureType = feature.FeatureType,
                    FeatureRef = feature.FeatureRef,
                    DatasetId = Chart.Id.Value,
                    ProductSpec = "S-57",
                };
                if (Service.DrawnParts(hit) is not null)
                    return hit;
            }

            throw new InvalidOperationException(
                $"No drawn {string.Join('/', types)} in US5MA1BO; its types: "
                + string.Join(", ", Cell.Features.Select(f => f.FeatureType).Distinct().Order()));
        }

        /// <summary>Picks <paramref name="hit"/> and waits for its names and glyphs.</summary>
        public async Task PickAsync(PickHit hit)
        {
            Report.SetPicks([hit]);
            await DrawnAs.Loading;
        }

        public void Dispose()
        {
            DrawnAs.Dispose();
            Glyphs.Dispose();
            Service.Dispose();
            Catalogues.Dispose();
        }
    }

    private static IEnumerable<LayerStackEntry> Entries(VectorPortrayalResult result, string datasetId) =>
        result.SubLayers.Select(sub => new LayerStackEntry(
            new MemoryLayer(sub.LayerName),
            new SubLayerStackItem(new VectorStackPayload(result, sub), sub.Plane, 0, datasetId, sub.SourceFeatureType)));

    [AvaloniaFact]
    public async Task A_lateral_beacon_lists_its_symbol_as_a_term_and_shows_its_glyph()
    {
        using var scene = await Scene.CreateAsync();
        var hit = scene.Hit("BCNLAT", "LateralBeacon");
        Assert.Equal("LateralBeacon", hit.FeatureType);

        await scene.PickAsync(hit);

        var drawnAs = scene.DrawnAs;
        Assert.True(drawnAs.HasParts);
        var symbol = drawnAs.Parts.First(p => p.Part.Kind == PickDrawnPartKind.Symbol);
        Assert.Equal(drawnAs.Drawing!.Main, symbol.Part);
        Assert.StartsWith("BCN", symbol.Part.ItemId, StringComparison.Ordinal);
        Assert.Equal(new ReferenceKey(new SpecRef("S-101", default), ReferenceKind.Symbol, symbol.Part.ItemId!), symbol.Key);
        Assert.Equal(hit.FeatureRef, symbol.Part.FeatureReference);
        // Named from the index: "BCNLAT21 · Minor lateral beacon, …".
        Assert.StartsWith(symbol.Part.ItemId + " · ", symbol.Label, StringComparison.Ordinal);
        Assert.True(symbol.HasGlyph);
        Assert.False(symbol.IsFallback);

        // A topmark the beacon's rule draws is listed as a symbol of its own.
        if (scene.Cell.Result.SubLayers.SelectMany(s => s.Instructions).OfType<PointInstruction>()
                .Any(p => p.FeatureReference == hit.FeatureRef && p.SymbolReference?.StartsWith("TOP", StringComparison.Ordinal) == true))
        {
            Assert.Contains(drawnAs.Parts, p => p.Part.ItemId?.StartsWith("TOP", StringComparison.Ordinal) == true);
        }

        // The header shows the symbol, not the generic icon.
        Assert.True(drawnAs.HasGlyph);
        Assert.Equal("Drawn as " + symbol.Label, drawnAs.GlyphName);
    }

    [AvaloniaFact]
    public async Task A_depth_area_shows_a_patch_and_a_coastline_a_line_sample()
    {
        using var scene = await Scene.CreateAsync();

        await scene.PickAsync(scene.Hit("DEPARE", "DepthArea"));

        var area = scene.DrawnAs.Drawing!.Main!;
        Assert.Equal(PickDrawnPartKind.ColourFill, area.Kind);
        Assert.StartsWith("DEP", area.Colour, StringComparison.Ordinal);
        var fill = scene.DrawnAs.Parts.Single(p => p.Part == area);
        Assert.Equal("Colour fill: " + area.Colour, fill.Label);
        Assert.Null(fill.Key);
        Assert.True(fill.HasGlyph);
        Assert.True(scene.DrawnAs.HasGlyph);

        await scene.PickAsync(scene.Hit("COALNE", "Coastline"));

        var line = scene.DrawnAs.Drawing!.Main!;
        Assert.Contains(line.Kind, new[] { PickDrawnPartKind.Line, PickDrawnPartKind.LineStyle });
        Assert.True(scene.DrawnAs.Parts.Single(p => p.Part == line).HasGlyph);
        Assert.True(scene.DrawnAs.HasGlyph);
    }

    [AvaloniaFact]
    public async Task A_fallback_symbol_says_so()
    {
        using var scene = await Scene.CreateAsync();
        var real = scene.Cell.Result;
        var sub = new VectorSubLayer
        {
            LayerKey = "s101.unknown",
            LayerName = "Unknown",
            Instructions =
            [
                new PointInstruction { FeatureReference = "unknown-1", SymbolReference = "BCNDEF13" },
                new TextInstruction { FeatureReference = "unknown-1", Text = "No 3" },
            ],
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "s101.unknown",
        };
        scene.Legend.Loader.SetEntries(
        [
            new LayerStackEntry(
                new MemoryLayer("unknown"),
                new SubLayerStackItem(new VectorStackPayload(real, sub), sub.Plane, 0, scene.Chart.Id.Value, sub.SourceFeatureType)),
        ]);
        scene.Legend.Loader.FireLayerStackChanged();

        await scene.PickAsync(new PickHit { FeatureType = "BCNLAT", FeatureRef = "unknown-1", DatasetId = scene.Chart.Id.Value, ProductSpec = "S-57" });

        var parts = scene.DrawnAs.Parts;
        Assert.Equal([PickDrawnPartKind.Symbol, PickDrawnPartKind.Text], parts.Select(p => p.Part.Kind));
        Assert.True(parts[0].IsFallback);
        Assert.Equal("The catalogue's fallback symbol: nothing else matched", parts[0].FallbackNote);
        Assert.EndsWith(". The catalogue's fallback symbol: nothing else matched", parts[0].AccessibleName, StringComparison.Ordinal);
        Assert.Equal("Text: No 3", parts[1].Label);
        Assert.False(parts[1].IsFallback);
        Assert.False(parts[1].HasGlyph);
    }

    [AvaloniaFact]
    public async Task After_a_palette_switch_the_glyphs_are_drawn_again()
    {
        using var scene = await Scene.CreateAsync();
        await scene.PickAsync(scene.Hit("BCNLAT", "LateralBeacon"));
        var day = scene.DrawnAs.Glyph;
        var dayTile = scene.DrawnAs.Tile;
        var dayPart = scene.DrawnAs.Parts[0].Glyph;
        Assert.NotNull(day);

        scene.Glyphs.SetPalette(PaletteType.Night);
        await scene.DrawnAs.Loading;

        Assert.NotNull(scene.DrawnAs.Glyph);
        Assert.NotSame(day, scene.DrawnAs.Glyph);
        Assert.NotSame(dayPart, scene.DrawnAs.Parts[0].Glyph);
        Assert.NotEqual(dayTile.ToString(), scene.DrawnAs.Tile.ToString());
        Assert.Same(scene.DrawnAs.Tile, scene.DrawnAs.Parts[0].Tile);
    }

    [AvaloniaFact]
    public async Task An_S102_pick_keeps_its_key_line_and_has_no_drawn_parts()
    {
        using var scene = await Scene.CreateAsync();
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-102", Specification.CreatePortrayalCatalogueSource("S-102"));
        using var processor = new S102DatasetProcessor(
            Fixture("S102", "102US004MI1CI262227.h5"), catalogues, TestLuaEngine.Create(), new ProjNetCrsTransformFactory());
        var coverage = await processor.BuildCoveragePortrayalAsync(
            new RenderContext { Palette = PaletteType.Day }, TestContext.Current.CancellationToken);
        var bathymetry = scene.Legend.Add("102US004MI1CI262227.h5", "S-102", 42, -71, 43, -70);
        scene.Legend.Loader.SetEntries(
        [
            .. Entries(scene.Cell.Result, scene.Chart.Id.Value),
            .. coverage.SubLayers.Select(sub => new LayerStackEntry(
                new MemoryLayer(sub.LayerName),
                new SubLayerStackItem(new CoverageStackPayload(coverage, sub), sub.Plane, 0, bathymetry.Id.Value, sub.SourceFeatureType))),
        ]);
        scene.Legend.Loader.FireLayerStackChanged();
        var depth = new PickHit
        {
            FeatureType = "BathymetryCoverage",
            FeatureRef = "(10,10)",
            DatasetId = bathymetry.Id.Value,
            ProductSpec = "S-102",
            Attributes = [new PickAttribute { Code = "depth", Name = "Depth", RawValue = "5" }],
        };

        await scene.PickAsync(depth);

        // The key line is the Legend's, as before Drawn as: Object information
        // shows what FindRow matches, and Drawn as stays out of the way.
        Assert.Equal(
            scene.Service.FindRow(depth, latitude: null)?.Row.Label,
            Assert.Single(scene.Report.LegendKey.Select(l => l.RowLabel).DefaultIfEmpty()));
        Assert.False(scene.DrawnAs.HasParts);
        Assert.False(scene.DrawnAs.HasGlyph);

        // A catalogue-portrayed pick has parts and no key line.
        await scene.PickAsync(scene.Hit("BCNLAT", "LateralBeacon"));
        Assert.True(scene.DrawnAs.HasParts);
        Assert.False(scene.Report.HasLegendKey);
    }

    [AvaloniaFact]
    public async Task The_report_shows_the_glyph_and_the_parts_as_terms()
    {
        using var scene = await Scene.CreateAsync();
        await scene.PickAsync(scene.Hit("BCNLAT", "LateralBeacon"));
        using var host = ViewHost.Show(new PickReportView { DataContext = scene.Report }, width: 420, height: 1200);
        host.Settle();

        Assert.True(host.IsShown("PickReport.DrawnAs"));
        Assert.Equal("Drawn as", AutomationProperties.GetName(host.Find<Border>("PickReport.DrawnAs")));
        var parts = host.FindAll<Grid>("PickReport.DrawnAs.Part");
        Assert.Equal(scene.DrawnAs.Parts.Count, parts.Count);
        Assert.Equal(scene.DrawnAs.Parts[0].AccessibleName, AutomationProperties.GetName(parts[0]));
        var term = host.Find<ReferenceTerm>("Reference.Term", parts[0]);
        Assert.Equal(scene.DrawnAs.Parts[0].Key, term.Key);
        Assert.True(term.IsTerm);

        var glyph = host.Find<Image>("PickReport.Glyph");
        Assert.True(host.IsShown("PickReport.Glyph"));
        Assert.NotNull(glyph.Source);
        Assert.Equal(scene.DrawnAs.GlyphName, AutomationProperties.GetName(glyph));
    }
}
