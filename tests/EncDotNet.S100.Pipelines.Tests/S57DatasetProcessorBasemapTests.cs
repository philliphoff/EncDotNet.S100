using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression test for issue #735: the S-57 single-dataset headless render
/// (<c>s100 render &lt;cell&gt;.000 --basemap offline</c>) never forwarded the
/// render context's <see cref="BasemapKind"/>, so it drew no land basemap.
/// </summary>
public class S57DatasetProcessorBasemapTests
{
    // Boston and central Massachusetts. The US5MA1BO harbour cell covers only a
    // small part of the east edge, so the frame's centre-west is inland and
    // holds no chart data.
    private static readonly Viewport MassachusettsViewport = new()
    {
        MinLongitude = -73.5,
        MaxLongitude = -70.5,
        MinLatitude = 41.5,
        MaxLatitude = 43.5,
        WidthPixels = 300,
        HeightPixels = 300,
        ScaleDenominator = 2_000_000,
    };

    [Fact]
    public async Task RenderHeadless_OfflineBasemap_PaintsLandOutsideTheCell()
    {
        var fixturePath = ResolveFixturePath("US5MA1BO.000");
        Assert.SkipUnless(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var featureCatalogueManager = new FeatureCatalogueManager(
            spec => Specification.TryOpenFeatureCatalogue(spec));
        var processor = new S57DatasetProcessor(
            fixturePath, catalogueManager, new MoonSharpLuaEngine(), featureCatalogueManager);

        using var none = await processor.RenderHeadlessAsync(
            300, 300, new S101RenderContext { Viewport = MassachusettsViewport, Basemap = BasemapKind.None });
        using var offline = await processor.RenderHeadlessAsync(
            300, 300, new S101RenderContext { Viewport = MassachusettsViewport, Basemap = BasemapKind.Offline });

        // About 72.8°W, 42.5°N: inland, west of the cell.
        const int x = 70, y = 150;
        var land = NaturalEarthBasemap.LandFill;
        Assert.Equal(SKColors.White, none.GetPixel(x, y));
        Assert.Equal(new SKColor(land.R, land.G, land.B), offline.GetPixel(x, y));
    }

    private static string ResolveFixturePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S57", "US5MA1BO", fileName);
    }
}
