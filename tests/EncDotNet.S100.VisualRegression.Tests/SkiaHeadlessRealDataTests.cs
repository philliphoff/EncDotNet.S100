using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// End-to-end tests that render real committed GML fixtures through the new
/// headless, backend-agnostic Skia vector core
/// (<c>GmlDatasetProcessorBase.RenderHeadlessAsync</c> →
/// <c>VectorSceneBuilder</c> → <c>SkiaDisplayListRenderer</c>), with no Mapsui
/// in the pipeline. These prove the shared core lowers and rasterises actual
/// dataset display lists, not just synthetic scenes.
/// </summary>
/// <remarks>
/// Set the <c>SKIA_DUMP_DIR</c> environment variable to a writable directory to
/// also emit the rendered PNGs there (used for manual visual inspection).
/// </remarks>
public sealed class SkiaHeadlessRealDataTests
{
    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }

    private static IDisplayPlaneAuthorityProvider CreateAuthorityProvider() =>
        new DisplayPlaneAuthorityProvider();

    private static void MaybeDump(SKBitmap bitmap, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SKIA_DUMP_DIR");
        if (string.IsNullOrEmpty(dir))
            return;
        Directory.CreateDirectory(dir);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(Path.Combine(dir, name));
        data.SaveTo(fs);
    }

    private static void AssertNonBlank(SKBitmap bitmap)
    {
        // White background — assert at least one non-white pixel was painted.
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red != 255 || p.Green != 255 || p.Blue != 255)
                return;
        }
        Assert.Fail("Headless Skia render produced a blank (all-white) bitmap.");
    }

    [Fact]
    public async Task S201_AtonLight_RendersThroughSkiaCore()
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S201", "aton_light.gml");
        using var manager = CreateCatalogueManager();

        var processor = new S201DatasetProcessor(path, manager, CreateAuthorityProvider());
        using var bitmap = await processor.RenderHeadlessAsync(800, 600);

        Assert.Equal(800, bitmap.Width);
        Assert.Equal(600, bitmap.Height);
        AssertNonBlank(bitmap);
        MaybeDump(bitmap, "s201_aton_light.png");
    }

    [Fact]
    public async Task S124_NavwarnSurface_RendersThroughSkiaCore()
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S124", "navwarn_surface.gml");
        using var manager = CreateCatalogueManager();

        var processor = new S124DatasetProcessor(path, manager, CreateAuthorityProvider());
        using var bitmap = await processor.RenderHeadlessAsync(800, 600);

        AssertNonBlank(bitmap);
        MaybeDump(bitmap, "s124_navwarn_surface.png");
    }

    [SkippableFact]
    public async Task S101_Cell_RendersThroughSkiaCore()
    {
        // Committed S-101 exchange-set cell (ISO 8211). Patterns are deferred in
        // the headless core, but the dominant solid depth-area fills, lines,
        // soundings/symbols, and text are rendered.
        var path = Path.Combine(
            TestHelpers.DatasetsRoot, "S101", "S-101", "DATASET_FILES", "101AA00DS0008.000");
        Skip.IfNot(File.Exists(path), $"S-101 cell not present: {path}");

        using var manager = CreateCatalogueManager();
        var featureCatalogues = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);

        var processor = new S101DatasetProcessor(
            path, manager, new MoonSharpLuaEngine(), featureCatalogues);
        using var bitmap = await processor.RenderHeadlessAsync(1000, 800);

        Assert.Equal(1000, bitmap.Width);
        Assert.Equal(800, bitmap.Height);
        AssertNonBlank(bitmap);
        MaybeDump(bitmap, "s101_cell.png");
    }
}
