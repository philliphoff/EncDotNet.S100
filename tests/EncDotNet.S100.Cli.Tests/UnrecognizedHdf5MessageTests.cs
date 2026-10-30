using System.Reflection;
using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using PureHDF;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1092: an HDF5 file with no <c>productSpecification</c> isn't
/// assumed to be S-102, and every CLI surface (<c>info</c>, <c>validate</c>,
/// <c>identify</c> and <c>mcp serve</c>'s <c>open_dataset</c>) says why it
/// wasn't opened, in the same words.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class UnrecognizedHdf5MessageTests : IDisposable
{
    private const string Reason =
        "This HDF5 file doesn't say which S-100 product it is (no productSpecification attribute).";

    private readonly string _dir = Directory.CreateTempSubdirectory("cli-hdf5-undeclared-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>An S-102-shaped tile whose root declares no product.</summary>
    private string WriteUndeclaredS102()
    {
        var path = Path.Combine(_dir, "undeclared.h5");
        new H5File
        {
            Attributes = new() { ["horizontalCRS"] = 4326 },
            ["BathymetryCoverage"] = new H5Group
            {
                ["BathymetryCoverage.01"] = new H5Group
                {
                    Attributes = new()
                    {
                        ["gridOriginLatitude"] = 47.0,
                        ["gridOriginLongitude"] = -122.0,
                        ["gridSpacingLatitudinal"] = 0.001,
                        ["gridSpacingLongitudinal"] = 0.001,
                        ["numPointsLatitudinal"] = 2,
                        ["numPointsLongitudinal"] = 2,
                    },
                    ["Group_001"] = new H5Group { ["values"] = Enumerable.Range(0, 4).Select(i => new BathyRow { Depth = 10 + i, Uncertainty = 0.5f }).ToArray() },
                },
            },
        }.Write(path, new H5WriteOptions(FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }

    [Fact]
    public void Info_says_the_file_declares_no_product()
    {
        var path = WriteUndeclaredS102();

        var (exit, stdout) = RunCapturingAnsiConsole(["info", path]);

        Assert.Equal(2, exit);
        Assert.Contains($"Could not detect an S-100 product specification for: {path}", stdout);
        Assert.Contains(Reason, stdout);
    }

    [Fact]
    public void Validate_says_the_file_declares_no_product()
    {
        var path = WriteUndeclaredS102();

        var (exit, stdout) = RunCapturingAnsiConsole(["validate", path]);

        Assert.Equal(2, exit);
        Assert.Contains(Reason, stdout);
    }

    [Fact]
    public void Validate_json_carries_the_reason_in_the_message()
    {
        var path = WriteUndeclaredS102();

        var (exit, stdout) = RunCapturingStdout(["validate", path, "--format", "json"]);

        Assert.Equal(2, exit);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("spec-not-detected", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            $"Could not detect an S-100 product specification for: {path}. {Reason}",
            doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Identify_skips_the_file_with_the_reason()
    {
        var path = WriteUndeclaredS102();
        var original = Console.Error;
        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            CliApp.Build().Run(["identify", path, "--lat", "47.0005", "--lon", "-121.9995"], TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains($"Skipped unsupported dataset (no known product specification): {path}. {Reason}", stderr.ToString());
    }

    [Fact]
    public async Task Mcp_open_dataset_reports_the_reason_as_a_problem()
    {
        var path = WriteUndeclaredS102();
        using var catalog = new HeadlessMutableCatalog();

        var outcome = await catalog.LoadAsync(path, specHint: null, TestContext.Current.CancellationToken);

        Assert.Empty(outcome.Added);
        var problem = Assert.Single(outcome.Problems ?? []);
        Assert.EndsWith(Reason, problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spec_hint_still_opens_an_undeclared_S102_file()
    {
        // The escape hatch for a producer that omits the attribute: name the
        // product (`open_dataset` `spec`, or a Library item's declared spec).
        var path = WriteUndeclaredS102();
        using var catalog = new HeadlessMutableCatalog();

        var outcome = await catalog.LoadAsync(path, "S-102", TestContext.Current.CancellationToken);

        Assert.Single(outcome.Added);
    }

    private static (int Exit, string Stdout) RunCapturingAnsiConsole(string[] args)
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
            int exit = CliApp.Build(console: console).Run(args);
            return (exit, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = original;
        }
    }

    private static (int Exit, string Stdout) RunCapturingStdout(string[] args)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            int exit = CliApp.Build().Run(args);
            return (exit, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private struct BathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
