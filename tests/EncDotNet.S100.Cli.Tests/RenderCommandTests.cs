using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end smoke tests that run the <c>s100</c> CLI in-process against a
/// committed synthetic dataset fixture.
/// </summary>
public sealed class RenderCommandTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public void Render_writes_a_valid_png_at_requested_dimensions()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", dataset, output, "--width", "640", "--height", "480"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            var bytes = File.ReadAllBytes(output);
            Assert.True(bytes.Length > PngSignature.Length);
            Assert.Equal(PngSignature, bytes[..PngSignature.Length]);

            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
            Assert.Equal(640, bitmap!.Width);
            Assert.Equal(480, bitmap.Height);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_with_missing_dataset_returns_nonzero()
    {
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(["render", "does-not-exist.gml", output]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_with_bad_palette_returns_nonzero()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(["render", dataset, output, "--palette", "bogus"]);
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void ListSpecs_returns_success()
    {
        int exit = CliApp.Build().Run(["list-specs"]);
        Assert.Equal(0, exit);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("webp")]
    public void Render_infers_non_png_format_from_extension(string extension)
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.{extension}");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", dataset, output, "--width", "320", "--height", "240"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            // A PNG signature must NOT be present; the bytes must decode as an image.
            var bytes = File.ReadAllBytes(output);
            Assert.NotEqual(PngSignature, bytes[..PngSignature.Length]);

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

    [Fact]
    public void Render_honours_explicit_format_option_over_extension()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        // No extension on the output path; --format drives the encoder.
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", dataset, output, "--format", "jpeg", "--quality", "75",
                 "--width", "320", "--height", "240"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_with_unknown_format_returns_nonzero()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(["render", dataset, output, "--format", "tiff"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_with_format_extension_mismatch_returns_nonzero()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(["render", dataset, output, "--format", "jpeg"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_with_out_of_range_quality_returns_nonzero()
    {
        var dataset = FixturePath("marine_curve.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.jpg");
        int exit = CliApp.Build().Run(["render", dataset, output, "--quality", "0"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_writes_a_valid_png_for_an_s57_cell()
    {
        var dataset = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-s57-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", dataset, output, "--width", "320", "--height", "240"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            var bytes = File.ReadAllBytes(output);
            Assert.True(bytes.Length > PngSignature.Length);
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
    public void Render_applies_sibling_updates_for_an_s101_base_cell()
    {
        var basePath = FindS101BaseCellWithUpdate();
        Skip.If(basePath is null,
            "No S-101 base cell (.000) with a sibling .001 update found under IC-ENC sample data.");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-s101upd-{Guid.NewGuid():N}.png");
        try
        {
            // The default (updates applied) and --no-updates paths must both
            // render successfully; updates are best-effort and never block.
            int exit = CliApp.Build().Run(
                ["render", basePath!, output, "--width", "320", "--height", "240"]);

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
    public void Render_with_no_updates_flag_renders_s101_base_cell()
    {
        var basePath = FindS101BaseCellWithUpdate();
        Skip.If(basePath is null,
            "No S-101 base cell (.000) with a sibling .001 update found under IC-ENC sample data.");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-s101noupd-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", basePath!, output, "--no-updates", "--width", "320", "--height", "240"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_writes_a_display_list_json_document()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.json");
        try
        {
            int exit = CliApp.Build().Run(["render", dataset, output]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            var root = doc.RootElement;
            Assert.Equal("S-124", root.GetProperty("product").GetString());
            Assert.Equal("navwarn_surface.gml", root.GetProperty("dataset").GetString());

            var instructions = root.GetProperty("instructions");
            Assert.Equal(JsonValueKind.Array, instructions.ValueKind);
            Assert.Equal(
                root.GetProperty("instructionCount").GetInt32(), instructions.GetArrayLength());
            Assert.True(instructions.GetArrayLength() > 0);

            // Every instruction carries the base portrayal fields the format promises.
            foreach (var instruction in instructions.EnumerateArray())
            {
                Assert.False(string.IsNullOrEmpty(instruction.GetProperty("kind").GetString()));
                Assert.False(string.IsNullOrEmpty(instruction.GetProperty("feature").GetString()));
                Assert.True(instruction.TryGetProperty("plane", out _));
                Assert.True(instruction.TryGetProperty("drawingPriority", out _));
            }
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_display_list_json_is_deterministic()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var first = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.json");
        var second = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal(0, CliApp.Build().Run(["render", dataset, first]));
            Assert.Equal(0, CliApp.Build().Run(["render", dataset, second]));

            // Pure portrayal output: two runs over the same dataset and render
            // context must be byte-identical so the document is snapshot-testable.
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public void Render_json_via_explicit_format_over_non_image_extension()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.txt");
        try
        {
            int exit = CliApp.Build().Run(["render", dataset, output, "--format", "json"]);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal("S-124", doc.RootElement.GetProperty("product").GetString());
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_format_json_conflicting_with_image_extension_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(["render", dataset, output, "--format", "json"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_json_rejected_for_composite_form_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.json");
        int exit = CliApp.Build().Run(["render", "--layer", dataset, output]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_single_dataset_with_bbox_writes_png()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", dataset, output, "--bbox", "-80,30,-60,45", "--width", "400", "--height", "300"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));

            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
            Assert.Equal(400, bitmap!.Width);
            Assert.Equal(300, bitmap.Height);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_single_dataset_with_center_scale_writes_png()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            int exit = CliApp.Build().Run(
                ["render", dataset, output, "--center", "-70,38", "--scale", "20000000",
                 "--width", "400", "--height", "300"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Render_viewport_flags_rejected_with_format_json_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S124", "navwarn_surface.gml"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.json");
        int exit = CliApp.Build().Run(["render", dataset, output, "--bbox", "-80,30,-60,45"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Render_viewport_flags_rejected_for_coverage_product_returns_nonzero()
    {
        var dataset = FixturePath(Path.Combine("S102", "102US004MI1CI262227.h5"));
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        int exit = CliApp.Build().Run(
            ["render", dataset, output, "--bbox", "-76,38,-75,39", "--width", "200", "--height", "200"]);
        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    /// <summary>
    /// Locates an S-101 base cell (<c>….000</c>) that has at least one sibling
    /// update (<c>….001</c>) under the IC-ENC sample tree, or <c>null</c> when
    /// none is present. Resolves the root from the <c>ICENC_ROOT</c> environment
    /// variable, falling back to <c>~/Downloads/IC-ENC</c>.
    /// </summary>
    private static string? FindS101BaseCellWithUpdate()
    {
        var root = Environment.GetEnvironmentVariable("ICENC_ROOT");
        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(home, "Downloads", "IC-ENC");
        }

        if (!Directory.Exists(root))
            return null;

        foreach (var basePath in Directory.EnumerateFiles(root, "*.000", SearchOption.AllDirectories))
        {
            var updates = EncDotNet.S100.Datasets.Pipelines.S101FilesystemUpdateDiscovery
                .FindSequentialUpdates(basePath);
            if (updates.Count == 0)
                continue;

            if (EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory
                    .DetectProductSpec(basePath) == "S-101")
            {
                return basePath;
            }
        }

        return null;
    }
}
