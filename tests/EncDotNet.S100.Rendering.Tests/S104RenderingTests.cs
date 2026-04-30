using EncDotNet.S100.Testing.Rendering;

namespace EncDotNet.S100.Rendering.Tests;

/// <summary>Visual regression tests for S-104 water level rendering.</summary>
public sealed class S104RenderingTests
{
    [SkippableFact]
    public Task WaterLevel_FirstTimeStep_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot, "S104", "104US004SC1CP_20251217T12Z.h5");
        Skip.IfNot(File.Exists(path), $"S-104 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
            TimeStepIndex = 0,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }
}
