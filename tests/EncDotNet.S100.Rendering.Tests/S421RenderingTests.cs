using EncDotNet.S100.Testing.Rendering;

namespace EncDotNet.S100.Rendering.Tests;

/// <summary>Visual regression tests for S-421 route plan rendering.</summary>
public sealed class S421RenderingTests
{
    [SkippableTheory]
    [InlineData("RTE-TEST-GMIN.s421.gml")]
    [InlineData("RTE-TEST-GBASIC.s421.gml")]
    [InlineData("RTE-TEST-GFULL.s421.gml")]
    public Task RoutePlan(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S421", fileName);
        Skip.IfNot(File.Exists(path), $"S-421 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 800,
            Height = 600,
        });

        // Strip the .s421 segment so the verified-snapshot filename stays clean.
        var name = fileName.Replace(".s421.gml", "");
        return TestHelpers.VerifyBitmap(bitmap).UseParameters(name);
    }
}
