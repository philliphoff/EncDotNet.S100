using EncDotNet.S100.Cli.Infrastructure;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for opening an NIS file geodatabase directly (#1777): a <c>.gdb</c>
/// resolves to one S-57 dataset per cell, a cell path names one, and the
/// dataset commands accept both. They use Esri's sample NIS, downloaded by
/// tools/nis-fixtures/fetch-tutorial-nis.sh, and skip without it.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class NisDatasetInputTests : IDisposable
{
    private readonly string _outputDir = Directory.CreateTempSubdirectory("nis-input-").FullName;

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

    [Fact]
    public void A_geodatabase_resolves_to_one_s57_dataset_per_cell()
    {
        SkipUnlessTutorial();
        var skipped = new List<SkippedDataset>();

        var inputs = DatasetInputResolver.Resolve(TutorialPath, [], null, null, skipped, out _);

        Assert.Empty(skipped);
        Assert.Equal(["TUTORIAL_NIS.gdb/US5CA98M.005", "TUTORIAL_NIS.gdb/US5NY1DM.010"], inputs.Select(i => i.Id.Value));
        Assert.All(inputs, i => Assert.Equal("S-57", i.Spec));
        Assert.All(inputs, i => Assert.True(File.Exists(i.Path)));
    }

    [Fact]
    public void Only_filters_the_cells_out()
    {
        SkipUnlessTutorial();

        var inputs = DatasetInputResolver.Resolve(TutorialPath, [], null, "S-101", new List<SkippedDataset>(), out _);

        Assert.Empty(inputs);
    }

    [Fact]
    public void A_cell_path_layer_resolves_to_its_cell()
    {
        SkipUnlessTutorial();
        var skipped = new List<SkippedDataset>();

        var inputs = DatasetInputResolver.Resolve(null, [Path.Combine(TutorialPath, "US5NY1DM")], null, null, skipped, out _);

        var input = Assert.Single(inputs);
        Assert.Equal("TUTORIAL_NIS.gdb/US5NY1DM.010", input.Id.Value);
        Assert.Empty(skipped);

        var missing = DatasetInputResolver.Resolve(null, [Path.Combine(TutorialPath, "XX5NONE1")], null, null, skipped, out _);
        Assert.Empty(missing);
        Assert.Contains("holds no cell named XX5NONE1", Assert.Single(skipped).Warning);
    }

    [Fact]
    public void Render_info_and_validate_take_a_cell_path()
    {
        SkipUnlessTutorial();
        var image = Path.Combine(_outputDir, "tahoe.png");

        var (renderExit, render) = Run(["render", Path.Combine(TutorialPath, "US5CA98M.005"), image, "--width", "256", "--height", "256"]);
        Assert.True(renderExit == 0, render);
        Assert.True(new FileInfo(image).Length > 0);

        var (infoExit, info) = Run(["info", Path.Combine(TutorialPath, "US5CA98M")]);
        Assert.True(infoExit == 0, info);
        Assert.Contains("S-57", info);

        var (validateExit, validate) = Run(["validate", Path.Combine(TutorialPath, "US5NY1DM")]);
        Assert.True(validateExit == 0, validate);

        var (badExit, bad) = Run(["info", Path.Combine(TutorialPath, "XX5NONE1")]);
        Assert.NotEqual(0, badExit);
        Assert.Contains("holds no cell named XX5NONE1", bad);
    }

    [Fact]
    public void Convert_to_s101_writes_a_valid_dataset()
    {
        SkipUnlessTutorial();
        var output = Path.Combine(_outputDir, "101US00US5CA98M.000");

        var (exit, stdout) = Run(["nis", "convert", "--to", "s101", "--cell", "US5CA98M", "-o", output, TutorialPath]);

        Assert.True(exit == 0, stdout);
        Assert.Contains("as S-101, through S-57", stdout);
        var (validateExit, validate) = Run(["validate", output]);
        Assert.True(validateExit == 0, validate);
        Assert.Contains("S-101", validate);
    }

    [Fact]
    public void Convert_to_s101_has_no_exchange_set()
    {
        var (exit, stdout) = Run(["nis", "convert", "--to", "s101", "--exchange-set", "-o", _outputDir, _outputDir]);

        Assert.NotEqual(0, exit);
        Assert.Contains("--exchange-set applies only with --to s57", stdout);
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
