using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S57;
using EncDotNet.S57.ExchangeSets;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 s101 convert --to s57</c>: converting an S-101 dataset to
/// an S-57 base cell (S-65 Annex C) via the CLI, end to end on the IHO S-101
/// test datasets.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class S101ConvertCommandTests : IDisposable
{
    private const string Cell = "101AA00DS0016.000";

    private readonly string _outputDir = Directory.CreateTempSubdirectory("s101-convert-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_outputDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S101", fileName);

    public static TheoryData<string> IhoTestDatasets()
    {
        var data = new TheoryData<string>();
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "S101");
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.GetFiles(dir, "*.000").Order(StringComparer.Ordinal))
                data.Add(Path.GetFileName(path));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(IhoTestDatasets))]
    public void Convert_iho_test_dataset_reads_back_as_s57(string fileName)
    {
        var source = FixturePath(fileName);
        var output = Path.Combine(_outputDir, "converted.000");
        var report = Path.Combine(_outputDir, "report.json");

        var (exit, stdout) = Run(["s101", "convert", "--to", "s57", "-o", output, "--report", report, source]);

        Assert.True(exit == 0, stdout);
        Assert.Contains("as S-57 3.1", stdout);

        var document = S57DocumentReader.Read(File.ReadAllBytes(output));
        Assert.NotEmpty(document.FeatureRecords);
        Assert.Equal("3.1", document.DataSetIdentification?.S57EditionNumber.TrimStart('0'));

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal(document.FeatureRecords.Count, json.RootElement.GetProperty("featuresWritten").GetInt32());

        var (infoExit, info) = Run(["info", output]);
        Assert.True(infoExit == 0, info);
        Assert.Contains("S-57", info);
    }

    [Fact]
    public void Convert_without_to_defaults_to_s57()
    {
        var output = Path.Combine(_outputDir, "converted.000");

        var (exit, stdout) = Run(["s101", "convert", "-o", output, FixturePath(Cell)]);

        Assert.True(exit == 0, stdout);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Convert_prints_the_translation_summary()
    {
        var output = Path.Combine(_outputDir, "converted.000");

        var (exit, stdout) = Run(["s101", "convert", "--to", "s57", "-o", output, FixturePath(Cell)]);

        Assert.Equal(0, exit);
        Assert.Contains("S-101 features read", stdout);
        Assert.Contains("S-57 feature records written", stdout);
        Assert.Contains("Attributes dropped by Annex C", stdout);
    }

    [Fact]
    public void Convert_warns_that_an_edition_1_dataset_isnt_what_annex_c_converts()
    {
        var output = Path.Combine(_outputDir, "converted.000");

        var (exit, stdout) = Run(["s101", "convert", "--to", "s57", "-o", output, FixturePath(Cell)]);

        Assert.Equal(0, exit);
        Assert.Contains("declares S-101 Edition 1.0", stdout);
        Assert.Contains("S-65 Annex C converts Edition 2.0.0", stdout);
    }

    [Fact]
    public void Convert_round_trips_an_s57_cell_through_s101()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");
        Assert.SkipUnless(File.Exists(source), $"Fixture not found: {source}");
        var s101 = Path.Combine(_outputDir, "US5MA1BO.s101.000");
        var s57 = Path.Combine(_outputDir, "US5MA1BO.000");

        var (forwardExit, forward) = Run(["s57", "convert", "-o", s101, source]);
        var (reverseExit, reverse) = Run(["s101", "convert", "--to", "s57", "-o", s57, s101]);

        Assert.True(forwardExit == 0, forward);
        Assert.True(reverseExit == 0, reverse);
        Assert.DoesNotContain("Warning", reverse);
        var original = S57DocumentReader.Read(File.ReadAllBytes(source));
        var roundTripped = S57DocumentReader.Read(File.ReadAllBytes(s57));
        Assert.Equal(original.CoordinateMultiplicationFactor, roundTripped.CoordinateMultiplicationFactor);
        Assert.InRange(roundTripped.FeatureRecords.Count, original.FeatureRecords.Count * 9 / 10, original.FeatureRecords.Count * 11 / 10);
    }

    [Fact]
    public void Convert_report_writes_json_diagnostics_and_options()
    {
        var output = Path.Combine(_outputDir, "converted.000");
        var report = Path.Combine(_outputDir, "report.json");

        var (exit, _) = Run([
            "s101", "convert", "--to", "s57", "-o", output, "--report", report,
            "--inform-separator", " | ", "--max-inform-length", "120", "--inform-length", "truncate",
            "--national-language", "fra", "--name-without-usage", "drop", "--no-optional-inform",
            FixturePath(Cell)]);

        Assert.Equal(0, exit);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        var root = doc.RootElement;
        Assert.Equal("S-57", root.GetProperty("product").GetString());
        Assert.True(root.GetProperty("featuresRead").GetInt32() > 0);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, root.GetProperty("droppedAttributes").ValueKind);
        Assert.Equal(0, root.GetProperty("updatesApplied").GetArrayLength());

        var options = root.GetProperty("options");
        Assert.Equal(" | ", options.GetProperty("informSeparator").GetString());
        Assert.Equal(120, options.GetProperty("maxInformLength").GetInt32());
        Assert.Equal("Truncate", options.GetProperty("informLengthPolicy").GetString());
        Assert.Equal("fra", options.GetProperty("nationalLanguage").GetString());
        Assert.Equal("Drop", options.GetProperty("nameWithoutUsage").GetString());
        Assert.False(options.GetProperty("includeOptionalInform").GetBoolean());
    }

    [Fact]
    public void Convert_exchange_set_writes_a_verified_catalogue()
    {
        var root = Path.Combine(_outputDir, "set");

        var (exit, stdout) = Run([
            "s101", "convert", "--to", "s57", "--exchange-set", "--cell-name", "AA5DS016", "-o", root, FixturePath(Cell)]);

        Assert.True(exit == 0, stdout);
        var catalogPath = Path.Combine(root, "ENC_ROOT", "CATALOG.031");
        var cellPath = Path.Combine(root, "ENC_ROOT", "AA5DS016", "AA5DS016.000");
        Assert.True(File.Exists(cellPath));
        Assert.DoesNotContain("not an 8-character S-57 cell name", stdout);

        var catalog = S57CatalogReader.ReadFromFile(catalogPath);
        Assert.Equal(["CATALOG.031", "AA5DS016\\AA5DS016.000"], catalog.Entries.Select(e => e.FileName));
        var entry = catalog.Entries[1];
        Assert.Equal(S57CatalogWriter.ComputeCrc(File.ReadAllBytes(cellPath)), entry.CrcChecksum);
        Assert.NotNull(entry.SouthernmostLatitude);
        Assert.True(entry.NorthernmostLatitude > entry.SouthernmostLatitude);

        var (validateExit, validate) = Run(["validate", catalogPath]);
        Assert.True(validateExit == 0, validate);
    }

    [Fact]
    public void Convert_exchange_set_warns_on_a_non_s57_cell_name()
    {
        var root = Path.Combine(_outputDir, "set");

        var (exit, stdout) = Run(["s101", "convert", "--exchange-set", "-o", root, FixturePath(Cell)]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(root, "ENC_ROOT", "101AA00DS0016", "101AA00DS0016.000")));
        Assert.Contains("not an 8-character S-57 cell name", stdout);
    }

    [Theory]
    [InlineData("--to", "s101")]
    [InlineData("--inform-length", "shorten")]
    [InlineData("--name-without-usage", "always")]
    [InlineData("--max-inform-length", "0")]
    [InlineData("--cell-name", "AA5DS016")]
    public void Convert_invalid_option_returns_validation_error(string option, string value)
    {
        var output = Path.Combine(_outputDir, "converted.000");

        var (exit, _) = Run(["s101", "convert", option, value, "-o", output, FixturePath(Cell)]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Convert_missing_source_returns_validation_error()
    {
        var output = Path.Combine(_outputDir, "converted.000");

        var (exit, _) = Run(["s101", "convert", "--to", "s57", "-o", output, "does-not-exist.000"]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Convert_missing_output_option_returns_validation_error()
    {
        var (exit, _) = Run(["s101", "convert", "--to", "s57", FixturePath(Cell)]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Convert_missing_output_directory_returns_validation_error()
    {
        var missingDir = Path.Combine(_outputDir, "missing");
        var output = Path.Combine(missingDir, "converted.000");

        var (exit, _) = Run(["s101", "convert", "--to", "s57", "-o", output, FixturePath(Cell)]);

        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(missingDir));
    }

    [Fact]
    public void Convert_report_missing_directory_returns_validation_error()
    {
        var output = Path.Combine(_outputDir, "converted.000");
        var report = Path.Combine(_outputDir, "missing", "report.json");

        var (exit, _) = Run(["s101", "convert", "--to", "s57", "-o", output, "--report", report, FixturePath(Cell)]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(report));
    }

    private static (int Exit, string Stdout) Run(string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 1000;

        var original = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            int exit = CliApp.Build(console: console).Run(args, TestContext.Current.CancellationToken);
            return (exit, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = original;
        }
    }
}
