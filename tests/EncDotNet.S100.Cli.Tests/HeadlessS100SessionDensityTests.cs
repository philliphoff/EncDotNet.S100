using EncDotNet.S100.Cli.Infrastructure;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>render_to_image</c>'s <c>pixelDensity</c> in the headless session (map
/// rendering slice 10, #1279): width and height are device-independent, and
/// the image is that size times the density, as the viewer's capture is.
/// </summary>
public sealed class HeadlessS100SessionDensityTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [Fact]
    public async Task Render_scales_the_image_by_the_pixel_density()
    {
        Assert.SkipUnless(File.Exists(FixturePath), $"Fixture not found: {FixturePath}");

        using var catalog = new HeadlessMutableCatalog(transforms: null, factory: CliTestProcessors.Bundled);
        var outcome = await catalog.LoadAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);
        Assert.SkipWhen(outcome.Added.Count == 0, "The S-57 cell could not be loaded into the catalog.");

        using var session = new HeadlessS100Session(catalog);
        var renderer = (IImageRenderer)session;

        var standard = await renderer.RenderToPngAsync(200, 150, pixelDensity: 1.0, cancellationToken: TestContext.Current.CancellationToken);
        var hiDpi = await renderer.RenderToPngAsync(200, 150, pixelDensity: 2.0, cancellationToken: TestContext.Current.CancellationToken);

        using var standardImage = SKBitmap.Decode(standard);
        using var hiDpiImage = SKBitmap.Decode(hiDpi);
        Assert.Equal((200, 150), (standardImage.Width, standardImage.Height));
        Assert.Equal((400, 300), (hiDpiImage.Width, hiDpiImage.Height));
    }
}
