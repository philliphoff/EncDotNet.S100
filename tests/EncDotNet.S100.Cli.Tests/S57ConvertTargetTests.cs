using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.TestSupport;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the product <c>s100 s57 convert</c> writes (issue #608): S-401 for
/// an inland ENC (DSID <c>PRSP</c> = 10), S-101 otherwise, and the
/// <c>--target</c> override. The inland cell is the NOAA fixture with its
/// <c>PRSP</c> byte rewritten (<see cref="SyntheticS57Cell"/>).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class S57ConvertTargetTests : IDisposable
{
    private const byte MaritimeEnc = 1;
    private const byte InlandEnc = 10;

    private readonly string _dir = Directory.CreateTempSubdirectory("s57-convert-target-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData(InlandEnc, new string[0], "S-401", "1.3.0")]
    [InlineData(MaritimeEnc, new string[0], "S-101", "1.0.0")]
    [InlineData(InlandEnc, new[] { "--target", "auto" }, "S-401", "1.3.0")]
    [InlineData(InlandEnc, new[] { "--target", "s101" }, "S-101", "1.0.0")]
    [InlineData(MaritimeEnc, new[] { "--target", "S-401" }, "S-401", "1.3.0")]
    public void Convert_writes_the_chosen_product(
        byte productSpecification, string[] targetArgs, string expectedSpec, string expectedEdition)
    {
        var source = SyntheticS57Cell.Write(_dir, "U37TEST.000", productSpecification);
        var output = Path.Combine(_dir, "converted.000");
        var report = Path.Combine(_dir, "report.json");

        var (exit, _) = Run(["s57", "convert", .. targetArgs, "-o", output, "--report", report, source]);

        Assert.Equal(0, exit);

        var identification = S101Dataset.Open(output).Document.Identification;
        Assert.Equal(expectedSpec, identification.ProductSpecification);
        Assert.Equal(expectedEdition, identification.ProductSpecificationEdition);
        Assert.Equal(expectedSpec, DatasetPipelineFactory.DetectProductSpec(output));

        using var json = JsonDocument.Parse(File.ReadAllText(report));
        var root = json.RootElement;
        Assert.Equal(expectedSpec, root.GetProperty("product").GetString());
        Assert.Equal(expectedEdition, root.GetProperty("productEdition").GetString());
        Assert.Equal(
            productSpecification == InlandEnc ? "S-401" : "S-101",
            root.GetProperty("detectedProduct").GetString());
    }

    [Fact]
    public void Convert_inland_cell_reports_s401_in_summary()
    {
        var source = SyntheticS57Cell.Write(_dir, "U37TEST.000", InlandEnc);
        var output = Path.Combine(_dir, "converted.000");

        var (exit, stdout) = Run(["s57", "convert", "-o", output, source]);

        Assert.Equal(0, exit);
        Assert.Contains("as S-401 1.3.0", Flatten(stdout));
        Assert.DoesNotContain("Warning", stdout);
    }

    [Fact]
    public void Convert_forcing_s401_on_maritime_cell_warns()
    {
        var source = SyntheticS57Cell.Write(_dir, "US5TEST.000", MaritimeEnc);
        var output = Path.Combine(_dir, "converted.000");

        var (exit, stdout) = Run(["s57", "convert", "--target", "s401", "-o", output, source]);

        Assert.Equal(0, exit);
        var text = Flatten(stdout);
        Assert.Contains("Warning:", text);
        Assert.Contains("does not declare the inland ENC product specification", text);
        Assert.Contains("as S-401 1.3.0", text);
    }

    [Fact]
    public void Convert_maritime_cell_does_not_warn()
    {
        var source = SyntheticS57Cell.Write(_dir, "US5TEST.000", MaritimeEnc);
        var output = Path.Combine(_dir, "converted.000");

        var (exit, stdout) = Run(["s57", "convert", "-o", output, source]);

        Assert.Equal(0, exit);
        Assert.Contains("as S-101 1.0.0", Flatten(stdout));
        Assert.DoesNotContain("Warning", stdout);
    }

    [Fact]
    public void Convert_invalid_target_returns_validation_error()
    {
        var source = SyntheticS57Cell.Write(_dir, "US5TEST.000", MaritimeEnc);
        var output = Path.Combine(_dir, "converted.000");

        var (exit, _) = Run(["s57", "convert", "--target", "s102", "-o", output, source]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(output));
    }

    /// <summary>
    /// Runs the CLI with the static <see cref="AnsiConsole"/> (which the command
    /// writes through) redirected to a wide, colourless in-memory console.
    /// </summary>
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
            var exit = CliApp.Build(console: console).Run(args);
            return (exit, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = original;
        }
    }

    private static string Flatten(string text) => text.ReplaceLineEndings(" ");
}
