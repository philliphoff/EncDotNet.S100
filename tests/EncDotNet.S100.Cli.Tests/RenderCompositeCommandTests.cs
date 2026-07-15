using EncDotNet.S100.Cli.Infrastructure;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end smoke tests for the composite (<c>--layer</c>) grammar of
/// <c>s100 render</c>, exercised in-process against the committed synthetic
/// S-124 and S-125 GML fixtures (mirroring the facade's composite test).
/// </summary>
public sealed class RenderCompositeCommandTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string S124 => Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_surface.gml");
    private static string S125 => Path.Combine(AppContext.BaseDirectory, "TestData", "S125", "aton_point.gml");

    [SkippableFact]
    public void Composite_two_layers_writes_a_valid_png_at_requested_dimensions()
    {
        Skip.IfNot(File.Exists(S124) && File.Exists(S125), "S-124 and S-125 fixtures not both present.");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-comp-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", "--layer", S124, "--layer", S125, output, "--width", "320", "--height", "240"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            var bytes = File.ReadAllBytes(output);
            Assert.Equal(PngSignature, bytes[..PngSignature.Length]);

            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
            Assert.Equal(320, bitmap!.Width);
            Assert.Equal(240, bitmap.Height);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [SkippableFact]
    public void Composite_with_output_option_writes_a_valid_png()
    {
        Skip.IfNot(File.Exists(S124) && File.Exists(S125), "S-124 and S-125 fixtures not both present.");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-comp-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", "--layer", S124, "--layer", S125, "-o", output, "--width", "256", "--height", "256"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            var bytes = File.ReadAllBytes(output);
            Assert.Equal(PngSignature, bytes[..PngSignature.Length]);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [SkippableFact]
    public void Composite_with_explicit_bbox_writes_a_valid_png()
    {
        Skip.IfNot(File.Exists(S124) && File.Exists(S125), "S-124 and S-125 fixtures not both present.");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-comp-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", "--layer", S124, "--layer", S125, output,
                 "--bbox", "-180,-85,180,85", "--width", "256", "--height", "256"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
            Assert.Equal(256, bitmap!.Width);
            Assert.Equal(256, bitmap.Height);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [SkippableFact]
    public void Composite_offline_basemap_differs_from_none_over_land()
    {
        Skip.IfNot(File.Exists(S124), "S-124 fixture not present.");

        // A bounding box over the Texas coast (Galveston) so the Natural Earth
        // 1:10m land layer certainly covers part of the frame, making the
        // offline basemap visibly change the output regardless of where the
        // chart feature itself sits.
        string[] bbox = ["--bbox", "-95.5,29.0,-94.0,30.0"];

        var none = Path.Combine(Path.GetTempPath(), $"s100-cli-bm-none-{Guid.NewGuid():N}.png");
        var offline = Path.Combine(Path.GetTempPath(), $"s100-cli-bm-off-{Guid.NewGuid():N}.png");
        try
        {
            int exitNone = CliApp.Build().Run(
                ["render", "--layer", S124, none, "--width", "256", "--height", "256",
                 .. bbox, "--basemap", "none"]);
            int exitOffline = CliApp.Build().Run(
                ["render", "--layer", S124, offline, "--width", "256", "--height", "256",
                 .. bbox, "--basemap", "offline"]);

            Assert.Equal(0, exitNone);
            Assert.Equal(0, exitOffline);
            Assert.True(File.Exists(none));
            Assert.True(File.Exists(offline));

            var noneBytes = File.ReadAllBytes(none);
            var offlineBytes = File.ReadAllBytes(offline);
            Assert.Equal(PngSignature, offlineBytes[..PngSignature.Length]);

            // The offline basemap paints land beneath the chart, so the two
            // encodings must differ.
            Assert.NotEqual(noneBytes, offlineBytes);
        }
        finally
        {
            if (File.Exists(none))
                File.Delete(none);
            if (File.Exists(offline))
                File.Delete(offline);
        }
    }

    [Fact]
    public void Render_with_invalid_basemap_returns_nonzero()
    {
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-bm-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(
            ["render", "--layer", S124, output, "--basemap", "satellite"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [SkippableFact]
    public void Composite_with_missing_layer_returns_nonzero()
    {
        Skip.IfNot(File.Exists(S124), "S-124 fixture not present.");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-comp-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(
            ["render", "--layer", S124, "--layer", "does-not-exist.gml", output]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [SkippableFact]
    public void Composite_with_two_positional_arguments_returns_nonzero()
    {
        Skip.IfNot(File.Exists(S124) && File.Exists(S125), "S-124 and S-125 fixtures not both present.");

        // With --layer, only a single positional (the output) is allowed.
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-comp-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(
            ["render", "--layer", S124, "extra-arg", output]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Bbox_in_single_dataset_form_returns_nonzero()
    {
        var dataset = Path.Combine(AppContext.BaseDirectory, "TestData", "marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(
            ["render", dataset, output, "--bbox", "-1.5,50.0,-1.0,50.5"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Center_without_scale_returns_nonzero()
    {
        var dataset = Path.Combine(AppContext.BaseDirectory, "TestData", "marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(
            ["render", "--layer", dataset, output, "--center", "-1.25,50.25"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }
}
