using EncDotNet.S100.Testing.Rendering;
using VerifyTests;

namespace EncDotNet.S100.Rendering.Tests;

/// <summary>
/// Visual regression tests for S-102 bathymetric surface rendering.
/// </summary>
public sealed class S102RenderingTests
{
    [SkippableFact]
    public Task BathymetricSurface_DepthShading_DefaultPalette()
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S102", "102US004MI1CI262227.h5");
        Skip.IfNot(File.Exists(path), $"S-102 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }
}
