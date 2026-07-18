using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 s57 convert</c>: converting an S-57 base cell to an S-101
/// dataset via the CLI.
/// </summary>
public sealed class S57ConvertCommandTests : IDisposable
{
    private readonly string _outputDir = Directory.CreateTempSubdirectory("s57-convert-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_outputDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [SkippableFact]
    public void Convert_writes_a_readable_s101_dataset()
    {
        var source = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(source), $"Fixture not found: {source}");

        var output = Path.Combine(_outputDir, "converted.000");

        int exit = CliApp.Build().Run(["s57", "convert", "-o", output, source]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(output), "Expected the converted S-101 dataset to be written.");
        Assert.True(new FileInfo(output).Length > 0, "Converted dataset should not be empty.");
    }

    [Fact]
    public void Convert_missing_source_returns_validation_error()
    {
        var output = Path.Combine(_outputDir, "converted.000");

        int exit = CliApp.Build().Run(["s57", "convert", "-o", output, "does-not-exist.000"]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [SkippableFact]
    public void Convert_missing_output_option_returns_validation_error()
    {
        var source = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(source), $"Fixture not found: {source}");

        int exit = CliApp.Build().Run(["s57", "convert", source]);

        Assert.NotEqual(0, exit);
    }

    [SkippableFact]
    public void Convert_missing_output_directory_returns_validation_error()
    {
        var source = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(source), $"Fixture not found: {source}");

        var missingDir = Path.Combine(_outputDir, "missing");
        var output = Path.Combine(missingDir, "converted.000");

        int exit = CliApp.Build().Run(["s57", "convert", "-o", output, source]);

        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(missingDir));
        Assert.False(File.Exists(output));
    }

    [SkippableFact]
    public void Convert_report_writes_json_diagnostics()
    {
        var source = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(source), $"Fixture not found: {source}");

        var output = Path.Combine(_outputDir, "converted.000");
        var report = Path.Combine(_outputDir, "report.json");

        int exit = CliApp.Build().Run(["s57", "convert", "-o", output, "--report", report, source]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(report), "Expected the diagnostics report to be written.");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("featuresEmitted", out _));
        Assert.True(root.TryGetProperty("unmappedObjectClasses", out _));
        Assert.Equal(0, root.GetProperty("updatesApplied").GetArrayLength());
    }

    [SkippableFact]
    public void Convert_report_missing_directory_returns_validation_error()
    {
        var source = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(source), $"Fixture not found: {source}");

        var output = Path.Combine(_outputDir, "converted.000");
        var report = Path.Combine(_outputDir, "missing", "report.json");

        int exit = CliApp.Build().Run(["s57", "convert", "-o", output, "--report", report, source]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(report));
    }

    [SkippableFact]
    public void Convert_no_updates_still_writes_dataset()
    {
        var source = FixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(source), $"Fixture not found: {source}");

        var output = Path.Combine(_outputDir, "converted.000");

        int exit = CliApp.Build().Run(["s57", "convert", "--no-updates", "-o", output, source]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(output));
    }
}
