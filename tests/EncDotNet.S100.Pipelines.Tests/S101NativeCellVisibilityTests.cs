using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Real-data regression cover: every committed IHO S-101 test cell, run through
/// the viewer's Mapsui render path, must produce drawing instructions and
/// leave features visible at a scale inside the cell's own display band.
/// </summary>
/// <remarks>
/// Cells 101AA00DS0006 / 0007 / 0015 declare an inverted <c>DataCoverage</c>
/// pair (<c>minimumDisplayScale</c> 22000, <c>maximumDisplayScale</c> 90000).
/// Read literally, the out-of-scale-band cutoff hid their line work, points
/// and text beyond 1:22 000, so the viewer drew nothing at 1:50 000 while the
/// headless renderer (which applies no band) drew the cell.
/// </remarks>
public class S101NativeCellVisibilityTests
{
    /// <summary>A scale inside every committed cell's intended 1:90 000..1:22 000 band.</summary>
    private const int InBandScaleDenominator = 50000;

    /// <summary>The committed IHO S-101 test cells.</summary>
    public static TheoryData<string> Cells()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(ResolveFixtureDirectory(), "*.000").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task NativeCell_HasVisibleFeatures_AtInBandScale(string cell)
    {
        var processor = CreateFactory().CreateProcessor(Path.Combine(ResolveFixtureDirectory(), cell));
        using var lifetime = processor as IDisposable;
        var renderer = new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory());

        var result = await renderer.RenderAsync(processor);

        var cellMinimum = Assert.IsType<int>(result.CellMinimumDisplayScale);
        Assert.True(
            cellMinimum >= InBandScaleDenominator,
            $"{cell}: cell band ends at 1:{cellMinimum}, so it is hidden at 1:{InBandScaleDenominator}.");

        var features = result.Layers.OfType<MemoryLayer>().SelectMany(static l => l.Features).ToList();
        Assert.NotEmpty(features);

        var latitudeRadians = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(
            (result.Extent.MinY + result.Extent.MaxY) / 2.0);
        var resolution = MapsuiDisplayListRenderer.DenominatorToResolution(InBandScaleDenominator, latitudeRadians);

        // Line work, points and text carry the out-of-scale-band cap; at least
        // one of them must survive it at an in-band scale.
        var lineworkVisible = result.Layers
            .OfType<MemoryLayer>()
            .Where(static l => l.Name.Contains("(lines)", StringComparison.Ordinal))
            .SelectMany(static l => l.Features)
            .Count(f => IsVisibleAt(f, resolution));
        Assert.True(lineworkVisible > 0, $"{cell}: no line/point/text feature is visible at 1:{InBandScaleDenominator}.");
    }

    private static bool IsVisibleAt(IFeature feature, double resolution) =>
        feature.Styles.Any(s => s.Enabled && s.MinVisible <= resolution && resolution <= s.MaxVisible);

    private static DatasetPipelineFactory CreateFactory()
    {
        var catalogueManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                catalogueManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider());
    }

    /// <summary>Walks up from the test assembly to find the committed S-101 fixture directory.</summary>
    private static string ResolveFixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S101", "S-101", "DATASET_FILES");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Committed S-101 fixture directory not found.");
    }
}
