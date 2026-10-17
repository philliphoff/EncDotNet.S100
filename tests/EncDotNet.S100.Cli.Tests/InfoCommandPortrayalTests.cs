using EncDotNet.S100.Cli.Infrastructure;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 info</c> labels the S-104 portrayal as SoundCharts' own, because
/// S-104 Ed 2.0.0 defines none, and prints no such row for products drawn from
/// an official portrayal catalogue (issue #937).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class InfoCommandPortrayalTests
{
    private const string Note = "SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology";

    private static string FixturePath(string relativePath) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", relativePath);

    [Fact]
    public void Info_labels_S104_portrayal_as_SoundCharts_own()
    {
        var dataset = FixturePath(Path.Combine("S104", "104US004SC1CO_20251217T12Z.h5"));
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("Portrayal", stdout);
        Assert.Contains(Note, stdout);
    }

    [Fact]
    public void Info_has_no_portrayal_row_for_S102()
    {
        var dataset = FixturePath(Path.Combine("S102", "102US004MI1CI262227.h5"));
        Assert.SkipUnless(File.Exists(dataset), $"Fixture not found: {dataset}");

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("SoundCharts portrayal", stdout);
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
}
