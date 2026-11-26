using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S57;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 nis convert</c>: choosing a cell, argument validation, and
/// converting Esri's sample NIS end to end. The sample is downloaded by
/// tools/nis-fixtures/fetch-tutorial-nis.sh and never committed; the end-to-end
/// tests skip when it's absent.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class NisConvertCommandTests : IDisposable
{
    private static readonly string[] Cells = ["US5CA98M.005", "US5NY1DM.010"];

    private readonly string _outputDir = Directory.CreateTempSubdirectory("nis-convert-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_outputDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string TutorialPath
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("ENC_DOTNET_NIS_FIXTURES");
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "encdotnet-nis-fixtures");
            return Path.Combine(root, "TUTORIAL_NIS.gdb");
        }
    }

    private static void SkipUnlessTutorial()
        => Assert.SkipUnless(Directory.Exists(TutorialPath), $"Esri's sample NIS isn't at {TutorialPath}. Run tools/nis-fixtures/fetch-tutorial-nis.sh.");

    [Theory]
    [InlineData("US5CA98M.005", "US5CA98M.005")]
    [InlineData("us5ca98m", "US5CA98M.005")]
    [InlineData("US5NY1DM", "US5NY1DM.010")]
    public void A_cell_is_chosen_by_full_name_or_without_extension(string cell, string expected)
    {
        Assert.True(NisConvertCommand.TryChooseCell(cell, Cells, out var chosen, out _));
        Assert.Equal(expected, chosen);
    }

    [Fact]
    public void Choosing_needs_a_cell_when_there_are_several()
    {
        Assert.False(NisConvertCommand.TryChooseCell(null, Cells, out _, out var error));
        Assert.Contains("--cell", error);
        Assert.False(NisConvertCommand.TryChooseCell("XX5NONE1", Cells, out _, out error));
        Assert.Contains("US5CA98M.005", error);
        Assert.True(NisConvertCommand.TryChooseCell(null, ["XX5ONLY1.000"], out var only, out _));
        Assert.Equal("XX5ONLY1.000", only);
    }

    [Fact]
    public void A_missing_geodatabase_is_an_argument_error()
    {
        var (exit, stdout) = Run(["nis", "convert", "--to", "s57", "-o", Path.Combine(_outputDir, "x.000"), Path.Combine(_outputDir, "missing.gdb")]);

        Assert.NotEqual(0, exit);
        Assert.Contains("not found", stdout);
    }

    [Fact]
    public void A_directory_that_isnt_a_geodatabase_is_an_error()
    {
        var (exit, stdout) = Run(["nis", "convert", "--to", "s57", "-o", Path.Combine(_outputDir, "x.000"), _outputDir]);

        Assert.Equal(1, exit);
        Assert.Contains("not a file geodatabase", stdout);
    }

    [Fact]
    public void List_prints_the_cells()
    {
        SkipUnlessTutorial();

        var (exit, stdout) = Run(["nis", "convert", "--list", TutorialPath]);

        Assert.Equal(0, exit);
        Assert.Contains("US5CA98M.005", stdout);
        Assert.Contains("4190", stdout);
        Assert.Contains("US5NY1DM.010", stdout);
    }

    [Fact]
    public void Convert_writes_a_cell_and_a_report()
    {
        SkipUnlessTutorial();
        var output = Path.Combine(_outputDir, "US5CA98M.000");
        var report = Path.Combine(_outputDir, "report.json");

        var (exit, stdout) = Run(["nis", "convert", "--to", "s57", "--cell", "US5CA98M", "--issue-date", "20261126", "-o", output, "--report", report, TutorialPath]);

        Assert.True(exit == 0, stdout);
        Assert.Contains("as S-57 3.1", stdout);
        var document = S57DocumentReader.Read(File.ReadAllBytes(output));
        Assert.Equal("US5CA98M.000", document.DataSetIdentification?.DataSetName);
        Assert.Equal("20261126", document.DataSetIdentification?.IssueDate);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal(document.FeatureRecords.Count, json.RootElement.GetProperty("featuresWritten").GetInt32());
        Assert.Equal("US5CA98M.005", json.RootElement.GetProperty("sourceCell").GetString());

        var (validateExit, validate) = Run(["validate", output]);
        Assert.True(validateExit == 0, validate);
    }

    [Fact]
    public void Convert_writes_an_exchange_set()
    {
        SkipUnlessTutorial();

        var (exit, stdout) = Run(["nis", "convert", "--to", "s57", "--cell", "US5NY1DM", "--exchange-set", "-o", _outputDir, TutorialPath]);

        Assert.True(exit == 0, stdout);
        Assert.True(File.Exists(Path.Combine(_outputDir, "ENC_ROOT", "CATALOG.031")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "ENC_ROOT", "US5NY1DM", "US5NY1DM.000")));
    }

    [Fact]
    public void Convert_without_a_cell_lists_the_choices()
    {
        SkipUnlessTutorial();

        var (exit, stdout) = Run(["nis", "convert", "--to", "s57", "-o", Path.Combine(_outputDir, "x.000"), TutorialPath]);

        Assert.Equal(1, exit);
        Assert.Contains("US5CA98M.005", stdout);
        Assert.Contains("--cell", stdout);
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
