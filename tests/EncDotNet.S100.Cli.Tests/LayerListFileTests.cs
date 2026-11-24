using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Updates;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>--layers-from &lt;FILE&gt;</c> (issue #1713): datasets read
/// from a list file, for hosts with more datasets than fit on a Windows
/// command line.
/// </summary>
public sealed class LayerListFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "layer-lists-" + Guid.NewGuid().ToString("N"));

    public LayerListFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Dataset(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private string List(string name, params string[] lines)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void Read_SkipsBlankAndCommentLines_AndTrims()
    {
        var a = Dataset("a.000");
        var b = Dataset("b.h5");
        var list = List("list.txt", "# charts", "", $"  {a}  ", "   ", "\t# indented comment", b);

        Assert.Equal([a, b], LayerListFile.Read(list));
    }

    [Fact]
    public void Read_ResolvesRelativePathsAgainstTheListFolder()
    {
        var a = Dataset(Path.Combine("charts", "US5AA00A", "US5AA00A.000"));
        var list = List(Path.Combine("charts", "list.txt"), Path.Combine("US5AA00A", "US5AA00A.000"));

        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = Path.GetTempPath();
        try
        {
            Assert.Equal([a], LayerListFile.Read(list));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void Expand_AppendsListedPathsAfterLayers_InOrder()
    {
        var a = Dataset("a.000");
        var b = Dataset("b.h5");
        var c = Dataset("c.h5");
        string[] layers = [a];

        var result = LayerListFile.Expand(ref layers, [List("one.txt", b), List("two.txt", c)]);

        Assert.True(result.Successful);
        Assert.Equal([a, b, c], layers);
    }

    [Fact]
    public void Expand_RejectsAMissingList()
    {
        string[] layers = [];
        var missing = Path.Combine(_root, "missing.txt");

        var result = LayerListFile.Expand(ref layers, [missing]);

        Assert.False(result.Successful);
        Assert.Contains(missing, result.Message);
    }

    [Fact]
    public void Expand_RejectsAListWithNoDatasets()
    {
        string[] layers = [];
        var list = List("empty.txt", "# nothing yet", "");

        var result = LayerListFile.Expand(ref layers, [list]);

        Assert.False(result.Successful);
        Assert.Contains("names no datasets", result.Message);
    }

    [Fact]
    public void TilesServe_ServesTheListedDatasets()
    {
        var a = Dataset("a.000");
        var b = Dataset("b.h5");
        var settings = new TilesServeCommand.Settings { Layers = [a], LayersFrom = [List("list.txt", b)] };

        Assert.True(settings.Validate().Successful);
        Assert.True(settings.IsComposite);
        Assert.Equal([a, b], settings.Layers);

        // Validating again doesn't add the list twice.
        Assert.True(settings.Validate().Successful);
        Assert.Equal([a, b], settings.Layers);
    }

    [Fact]
    public void TilesServe_ListAlone_IsTheCompositeForm()
    {
        var a = Dataset("a.000");
        var b = Dataset("b.h5");
        var settings = new TilesServeCommand.Settings { LayersFrom = [List("list.txt", a, b)] };

        Assert.True(settings.Validate().Successful);
        Assert.Equal([a, b], settings.Layers);
    }

    [Fact]
    public void TilesServe_RejectsAListedDatasetThatDoesNotExist()
    {
        var missing = Path.Combine(_root, "missing.000");
        var settings = new TilesServeCommand.Settings { LayersFrom = [List("list.txt", missing)] };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains($"Layer file not found: {missing}", result.Message);
    }

    [Fact]
    public void TilesServe_RejectsAListWithAnExchangeSet()
    {
        var settings = new TilesServeCommand.Settings
        {
            LayersFrom = [List("list.txt", Dataset("a.000"))],
            ExchangeSet = _root,
        };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void TilesExport_ExportsTheListedDatasets()
    {
        var a = Dataset("a.000");
        var settings = new TilesExportCommand.Settings
        {
            LayersFrom = [List("list.txt", a)],
            Output = Path.Combine(_root, "out.pmtiles"),
        };

        Assert.True(settings.Validate().Successful);
        Assert.Equal([a], settings.Layers);
    }

    [Fact]
    public void Render_CompositesTheListedDatasets()
    {
        var a = Dataset("a.000");
        var b = Dataset("b.h5");
        var settings = new RenderCommand.Settings
        {
            LayersFrom = [List("list.txt", a, b)],
            OutputOption = Path.Combine(_root, "chart.png"),
            Width = 1024,
            Height = 768,
            Quality = 90,
        };

        Assert.True(settings.Validate().Successful);
        Assert.True(settings.IsComposite);
        Assert.Equal([a, b], settings.Layers);
    }

    [Fact]
    public void Identify_PicksAcrossTheListedDatasets()
    {
        var a = Dataset("a.000");
        var settings = new IdentifyCommand.Settings
        {
            Latitude = "50.1",
            Longitude = "-1.3",
            LayersFrom = [List("list.txt", a)],
        };

        Assert.True(settings.Validate().Successful);
        Assert.Equal([a], settings.Layers);
    }

    [Fact]
    public void McpServe_ServesTheListedDatasets()
    {
        var a = Dataset("a.000");
        var settings = new McpServeCommand.Settings { LayersFrom = [List("list.txt", a)] };

        Assert.True(settings.Validate().Successful);
        Assert.Equal([a], settings.Layers);
    }

    [Fact]
    public void McpServe_RejectsAMissingList()
    {
        var settings = new McpServeCommand.Settings { LayersFrom = [Path.Combine(_root, "missing.txt")] };

        Assert.False(settings.Validate().Successful);
    }

    [Theory]
    [InlineData("tiles", "serve")]
    [InlineData("tiles", "export")]
    [InlineData("render")]
    [InlineData("identify")]
    [InlineData("mcp", "serve")]
    public async Task EveryCompositeCommand_TakesTheOption(params string[] command)
    {
        var missing = Path.Combine(_root, "missing.txt");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliRunner.RunAsync(
            [.. command, "--layers-from", missing],
            new CliVersionInfo("1.0.0", "1.0.0"),
            new NoUpdates(),
            error,
            AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            }),
            standardOutput: output);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Layer list file not found", output.ToString() + error);
    }

    private sealed class NoUpdates : ICliUpdateChecker
    {
        public Task<CliUpdateNotice?> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CliUpdateNotice?>(null);
    }
}
