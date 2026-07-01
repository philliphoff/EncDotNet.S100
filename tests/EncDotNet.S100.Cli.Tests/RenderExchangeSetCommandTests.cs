using System.IO.Compression;
using EncDotNet.S100.Cli.Infrastructure;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end tests for the exchange-set / directory compositing grammar of
/// <c>s100 render</c> (issue #407), exercised in-process against the committed
/// <c>Synthetic-Renderable</c> exchange-set fixture (a real <c>CATALOG.XML</c>
/// plus tiny synthetic S-124 and S-125 GML datasets and one unsupported entry).
/// </summary>
public sealed class RenderExchangeSetCommandTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string ExchangeSetDir =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "ExchangeSet");

    private static string CataloguePath => Path.Combine(ExchangeSetDir, "CATALOG.XML");

    private static bool FixturePresent =>
        File.Exists(CataloguePath)
        && File.Exists(Path.Combine(ExchangeSetDir, "S124", "navwarn_surface.gml"))
        && File.Exists(Path.Combine(ExchangeSetDir, "S125", "aton_point.gml"));

    private static string TempOutput() =>
        Path.Combine(Path.GetTempPath(), $"s100-cli-es-{Guid.NewGuid():N}.png");

    private static void AssertValidPng(string output, int width, int height)
    {
        Assert.True(File.Exists(output));
        var bytes = File.ReadAllBytes(output);
        Assert.Equal(PngSignature, bytes[..PngSignature.Length]);
        using var bitmap = SKBitmap.Decode(output);
        Assert.NotNull(bitmap);
        Assert.Equal(width, bitmap!.Width);
        Assert.Equal(height, bitmap.Height);
    }

    [SkippableFact]
    public void Positional_directory_composites_the_whole_set()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(
                ["render", ExchangeSetDir, output, "--width", "320", "--height", "240"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 320, 240);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [SkippableFact]
    public void Positional_catalogue_file_composites_the_whole_set()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(
                ["render", CataloguePath, output, "--width", "256", "--height", "256"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 256, 256);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [SkippableFact]
    public void Explicit_exchange_set_option_is_equivalent_to_positional()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(
                ["render", "--exchange-set", ExchangeSetDir, "-o", output, "--width", "256", "--height", "256"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 256, 256);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [SkippableFact]
    public void From_alias_is_accepted()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(
                ["render", "--from", ExchangeSetDir, output, "--width", "200", "--height", "200"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 200, 200);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [SkippableFact]
    public void Only_filter_restricts_to_named_specs()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(
                ["render", ExchangeSetDir, output, "--only", "S124", "--width", "128", "--height", "128"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 128, 128);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [SkippableFact]
    public void Only_filter_matching_no_datasets_returns_nonzero()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        // The set contains S-124 and S-125 only; S-101 matches nothing.
        var output = TempOutput();
        int exit = CliApp.Build().Run(
            ["render", ExchangeSetDir, output, "--only", "S101"]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [SkippableFact]
    public void Zip_exchange_set_is_extracted_and_composited()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var zip = Path.Combine(Path.GetTempPath(), $"s100-cli-es-{Guid.NewGuid():N}.zip");
        var output = TempOutput();
        try
        {
            ZipFile.CreateFromDirectory(ExchangeSetDir, zip);

            int exit = CliApp.Build().Run(
                ["render", zip, output, "--width", "192", "--height", "192"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 192, 192);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
            if (File.Exists(zip)) File.Delete(zip);
        }
    }

    [SkippableFact]
    public void Explicit_bbox_applies_to_exchange_set_form()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(
                ["render", ExchangeSetDir, output,
                 "--bbox", "-180,-85,180,85", "--width", "256", "--height", "256"]);

            Assert.Equal(0, exit);
            AssertValidPng(output, 256, 256);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [SkippableFact]
    public void Layer_and_exchange_set_together_returns_nonzero()
    {
        Skip.IfNot(FixturePresent, "Synthetic-Renderable exchange-set fixture not present.");

        var layer = Path.Combine(ExchangeSetDir, "S124", "navwarn_surface.gml");
        var output = TempOutput();
        int exit = CliApp.Build().Run(
            ["render", "--layer", layer, "--exchange-set", ExchangeSetDir, "-o", output]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Only_without_exchange_set_returns_nonzero()
    {
        var dataset = Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_surface.gml");
        Skip.IfNot(File.Exists(dataset), $"Fixture not found: {dataset}");

        var output = TempOutput();
        int exit = CliApp.Build().Run(
            ["render", dataset, output, "--only", "S124"]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Non_exchange_set_directory_returns_nonzero()
    {
        // A directory with no CATALOG.XML is not an exchange set and cannot be
        // a single dataset file either.
        var emptyDir = Path.Combine(Path.GetTempPath(), $"s100-cli-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        var output = TempOutput();
        try
        {
            int exit = CliApp.Build().Run(["render", emptyDir, output]);
            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(emptyDir)) Directory.Delete(emptyDir, recursive: true);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
